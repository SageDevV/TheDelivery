using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// REFAZ O AGARRÃO DO PESADELO DO ZERO (2026-11-19).
    /// <list type="number">
    /// <item>Importa <c>Models/CreatureGrabThrow.fbx</c>: a criatura re-rigada na Meshy com um movimento
    /// gerado por texto para esta cena (curva-se, agarra pelo pescoço com a direita, ergue alto,
    /// sacode e arremessa para baixo). Generic, sem loop, material Creature.mat.</item>
    /// <item>MEDE o clipe na escala da cena (amostrando um modelo oculto): a trajetória da PALMA
    /// (pulso avançado na direção antebraço→mão), o quadro do CONTATO (a palma sobe pela altura do
    /// pescoço dela depois do bote), o do ápice e o da SOLTURA (pico de velocidade do golpe para
    /// baixo) com a velocidade da palma ali.</item>
    /// <item>POSICIONA a criatura (marcador GrabPoint) de frente para a Clear, do lado do corredor,
    /// de modo que no quadro do contato a palma caia EXATAMENTE no pescoço dela parada na marca
    /// Abyss — o anexo começa sem salto.</item>
    /// <item>Troca o CreatureGrab da cena pelo novo modelo e liga tudo no NightmareDirector (ossos,
    /// tempos, velocidade, câmera do take); a queda passa a começar de onde ela está.</item>
    /// </list>
    /// Relatório em Temp/NightmareGrabRebuild.txt. Automação: Temp/NightmareGrabRebuild.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareGrabRebuild
    {
        private const string ModelPath = "Assets/_Project/Models/CreatureGrabThrow.fbx";
        private const string ControllerPath = "Assets/_Project/Animation/Controllers/CreatureGrabThrow.controller";
        private const string MaterialPath = "Assets/_Project/Materials/Creature.mat";
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string MarkerPath = "Temp/NightmareGrabRebuild.pending";
        private const float CreatureScale = 2.4f;
        private const float NeckHeight = 1.40f; // pescoço da Clear (1,65 m) de pé
        // A palma no auge de um arremesso por cima da cabeça passa de 20 m/s (braço de 4 m): herdada
        // inteira, a Clear sai do quadro num piscar. Ela é pesada e a mão escorrega — 55%.
        private const float ThrowDamping = 0.55f;
        private static double nextPoll;

        static NightmareGrabRebuild() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Run();
        }

        [MenuItem("Tools/The Delivery/Nightmare - Refazer o Agarrão")]
        public static void Run()
        {
            var report = new StringBuilder();
            AssetDatabase.Refresh();

            // 1. Import
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) { Debug.LogError("[NightmareGrabRebuild] FBX não encontrado: " + ModelPath); return; }
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips) { c.loopTime = false; c.name = "GrabThrow"; }
            importer.clipAnimations = clips;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            importer.SaveAndReimport();
            if (mat != null)
            {
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
                importer.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.layers[0].stateMachine.AddState("GrabThrow").motion = clip;
            AssetDatabase.SaveAssets();

            // 2. Medida do clipe
            if (!Measure(model, clip, out float contactN, out float releaseN, out Vector3 palmAtContact, out Vector3 palmAtRelease,
                         out Vector3 throwVel, out float apexH, out float holdN, report))
                return;

            // 3. Cena
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NightmareDirector d = null;
                foreach (var r in scene.GetRootGameObjects())
                    if ((d = r.GetComponentInChildren<NightmareDirector>(true)) != null) break;
                if (d == null) { Debug.LogError("[NightmareGrabRebuild] NightmareDirector não encontrado."); return; }
                var so = new SerializedObject(d);
                var abyss = so.FindProperty("abyssPoint").objectReferenceValue as Transform;
                var spawn = so.FindProperty("spawnPoint").objectReferenceValue as Transform;
                var marker = so.FindProperty("grabSpawnPoint").objectReferenceValue as Transform;
                var old = so.FindProperty("creatureGrabObject").objectReferenceValue as GameObject;
                if (abyss == null || marker == null) { Debug.LogError("[NightmareGrabRebuild] Falta abyssPoint ou grabSpawnPoint."); return; }

                Physics.SyncTransforms();
                float floorY = Physics.Raycast(abyss.position + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.point.y : abyss.position.y;
                // A criatura fica do lado do CORREDOR (de onde a Clear veio) e olha para ela e para o abismo.
                Vector3 toAbyss = spawn != null ? abyss.position - spawn.position : -abyss.forward;
                toAbyss.y = 0f; toAbyss.Normalize();
                Quaternion rot = Quaternion.LookRotation(toAbyss, Vector3.up);
                Vector3 neck = new Vector3(abyss.position.x, floorY + NeckHeight, abyss.position.z);
                Vector3 root = neck - rot * palmAtContact;
                root.y = floorY;
                // A BORDA DO ABISMO: anda da marca para a frente até o chão acabar.
                float edge = 0f;
                for (float s = 0f; s < 6f; s += 0.05f)
                {
                    Vector3 p = new Vector3(abyss.position.x, floorY + 1f, abyss.position.z) + toAbyss * s;
                    if (!Physics.Raycast(p, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)) { edge = s; break; }
                }
                // O EMPURRÃO: o que falta para os pés dela passarem da borda (+0,6 m) antes de
                // descerem à altura do chão, a partir da palma na soltura (o corpo pende abaixo dela).
                Vector3 relWorld = rot * palmAtRelease;
                float feet = palmAtRelease.y - NeckHeight;
                float vy = throwVel.y;
                float tFloor = (vy + Mathf.Sqrt(vy * vy + 2f * 9.81f * Mathf.Max(0.05f, feet))) / 9.81f;
                Vector3 palmWorld = root + relWorld;
                float palmAlong = Vector3.Dot(new Vector3(palmWorld.x - abyss.position.x, 0f, palmWorld.z - abyss.position.z), toAbyss);
                float need = Mathf.Max(0f, edge + 0.6f - palmAlong);
                float handFwd = Mathf.Max(0f, Vector3.Dot(rot * throwVel, toAbyss));
                float push = Mathf.Clamp(need / Mathf.Max(0.1f, tFloor) - handFwd, 0.5f, 7f);
                report.AppendLine($"Borda do abismo a {edge:0.00} m da marca. Na soltura a palma está a {palmAlong:0.00} m da marca (para o abismo), pés a {feet:0.00} m do chão; " +
                                  $"{tFloor:0.00} s até a altura do chão → empurrão para a frente {push:0.0} m/s (mão já leva {handFwd:0.0}).");
                Undo.RecordObject(marker, "Agarrão");
                marker.SetPositionAndRotation(root, rot);
                report.AppendLine($"Chão na marca Abyss: y={floorY:F2}. Pescoço da Clear: {neck:F2}.");
                report.AppendLine($"Criatura (GrabPoint): {root:F2}, yaw {rot.eulerAngles.y:0.0}° (olha para o abismo). Palma no contato a {new Vector2(palmAtContact.x, palmAtContact.z).magnitude:0.00} m à frente.");

                // Troca o modelo
                GameObject fresh = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                fresh.name = "CreatureGrab";
                if (old != null)
                {
                    fresh.transform.SetParent(old.transform.parent, false);
                    fresh.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                    fresh.SetActive(old.activeSelf);
                }
                fresh.transform.SetPositionAndRotation(root, rot);
                fresh.transform.localScale = Vector3.one * CreatureScale;
                if (!fresh.TryGetComponent(out Animator an)) an = fresh.AddComponent<Animator>();
                an.runtimeAnimatorController = ctrl;
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in fresh.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
                var bones = fresh.GetComponentsInChildren<Transform>(true);
                Transform hand = bones.FirstOrDefault(b => b.name == "RightHand");
                Transform head = bones.FirstOrDefault(b => b.name == "Head");

                so.FindProperty("creatureGrabObject").objectReferenceValue = fresh;
                so.FindProperty("grabHandBone").objectReferenceValue = hand;
                so.FindProperty("grabHeadBone").objectReferenceValue = head;
                so.FindProperty("grabYaw").floatValue = 0f;
                so.FindProperty("grabContactNormalizedTime").floatValue = contactN;
                so.FindProperty("grabReleaseNormalizedTime").floatValue = releaseN;
                so.FindProperty("grabHoldNormalizedTime").floatValue = holdN;
                so.FindProperty("grabHoldSpeed").floatValue = 0.012f;
                so.FindProperty("grabThrowVelocity").vector3Value = throwVel;
                so.FindProperty("grabPalmReach").floatValue = 0.08f * CreatureScale;
                so.FindProperty("grabThrowForward").floatValue = push;
                so.FindProperty("fallStartPoint").objectReferenceValue = null; // a queda começa de onde ela está
                so.FindProperty("fallBlinkDelay").floatValue = 0.7f;           // vê a criatura na borda antes de piscar
                so.FindProperty("grabTakeDistance").floatValue = 7f;
                so.FindProperty("grabTakeYaw").floatValue = 35f;
                so.FindProperty("grabTakeHeight").floatValue = 3.0f; // na altura dela erguida: de baixo, a cabeça da criatura a escondia no ápice
                so.FindProperty("grabTakeFov").floatValue = 50f;
                so.FindProperty("grabTakePush").floatValue = 0.8f;
                so.FindProperty("grabTakeLookHeight").floatValue = Mathf.Clamp(apexH * 0.65f, 2f, 3.2f);
                var hide = so.FindProperty("fallHideObjects");
                for (int i = 0; hide != null && i < hide.arraySize; i++)
                    if (old != null && hide.GetArrayElementAtIndex(i).objectReferenceValue == old)
                        hide.GetArrayElementAtIndex(i).objectReferenceValue = fresh;
                so.ApplyModifiedPropertiesWithoutUndo();

                if (old != null) Object.DestroyImmediate(old);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.AppendLine($"Ossos: mão '{hand?.name}', cabeça '{head?.name}'. Modelo antigo removido da cena.");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            File.WriteAllText("Temp/NightmareGrabRebuild.txt", report.ToString());
            Debug.Log("[NightmareGrabRebuild] OK\n" + report);
        }

        /// <summary>Amostra o clipe num modelo oculto (origem, sem rotação, escala da cena) e acha
        /// contato, ápice e soltura pela trajetória da palma.</summary>
        private static bool Measure(GameObject model, AnimationClip clip, out float contactN, out float releaseN,
                                    out Vector3 palmAtContact, out Vector3 palmAtRelease, out Vector3 throwVel, out float apexH, out float holdN, StringBuilder report)
        {
            contactN = releaseN = apexH = holdN = 0f; palmAtContact = palmAtRelease = throwVel = Vector3.zero;
            var go = Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one * CreatureScale;
                var bones = go.GetComponentsInChildren<Transform>(true);
                Transform hand = bones.FirstOrDefault(b => b.name == "RightHand");
                if (hand == null || hand.parent == null) { Debug.LogError("[NightmareGrabRebuild] Sem RightHand no rig."); return false; }
                float reach = 0.08f * CreatureScale;
                int n = Mathf.Max(60, Mathf.RoundToInt(clip.length * clip.frameRate));
                float dt = clip.length / n;
                var palm = new Vector3[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    clip.SampleAnimation(go, i * dt);
                    Vector3 along = (hand.position - hand.parent.position).normalized;
                    palm[i] = hand.position + along * reach;
                }
                int apex = 0;
                for (int i = 0; i <= (int)(n * 0.8f); i++) if (palm[i].y > palm[apex].y) apex = i;
                int low = 0;
                for (int i = 0; i < apex; i++) if (palm[i].y < palm[low].y) low = i;
                int contact = -1;
                for (int i = low; i <= apex; i++) if (palm[i].y >= NeckHeight) { contact = i; break; }
                if (contact < 0)
                {
                    contact = low;
                    for (int i = low; i <= apex; i++) if (Mathf.Abs(palm[i].y - NeckHeight) < Mathf.Abs(palm[contact].y - NeckHeight)) contact = i;
                }
                // O PICO do golpe é tarde demais: ali a mão já desceu e vai para trás, para as pernas
                // da criatura — soltar ali joga a Clear de volta no chão do corredor. A soltura é o
                // INÍCIO do golpe: volta do pico até a velocidade para baixo cair abaixo de 3,5 m/s.
                int peak = apex + 1; float best = 0f;
                float Down(int i) => -(palm[Mathf.Min(n, i + 1)].y - palm[i - 1].y) / (2f * dt);
                for (int i = apex + 1; i < n; i++)
                    if (Down(i) > best) { best = Down(i); peak = i; }
                int release = peak;
                while (release > apex + 1 && Down(release - 1) >= 3.5f) release--;
                // ARREMESSO POR CIMA DA CABEÇA: se depois do ápice a palma faz um arco para a FRENTE
                // (>= 3 m/s), a mão abre no ponto de MAIOR velocidade para a frente — é onde se solta
                // qualquer coisa arremessada. Sem arco (golpe só para baixo), vale o início do golpe.
                float Fwd(int i) => (palm[Mathf.Min(n, i + 1)].z - palm[i - 1].z) / (2f * dt);
                int fwdPeak = apex + 1; float fwdBest = 0f;
                for (int i = apex + 1; i < n; i++)
                    if (Fwd(i) > fwdBest) { fwdBest = Fwd(i); fwdPeak = i; }
                if (fwdBest >= 3f) release = fwdPeak;
                report.AppendLine($"Arco para a frente: pico {fwdBest:0.0} m/s em t={fwdPeak * dt:0.00}s ({(fwdBest >= 3f ? "usado como soltura" : "fraco — usa o início do golpe")}).");
                contactN = contact / (float)n;
                releaseN = release / (float)n;
                palmAtContact = palm[contact];
                throwVel = (palm[Mathf.Min(n, release + 1)] - palm[release - 1]) / (2f * dt) * ThrowDamping;
                apexH = palm[apex].y;
                // A PAUSA tem que CHEGAR no ápice: a desaceleração (0,3 s, ~0,16 s de clipe) e o fio de
                // movimento da pausa (~0,03 s) consomem clipe — começando no ápice, ela terminava já
                // dentro do arco, com a Clear embaixo. Começa 0,19 s antes.
                holdN = Mathf.Max(contact + 1, Mathf.Min(apex, release - 1) - Mathf.RoundToInt(0.19f / dt)) / (float)n;
                report.AppendLine($"PAUSA DRAMÁTICA começa em t={holdN * clip.length:0.00}s para parar no ápice ({apex * dt:0.00}s).");
                report.AppendLine($"Clipe '{clip.name}': {clip.length:0.00} s, {n} amostras.");
                report.AppendLine($"Palma: mais baixa {palm[low].y:0.00} m (t={low * dt:0.00}s), ápice {apexH:0.00} m (t={apex * dt:0.00}s).");
                report.AppendLine($"CONTATO t={contact * dt:0.00}s (norm {contactN:0.000}), palma {palmAtContact:F2}.");
                palmAtRelease = palm[release];
                report.AppendLine($"Pico do golpe t={peak * dt:0.00}s ({best:0.0} m/s para baixo).");
                report.AppendLine($"SOLTURA t={release * dt:0.00}s (norm {releaseN:0.000}), palma {palm[release]:F2}, velocidade {throwVel:F2} ({throwVel.magnitude:0.0} m/s).");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

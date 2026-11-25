using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// BRIGA NO BECO (Road, beco da rua B x≈145, adiante dos barris de fogo A_01/B_02 — saiu do beco da D em 2026-11-19 porque lá é o encontro do estranho): dois personagens da
    /// Meshy (imagem→3D t2.5 + rig + text-to-motion) — o AGRESSOR (jaqueta de couro) de pé, gritando,
    /// apontando e golpeando para baixo, e a VÍTIMA (camisa social) ajoelhada no chão, implorando e se
    /// protegendo. Perfil para quem olha da calçada; o agressor fica de costas para o fogo (silhueta) e a
    /// vítima recebe a luz do barril. Ficam a noite toda (sem toque de recolher).
    /// <para>Import: Generic, clipe de 8 s em loop, material URP/Lit com as texturas extraídas
    /// (<see cref="RoadPedestrianSetup.AssignMaterial"/>). Altura: a malha é "assada" ao longo do clipe e
    /// o personagem é deslocado para que o ponto mais baixo típico (mediana dos mínimos) toque o chão —
    /// joelhos da vítima no asfalto, botas do agressor no chão.</para>
    /// Automação: Temp/RoadAlleyFightSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadAlleyFightSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadAlleyFightSetup.pending";
        private const string RootName = "AlleyFight";
        private const float LoopFade = 1.0f;
        private static double nextPoll;

        private static readonly (string name, string fbx, Vector3 pos, Vector3 face, float height, float speed)[] Actors =
        {
            ("FightVictim", "FightVictim/FightVictim_Plead.fbx", new Vector3(143.6f, 0f, 356.5f), Vector3.right, 1.72f, 1.0f),
            ("FightThug",   "FightThug/FightThug_Threat.fbx",    new Vector3(144.95f, 0f, 356.5f), Vector3.left, 1.82f, 1.0f),
        };

        static RoadAlleyFightSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Apply();
        }

        [MenuItem("Tools/The Delivery/Road - Briga no beco")]
        public static void Apply()
        {
            AssetDatabase.Refresh();
            var report = new System.Text.StringBuilder();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == RootName) Object.DestroyImmediate(root);
                var group = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(group, scene);
                Physics.SyncTransforms();

                foreach (var a in Actors)
                {
                    string modelPath = $"Assets/_Project/Models/{a.fbx}";
                    AnimationClip clip = Prepare(a.name, modelPath, out AnimatorController controller);
                    if (clip == null) { report.AppendLine($"  {a.name}: FALHOU no import"); continue; }
                    float lift = GroundOffset(modelPath, clip, out float h);

                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    go.name = a.name;
                    go.transform.SetParent(group.transform, false);
                    Vector3 p = a.pos;
                    if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                        p.y = hit.point.y;
                    go.transform.SetPositionAndRotation(p + Vector3.up * lift, Quaternion.LookRotation(a.face, Vector3.up));

                    if (!go.TryGetComponent(out Animator an)) an = go.AddComponent<Animator>();
                    an.runtimeAnimatorController = controller;
                    an.applyRootMotion = false;
                    an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    an.speed = a.speed;

                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.radius = 0.32f;
                    cap.height = Mathf.Max(0.9f, h);
                    cap.center = new Vector3(0f, cap.height * 0.5f - lift, 0f);

                    report.AppendLine($"  {a.name}: clipe '{clip.name}' {clip.length:0.0}s, altura máx. {h:0.00} m, ajuste vertical {lift:+0.000;-0.000} m em ({p.x:0.0},{p.y:0.00},{p.z:0.0})");
                }
                AddDialogue(group, report);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            Debug.Log("[RoadAlleyFightSetup] OK\n" + report);
        }

        /// <summary>Vozes (ElevenLabs, Audio/AlleyFight/Thug_*.mp3 e Victim_*.mp3) em fontes 3D na cabeça
        /// de cada um + fonte da pancada no peito da vítima; componente AlleyFightDialogue na raiz.</summary>
        private static void AddDialogue(GameObject group, System.Text.StringBuilder report)
        {
            const string dir = "Assets/_Project/Audio/AlleyFight";
            AudioClip[] Load(string prefix) => AssetDatabase.FindAssets("t:AudioClip", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).StartsWith(prefix)).OrderBy(p => p)
                .Select(p =>
                {
                    if (AssetImporter.GetAtPath(p) is AudioImporter ai && ai.loadInBackground == false)
                    {
                        var s = ai.defaultSampleSettings; s.loadType = AudioClipLoadType.DecompressOnLoad; ai.defaultSampleSettings = s;
                        ai.forceToMono = true; ai.loadInBackground = true; ai.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                }).Where(c => c != null).ToArray();
            AudioClip[] thug = Load("Thug_"), victim = Load("Victim_");

            AudioSource Source(string parentName, string name, float y)
            {
                Transform parent = group.transform.Find(parentName);
                var go = new GameObject(name);
                go.transform.SetParent(parent != null ? parent : group.transform, false);
                go.transform.localPosition = new Vector3(0f, y, 0.1f);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = false;
                s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 5f; s.maxDistance = 30f; s.dopplerLevel = 0f; s.volume = 1f;
                return s;
            }
            var dlg = group.AddComponent<TheDelivery.FX.AlleyFightDialogue>();
            var so = new SerializedObject(dlg);
            so.FindProperty("thugVoice").objectReferenceValue = Source("FightThug", "ThugVoice", 1.65f);
            so.FindProperty("victimVoice").objectReferenceValue = Source("FightVictim", "VictimVoice", 1.05f);
            so.FindProperty("impact").objectReferenceValue = Source("FightVictim", "Impact", 0.8f);
            so.FindProperty("thudClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/AlleyFightThud.wav");
            var tl = so.FindProperty("thugLines"); tl.arraySize = thug.Length;
            for (int i = 0; i < thug.Length; i++) tl.GetArrayElementAtIndex(i).objectReferenceValue = thug[i];
            var vl = so.FindProperty("victimLines"); vl.arraySize = victim.Length;
            for (int i = 0; i < victim.Length; i++) vl.GetArrayElementAtIndex(i).objectReferenceValue = victim[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"  Diálogo: {thug.Length} falas do agressor, {victim.Length} da vítima.");
        }

        private static AnimationClip Prepare(string name, string modelPath, out AnimatorController controller)
        {
            controller = null;
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return null;
            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            // SEM loop no clipe: a repetição é um crossfade do fim para o começo (ver abaixo), e o estado que
            // sai fica parado no último quadro durante a mistura em vez de voltar ao início com tranco.
            foreach (var c in clips) if (c.loopTime) { c.loopTime = false; dirty = true; }
            if (dirty || importer.clipAnimations.Length == 0) { importer.clipAnimations = clips; importer.SaveAndReimport(); }

            RoadPedestrianSetup.AssignMaterial(name, modelPath, importer);

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) return null;

            string controllerPath = $"Assets/_Project/Models/{name}/{name}.controller";
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            foreach (var st in sm.states.ToArray()) sm.RemoveState(st.state);
            // LOOP SUAVE: dois estados com o mesmo clipe, A → B → A. Cada transição começa LoopFade s
            // antes do fim e mistura o fim do ciclo com o começo do próximo (o clipe de text-to-motion não
            // termina na pose em que começa; pular direto dava um tranco visível a cada 8 s).
            var a = sm.AddState("Act_A");
            var b = sm.AddState("Act_B");
            a.motion = clip; b.motion = clip;
            float fade = Mathf.Min(LoopFade, clip.length * 0.3f);
            foreach (var (from, to) in new[] { (a, b), (b, a) })
            {
                var tr = from.AddTransition(to);
                tr.hasExitTime = true;
                tr.exitTime = (clip.length - fade) / clip.length;
                tr.hasFixedDuration = true;
                tr.duration = fade;
                tr.offset = 0f;
            }
            sm.defaultState = a;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return clip;
        }

        /// <summary>Assa a malha em 48 instantes do clipe; devolve quanto subir/descer para a mediana dos
        /// pontos mais baixos ficar em y=0, e a maior altura atingida.</summary>
        private static float GroundOffset(string modelPath, AnimationClip clip, out float maxHeight)
        {
            maxHeight = 1.7f;
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var mins = new System.Collections.Generic.List<float>();
                float top = 0f;
                var mesh = new Mesh();
                for (int i = 0; i < 48; i++)
                {
                    clip.SampleAnimation(go, clip.length * i / 48f);
                    float lo = float.MaxValue;
                    foreach (var smr in smrs)
                    {
                        smr.BakeMesh(mesh, true);
                        // BakeMesh(useScale) já aplica a escala: só falta posição e rotação.
                        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        foreach (var v in mesh.vertices)
                        {
                            float y = m.MultiplyPoint3x4(v).y;
                            if (y < lo) lo = y;
                            if (y > top) top = y;
                        }
                    }
                    mins.Add(lo);
                }
                Object.DestroyImmediate(mesh);
                mins.Sort();
                maxHeight = top;
                return -mins[mins.Count / 2];
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

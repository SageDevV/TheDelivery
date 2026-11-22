using System.Collections.Generic;
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
    /// TROCA A CRIATURA DO PESADELO PELA NOVA (2026-11-22, "Stalker": modelo refeito do zero na Meshy
    /// porque a antiga deformava os braços ao erguê-los). Cobre a PERSEGUIÇÃO (<c>creatureObject</c>,
    /// corrida em loop) e o ATAQUE (<c>creatureAttackObject</c>, golpe de garra). O agarrão é trocado
    /// pelo <see cref="NightmareGrabExtendRebuild"/>, que também mede o clipe e posiciona a criatura.
    ///
    /// A troca preserva tudo o que a cena pendurou na criatura antiga: posição/rotação/pai/ordem, se
    /// está ligada, componentes na raiz (colisor, scripts), filhos acrescentados e TODAS as referências
    /// de qualquer componente da cena — para a raiz, para os componentes copiados e para os OSSOS (por
    /// nome, ignorando o prefixo "mixamorig:").
    /// Automação: Temp/NightmareCreatureSwap.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareCreatureSwap
    {
        private const string MarkerPath = "Temp/NightmareCreatureSwap.pending";
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const float CreatureScale = 2.4f; // a mesma do agarrão (~4,5 m)
        private static double nextPoll;

        private struct Slot
        {
            public string Field, ModelPath, ControllerPath, State;
            public bool Loop, RootMotion;
        }

        private static readonly Slot[] Slots =
        {
            new Slot { Field = "creatureObject", ModelPath = "Assets/_Project/Models/Stalker/Stalker_Walk.fbx",
                       ControllerPath = "Assets/_Project/Animation/Controllers/StalkerChase.controller", State = "Walk", Loop = true, RootMotion = true },
            new Slot { Field = "creatureAttackObject", ModelPath = "Assets/_Project/Models/Stalker/Stalker_Slash.fbx",
                       ControllerPath = "Assets/_Project/Animation/Controllers/StalkerAttack.controller", State = "Slash", Loop = false },
        };

        static NightmareCreatureSwap() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Nightmare - Trocar criatura pela nova (perseguição + ataque)")]
        public static void Run()
        {
            var report = new StringBuilder();
            AssetDatabase.Refresh();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NightmareDirector d = null;
                foreach (var r in scene.GetRootGameObjects())
                    if ((d = r.GetComponentInChildren<NightmareDirector>(true)) != null) break;
                if (d == null) { Debug.LogError("[NightmareCreatureSwap] NightmareDirector não encontrado."); return; }

                foreach (var slot in Slots)
                {
                    var so = new SerializedObject(d);
                    var old = so.FindProperty(slot.Field).objectReferenceValue as GameObject;
                    if (old == null) { report.AppendLine($"{slot.Field}: vazio, pulado."); continue; }
                    var fresh = Swap(scene, old, slot, report);
                    if (fresh == null) continue;
                    so = new SerializedObject(d);
                    so.FindProperty(slot.Field).objectReferenceValue = fresh;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var dso = new SerializedObject(d);
                dso.FindProperty("attackYaw").floatValue = 0f; // o modelo novo já aponta para +Z (o antigo precisava de 180)
                dso.FindProperty("attackDuration").floatValue = 3.0f;       // um golpe inteiro do clipe novo (3,2 s)
                dso.FindProperty("attackTriggerDistance").floatValue = 3.5f; // a 3,5 m ela já alcança com a garra
                if (lastStride > 0f) dso.FindProperty("creatureClipStrideSpeed").floatValue = lastStride;
                dso.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            File.WriteAllText("Temp/NightmareCreatureSwap.txt", report.ToString());
            Debug.Log("[NightmareCreatureSwap] OK\n" + report);
        }

        private static GameObject Swap(Scene scene, GameObject old, Slot slot, StringBuilder report)
        {
            // 1. Import + controller
            var importer = AssetImporter.GetAtPath(slot.ModelPath) as ModelImporter;
            if (importer == null) { Debug.LogError("[NightmareCreatureSwap] FBX não encontrado: " + slot.ModelPath); return null; }
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            // CAMINHADA: o avanço do Meshy está no osso Hips. Com ele como Root Motion Node o Unity extrai a
            // translação XZ para o root (averageSpeed vira a passada real → o director casa a cadência com a
            // creatureSpeed e o pé não patina) e, com applyRootMotion desligado, o corpo anda no lugar.
            if (slot.RootMotion)
            {
                var probe = AssetDatabase.LoadAssetAtPath<GameObject>(slot.ModelPath);
                var hips = probe.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Norm(t.name) == "hips");
                if (hips != null) importer.motionNodeName = AnimationUtility.CalculateTransformPath(hips, probe.transform);
            }
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = slot.Loop; c.name = slot.State;
                if (slot.RootMotion) { c.lockRootRotation = true; c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.lockRootPositionXZ = false; c.loopPose = true; }
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(slot.ModelPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(slot.ModelPath);
            AssetDatabase.DeleteAsset(slot.ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(slot.ControllerPath);
            ctrl.layers[0].stateMachine.AddState(slot.State).motion = clip;
            AssetDatabase.SaveAssets();

            // 2. Instância no lugar da antiga
            var fresh = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            fresh.name = old.name;
            fresh.transform.SetParent(old.transform.parent, false);
            fresh.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            fresh.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            fresh.transform.localScale = Vector3.one * CreatureScale;
            foreach (var t in fresh.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = old.layer; // a câmera do ataque só enxerga esta camada
            var oldAn = old.GetComponentInChildren<Animator>(true);
            if (!fresh.TryGetComponent(out Animator an)) an = fresh.AddComponent<Animator>();
            an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = oldAn != null && oldAn.applyRootMotion;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in fresh.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            // 3. Componentes da raiz (colisor, scripts) → cópia na nova
            var compMap = new Dictionary<Object, Object> { [old] = fresh, [old.transform] = fresh.transform };
            if (oldAn != null) compMap[oldAn] = an;
            foreach (var c in old.GetComponents<Component>())
            {
                if (c == null || c is Transform || c is Animator) continue;
                var copy = fresh.AddComponent(c.GetType());
                if (copy == null) continue;
                EditorUtility.CopySerialized(c, copy);
                compMap[c] = copy;
                report.AppendLine($"  {slot.Field}: componente copiado {c.GetType().Name}");
            }
            // 4. Filhos acrescentados na cena (não fazem parte do modelo) → passam para a nova
            foreach (Transform ch in old.transform.Cast<Transform>().ToList())
                if (PrefabUtility.IsAddedGameObjectOverride(ch.gameObject) || !PrefabUtility.IsPartOfPrefabInstance(ch.gameObject))
                {
                    ch.SetParent(fresh.transform, true);
                    report.AppendLine($"  {slot.Field}: filho levado '{ch.name}'");
                }
            // 5. Ossos por nome
            var newBones = new Dictionary<string, Transform>();
            foreach (var t in fresh.GetComponentsInChildren<Transform>(true)) newBones[Norm(t.name)] = t;
            foreach (var t in old.GetComponentsInChildren<Transform>(true))
                if (!compMap.ContainsKey(t) && newBones.TryGetValue(Norm(t.name), out var nt)) compMap[t] = nt;

            // 6. Refaz todas as referências da cena
            int remapped = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp is Transform) continue;
                if (comp.gameObject == old || comp.transform.IsChildOf(old.transform)) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (compMap.TryGetValue(it.objectReferenceValue, out var to)) { it.objectReferenceValue = to; changed = true; remapped++; }
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            // Colisor copiado da criatura antiga vem no espaço DELA (outra escala): refaz pelos renderers.
            if (fresh.TryGetComponent(out CapsuleCollider cap))
            {
                Bounds b = new Bounds(fresh.transform.position, Vector3.zero);
                foreach (var r in fresh.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
                float s = fresh.transform.lossyScale.y;
                cap.direction = 1;
                cap.center = fresh.transform.InverseTransformPoint(b.center);
                cap.height = b.size.y / s;
                cap.radius = Mathf.Min(b.size.x, b.size.z) * 0.35f / s;
                report.AppendLine($"  {slot.Field}: cápsula refeita (altura {b.size.y:0.00} m, raio {cap.radius * s:0.00} m)");
            }
            if (slot.Field == "creatureAttackObject") FrameAttackCamera(fresh, report);
            if (slot.RootMotion) lastStride = MeasureStride(fresh, clip, report);
            fresh.SetActive(old.activeSelf);
            report.AppendLine($"{slot.Field}: '{old.name}' → {Path.GetFileName(slot.ModelPath)} (clipe {clip.length:0.00}s, loop {slot.Loop}); {remapped} referências refeitas.");
            Object.DestroyImmediate(old);
            return fresh;
        }

        private static float lastStride;

        /// <summary>Recoloca a AttackCamera (filha) para enquadrar a criatura nova inteira: de frente, 25° de lado, 6° de cima.</summary>
        private static void FrameAttackCamera(GameObject fresh, StringBuilder report)
        {
            var cam = fresh.GetComponentsInChildren<Camera>(true).FirstOrDefault();
            if (cam == null) return;
            Bounds b = new Bounds(fresh.transform.position, Vector3.zero);
            foreach (var r in fresh.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
            float half = Mathf.Max(b.size.y, b.size.x * 9f / 16f) * 1.15f * 0.5f;
            float dist = half / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 fwd = fresh.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 dir = Quaternion.AngleAxis(25f, Vector3.up) * fwd;
            dir = Quaternion.AngleAxis(-6f, Vector3.Cross(Vector3.up, dir)) * dir;
            cam.transform.position = b.center + dir.normalized * dist;
            cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position, Vector3.up);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = Mathf.Max(cam.farClipPlane, dist * 3f);
            report.AppendLine($"  câmera do ataque reenquadrada a {dist:0.0} m (criatura {b.size.y:0.00} m)");
        }

        /// <summary>Passada do clipe (no lugar) pelos pés: o pé de apoio recua D em meio ciclo → velocidade = 2D/T, já na escala do mundo.</summary>
        private static float MeasureStride(GameObject fresh, AnimationClip clip, StringBuilder report)
        {
            var foot = fresh.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Norm(t.name) == "leftfoot");
            if (foot == null) return 0f;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i <= 60; i++)
            {
                clip.SampleAnimation(fresh, clip.length * i / 60f);
                float z = fresh.transform.InverseTransformPoint(foot.position).z * fresh.transform.lossyScale.z;
                min = Mathf.Min(min, z); max = Mathf.Max(max, z);
            }
            clip.SampleAnimation(fresh, 0f);
            float speed = 2f * (max - min) / clip.length;
            report.AppendLine($"  passada medida pelos pés: {max - min:0.00} m por meio ciclo → {speed:0.00} m/s");
            return speed;
        }

        private static string Norm(string n)
        {
            int i = n.LastIndexOf(':');
            if (i >= 0) n = n.Substring(i + 1);
            return n.Replace("_", "").ToLowerInvariant();
        }
    }
}

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MODELO DA PROTAGONISTA (Clear): 20 anos, ~1,65 m, cabelo preto curto, moletom cinza, jeans e
    /// tênis. Meshy: imagem de conceito (nano-banana-pro) → imagem→3D t2.5 + textura PBR → rig 1,65 m
    /// → animações Idle (0) e Walking Woman (1) num FBX só (Models/Clear/Clear.fbx).
    /// <para>Import Generic (como os NPCs da Meshy), clipes em loop, material URP/Lit com as texturas
    /// extraídas (<see cref="RoadPedestrianSetup.AssignMaterial"/>), controller com Idle ⇄ Walk pelo
    /// bool "Walking" (crossfade 0,25 s) e prefab em Prefabs/Clear.prefab.</para>
    /// O jogo é em primeira pessoa: o modelo serve para cutscenes, trailer e reflexos.
    /// Automação: Temp/ClearModelSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class ClearModelSetup
    {
        private const string ModelPath = "Assets/_Project/Models/Clear/Clear.fbx";
        private const string ControllerPath = "Assets/_Project/Animation/Controllers/Clear.controller";
        private const string PrefabPath = "Assets/_Project/Prefabs/Clear.prefab";
        private const string MarkerPath = "Temp/ClearModelSetup.pending";
        private static double nextPoll;

        static ClearModelSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Clear - Modelo da protagonista")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) { Debug.LogError("[ClearModelSetup] FBX não encontrado: " + ModelPath); return; }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips) c.loopTime = true;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            RoadPedestrianSetup.AssignMaterial("Clear", ModelPath, importer);

            var all = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip idle = all.FirstOrDefault(c => c.name.IndexOf("Idle", System.StringComparison.OrdinalIgnoreCase) >= 0) ?? all.FirstOrDefault();
            AnimationClip walk = all.FirstOrDefault(c => c.name.IndexOf("Walk", System.StringComparison.OrdinalIgnoreCase) >= 0) ?? all.LastOrDefault();
            if (idle == null) { Debug.LogError("[ClearModelSetup] Nenhum clipe no FBX."); return; }

            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (!controller.parameters.Any(p => p.name == "Walking"))
                controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
            var sm = controller.layers[0].stateMachine;
            foreach (var st in sm.states.ToArray()) sm.RemoveState(st.state);
            var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
            var sWalk = sm.AddState("Walk"); sWalk.motion = walk;
            sm.defaultState = sIdle;
            var toWalk = sIdle.AddTransition(sWalk); toWalk.hasExitTime = false; toWalk.duration = 0.25f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "Walking");
            var toIdle = sWalk.AddTransition(sIdle); toIdle.hasExitTime = false; toIdle.duration = 0.25f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Walking");
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = "Clear";
                if (!go.TryGetComponent(out Animator an)) an = go.AddComponent<Animator>();
                an.runtimeAnimatorController = controller;
                an.applyRootMotion = false;
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.25f; cap.height = 1.65f; cap.center = new Vector3(0f, 0.825f, 0f);
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            var b = model.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            Debug.Log($"[ClearModelSetup] OK | clipes: {string.Join(", ", all.Select(c => $"{c.name} {c.length:0.0}s"))} | " +
                      $"idle='{idle.name}' walk='{walk?.name}' | altura {b.size.y:0.00} m | prefab {PrefabPath}");
        }
    }
}

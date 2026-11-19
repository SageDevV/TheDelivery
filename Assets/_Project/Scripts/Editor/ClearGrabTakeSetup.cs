using System.IO;
using System.Linq;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// TAKE EM 3ª PESSOA DO AGARRÃO: importa Models/Clear/Clear_Held.fbx (Clear pendurada pelo
    /// pescoço se debatendo — text-to-motion da Meshy no rig dela), monta Prefabs/ClearHeld.prefab
    /// (material da Clear, clipe em loop) e liga em grabTakeModel do NightmareDirector da cena
    /// Nightmare. Ver NightmareDirector.GrabTake.cs. Automação: Temp/ClearGrabTakeSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class ClearGrabTakeSetup
    {
        private const string ModelPath = "Assets/_Project/Models/Clear/Clear_Held.fbx";
        private const string ControllerPath = "Assets/_Project/Animation/Controllers/ClearHeld.controller";
        private const string PrefabPath = "Assets/_Project/Prefabs/ClearHeld.prefab";
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string MarkerPath = "Temp/ClearGrabTakeSetup.pending";
        private static double nextPoll;

        static ClearGrabTakeSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Nightmare - Take 3ª pessoa do agarrão")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) { Debug.LogError("[ClearGrabTakeSetup] FBX não encontrado: " + ModelPath); return; }
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips) c.loopTime = true;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            RoadPedestrianSetup.AssignMaterial("Clear", ModelPath, importer);

            var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) { Debug.LogError("[ClearGrabTakeSetup] Sem clipe no FBX."); return; }
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.layers[0].stateMachine.AddState("Held").motion = clip;
            AssetDatabase.SaveAssets();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = "ClearHeld";
                if (!go.TryGetComponent(out Animator an)) an = go.AddComponent<Animator>();
                an.runtimeAnimatorController = ctrl;
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally { Object.DestroyImmediate(go); }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NightmareDirector director = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((director = root.GetComponentInChildren<NightmareDirector>(true)) != null) break;
                if (director == null) { Debug.LogError("[ClearGrabTakeSetup] NightmareDirector não encontrado."); return; }
                var so = new SerializedObject(director);
                so.FindProperty("grabTakeModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            Debug.Log($"[ClearGrabTakeSetup] OK | clipe '{clip.name}' {clip.length:0.0}s | {PrefabPath} ligado no NightmareDirector");
        }
    }
}

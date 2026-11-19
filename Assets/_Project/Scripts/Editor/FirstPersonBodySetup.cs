using System.IO;
using System.Linq;
using TheDelivery.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MONTA O CORPO DA CLEAR NO PLAYER (primeira pessoa, corpo visível + sombra). Reimporta o
    /// Clear.fbx (Idle, Walking_Woman, Run_02, Cautious_Crouch_Walk_Forward), cria o controller
    /// ClearFirstPerson (Speed/Crouch: em pé = blend parada→caminhada→corrida; agachada = caminhada
    /// agachada, congelada quando parada) e põe no Player.prefab um nó "FirstPersonBody" com dois
    /// modelos — "ClearBody" (visto pela câmera, sem cabeça, sem sombra) e "ClearShadow" (só sombra).
    /// Ver <see cref="FirstPersonBody"/>. Automação: Temp/FirstPersonBodySetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class FirstPersonBodySetup
    {
        private const string ModelPath = "Assets/_Project/Models/Clear/Clear.fbx";
        private const string ControllerPath = "Assets/_Project/Animation/Controllers/ClearFirstPerson.controller";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player.prefab";
        private const string MarkerPath = "Temp/FirstPersonBodySetup.pending";
        private static double nextPoll;

        static FirstPersonBodySetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Clear - Corpo em primeira pessoa")]
        public static void Run()
        {
            ClearModelSetup.Run(); // reimport + material + prefab de apresentação

            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Find(string s) => clips.FirstOrDefault(c => c.name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0);
            AnimationClip idle = Find("Idle"), walk = Find("Walking"), run = Find("Run"), crouch = Find("Crouch");
            if (idle == null || walk == null || run == null || crouch == null)
            {
                Debug.LogError("[FirstPersonBodySetup] Faltam clipes no Clear.fbx: " + string.Join(", ", clips.Select(c => c.name)));
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;

            var stand = ctrl.CreateBlendTreeInController("Stand", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.5f);
            tree.AddChild(run, 3.1f);
            var crouchIdle = sm.AddState("CrouchIdle"); crouchIdle.motion = crouch; crouchIdle.speed = 0f;
            var crouchMove = sm.AddState("CrouchMove"); crouchMove.motion = crouch;
            sm.defaultState = stand;

            void T(AnimatorState a, AnimatorState b, params (AnimatorConditionMode m, float v, string p)[] conds)
            {
                var t = a.AddTransition(b);
                t.hasExitTime = false; t.duration = 0.2f;
                foreach (var c in conds) t.AddCondition(c.m, c.v, c.p);
            }
            T(stand, crouchIdle, (AnimatorConditionMode.If, 0f, "Crouch"), (AnimatorConditionMode.Less, 0.15f, "Speed"));
            T(stand, crouchMove, (AnimatorConditionMode.If, 0f, "Crouch"), (AnimatorConditionMode.Greater, 0.15f, "Speed"));
            T(crouchIdle, crouchMove, (AnimatorConditionMode.Greater, 0.15f, "Speed"));
            T(crouchMove, crouchIdle, (AnimatorConditionMode.Less, 0.15f, "Speed"));
            T(crouchIdle, stand, (AnimatorConditionMode.IfNot, 0f, "Crouch"));
            T(crouchMove, stand, (AnimatorConditionMode.IfNot, 0f, "Crouch"));
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var old = root.transform.Find("FirstPersonBody");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var holder = new GameObject("FirstPersonBody");
                holder.transform.SetParent(root.transform, false);

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Animator Make(string name)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
                    go.name = name;
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    if (!go.TryGetComponent(out Animator an)) an = go.AddComponent<Animator>();
                    an.runtimeAnimatorController = ctrl;
                    an.applyRootMotion = false;
                    an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    return an;
                }
                var body = Make("ClearBody");
                var shadow = Make("ClearShadow");

                var fpb = holder.AddComponent<FirstPersonBody>();
                var so = new SerializedObject(fpb);
                so.FindProperty("player").objectReferenceValue = root.GetComponent<PlayerController>();
                so.FindProperty("controller").objectReferenceValue = root.GetComponent<CharacterController>();
                so.FindProperty("bodyAnimator").objectReferenceValue = body;
                so.FindProperty("shadowAnimator").objectReferenceValue = shadow;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[FirstPersonBodySetup] OK | clipes idle='{idle.name}' walk='{walk.name}' run='{run.name}' crouch='{crouch.name}' | Player.prefab com FirstPersonBody");
        }
    }
}

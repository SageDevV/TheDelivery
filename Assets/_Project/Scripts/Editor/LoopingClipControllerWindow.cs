using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Constrói o setup MÍNIMO para um modelo com UMA animação embutida ficar tocando ela em
    /// LOOP para sempre — o caso dos NPCs de ambientação (ex.: o Shadow, que só precisa andar).
    /// Faz as três coisas que, no Inspector, são fáceis de esquecer e deixam o personagem em
    /// T-pose ou congelado no último frame:
    /// <list type="number">
    /// <item>marca o clipe do FBX como <b>Loop Time</b> (por padrão a importação vem SEM loop);</item>
    /// <item>garante um <b>Avatar</b> no FBX (rig Generic sem Avatar não anima);</item>
    /// <item>gera um AnimatorController de <b>estado único</b> com esse clipe e atribui ao alvo.</item>
    /// </list>
    ///
    /// É o irmão simples do <see cref="LocomotionControllerBuilderWindow"/>: aquele monta a FSM
    /// Idle/Walking/Running dirigida por parâmetros (para quem tem AI); este aqui não tem
    /// parâmetro nenhum — o personagem entra tocando e nunca sai do estado.
    ///
    /// Genérico: nada de nomes de asset hardcoded, tudo vem da janela. Idempotente: pode rodar
    /// de novo à vontade (o controller é recriado do zero).
    /// </summary>
    public sealed class LoopingClipControllerWindow : EditorWindow
    {
        private GameObject modelFbx;
        private string clipName = "";
        private string stateName = "Loop";
        private string outputPath = "Assets/_Project/Animation/Controllers/ShadowWalk.controller";
        private GameObject assignTarget;

        private bool forceLoopTime = true;
        private bool ensureAvatar = true;

        [MenuItem("Tools/The Delivery/Build Looping Clip Controller")]
        private static void Open()
        {
            var window = GetWindow<LoopingClipControllerWindow>("Looping Clip");
            window.minSize = new Vector2(400, 330);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Controller de clipe único em loop (NPC de ambientação)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Para um modelo que só precisa tocar a animação dele para sempre. Marca o clipe " +
                "como Loop Time, garante o Avatar do rig e gera um controller de um estado só.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            modelFbx = (GameObject)EditorGUILayout.ObjectField("FBX do modelo", modelFbx, typeof(GameObject), false);
            clipName = EditorGUILayout.TextField(
                new GUIContent("Nome do clipe", "Deixe VAZIO para usar o primeiro clipe do FBX."), clipName);
            stateName = EditorGUILayout.TextField("Nome do estado", stateName);

            EditorGUILayout.Space();
            forceLoopTime = EditorGUILayout.Toggle(
                new GUIContent("Marcar como Loop Time", "Reimporta o FBX com loopTime ligado em todos os clipes."),
                forceLoopTime);
            ensureAvatar = EditorGUILayout.Toggle(
                new GUIContent("Garantir Avatar", "Se o FBX não tem Avatar, reimporta como Generic + Create From This Model."),
                ensureAvatar);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Saída", EditorStyles.boldLabel);
            outputPath = EditorGUILayout.TextField("Caminho do .controller", outputPath);
            assignTarget = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("GameObject alvo", "Opcional: recebe/ganha um Animator já configurado."),
                assignTarget, typeof(GameObject), true);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(modelFbx == null || string.IsNullOrWhiteSpace(outputPath)))
                if (GUILayout.Button("Gerar controller (+ atribuir)", GUILayout.Height(28)))
                    Build();
        }

        private void Build()
        {
            string fbxPath = AssetDatabase.GetAssetPath(modelFbx);
            if (string.IsNullOrEmpty(fbxPath))
            {
                EditorUtility.DisplayDialog("Looping Clip", "O objeto informado não é um asset do projeto.", "OK");
                return;
            }

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                EditorUtility.DisplayDialog("Looping Clip", $"\"{fbxPath}\" não é um modelo importável (FBX/OBJ).", "OK");
                return;
            }

            if (PrepareImport(importer, fbxPath))
                importer.SaveAndReimport();

            AnimationClip clip = FindClip(fbxPath, clipName);
            if (clip == null)
            {
                EditorUtility.DisplayDialog("Looping Clip",
                    string.IsNullOrWhiteSpace(clipName)
                        ? $"Nenhum AnimationClip encontrado em:\n{fbxPath}\n\nConfira se 'Import Animation' está ligado no FBX."
                        : $"Não achei o clipe \"{clipName}\" em:\n{fbxPath}", "OK");
                return;
            }

            string dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                EditorUtility.DisplayDialog("Looping Clip",
                    $"A pasta de destino não existe:\n{dir}\n\nCrie-a antes de gerar.", "OK");
                return;
            }

            // Recria do zero para o botão ser idempotente.
            AssetDatabase.DeleteAsset(outputPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(outputPath);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState state = sm.AddState(string.IsNullOrWhiteSpace(stateName) ? "Loop" : stateName);
            state.motion = clip;
            sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            if (assignTarget != null)
                AssignToTarget(controller, fbxPath);

            EditorGUIUtility.PingObject(controller);
            Debug.Log($"[LoopingClipController] Controller gerado em \"{outputPath}\" " +
                      $"tocando \"{clip.name}\" em loop.", controller);
        }

        /// <summary>
        /// Ajusta as import settings do FBX (loop dos clipes e existência de Avatar).
        /// Retorna true se algo mudou e o asset precisa ser reimportado.
        /// </summary>
        private bool PrepareImport(ModelImporter importer, string fbxPath)
        {
            bool dirty = false;

            if (forceLoopTime)
            {
                // clipAnimations vazio = o FBX está usando os takes automáticos; para poder
                // MEXER no loop é preciso materializá-los como clipes explícitos.
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0)
                    clips = importer.defaultClipAnimations;

                foreach (ModelImporterClipAnimation clip in clips)
                {
                    if (clip.loopTime)
                        continue;

                    clip.loopTime = true;
                    dirty = true;
                }

                if (dirty)
                    importer.clipAnimations = clips;
            }

            // Rig Generic SEM Avatar não anima — o Animator não sabe mapear a hierarquia.
            if (ensureAvatar && !HasAvatar(fbxPath))
            {
                if (importer.animationType == ModelImporterAnimationType.None)
                    importer.animationType = ModelImporterAnimationType.Generic;

                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                dirty = true;
            }

            return dirty;
        }

        private void AssignToTarget(AnimatorController controller, string fbxPath)
        {
            Animator animator = assignTarget.GetComponent<Animator>();
            if (animator == null)
            {
                Undo.RegisterCompleteObjectUndo(assignTarget, "Add Animator");
                animator = Undo.AddComponent<Animator>(assignTarget);
            }
            else
            {
                Undo.RecordObject(animator, "Assign Animator Controller");
            }

            animator.runtimeAnimatorController = controller;

            Avatar avatar = FindAvatar(fbxPath);
            if (avatar != null)
                animator.avatar = avatar;

            // Quem desloca o NPC é o script de movimento (ex.: AmbientWalker), não a animação.
            animator.applyRootMotion = false;

            EditorUtility.SetDirty(animator);
            Debug.Log($"[LoopingClipController] Controller atribuído ao Animator de \"{assignTarget.name}\"" +
                      (avatar != null ? $" com Avatar \"{avatar.name}\"." : " (sem Avatar encontrado no FBX)."),
                      assignTarget);
        }

        /// <summary>
        /// Acha um AnimationClip dentro do FBX. Com <paramref name="wanted"/> vazio devolve o
        /// primeiro. Ignora os "__preview__" que o Editor cria para a janela de preview.
        /// </summary>
        private static AnimationClip FindClip(string fbxPath, string wanted)
        {
            var candidates = new List<AnimationClip>();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    candidates.Add(clip);

            if (candidates.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(wanted))
                return candidates[0];

            return candidates.Find(c => c.name == wanted);
        }

        private static Avatar FindAvatar(string fbxPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is Avatar avatar)
                    return avatar;

            return null;
        }

        private static bool HasAvatar(string fbxPath) => FindAvatar(fbxPath) != null;
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
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
    /// Genérico: nada de nomes de asset hardcoded, tudo vem da janela — o caminho de saída é
    /// SUGERIDO a partir do nome do FBX justamente para cada NPC ganhar o arquivo dele.
    /// Idempotente: pode rodar de novo à vontade (o controller existente é reescrito NO LUGAR,
    /// preservando o GUID e portanto as referências que já existem em cenas e prefabs).
    /// </summary>
    public sealed class LoopingClipControllerWindow : EditorWindow
    {
        /// <summary>Pasta padrão da sugestão de caminho. O nome do arquivo vem do FBX.</summary>
        private const string DefaultFolder = "Assets/_Project/Animation/Controllers";

        private GameObject modelFbx;
        private string clipName = "";
        private string stateName = "Loop";
        private string outputPath = "";
        private GameObject assignTarget;

        private bool forceLoopTime = true;
        private bool ensureAvatar = true;

        /// <summary>Último FBX visto no campo, para detectar a troca e re-sugerir o caminho.</summary>
        private GameObject lastModelFbx;
        /// <summary>Última sugestão que a janela escreveu sozinha em <see cref="outputPath"/>.
        /// Serve para saber se o campo ainda está "virgem": se o usuário digitou um caminho à
        /// mão, ele não é sobrescrito quando o FBX muda.</summary>
        private string suggestedPath = "";

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
            if (modelFbx != lastModelFbx)
            {
                lastModelFbx = modelFbx;
                SuggestOutputPath();
            }

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

            // UM ARQUIVO POR NPC. Dois personagens apontando para o MESMO .controller é o erro
            // clássico aqui: o segundo Build reescreve o clipe do primeiro e ele para de andar.
            if (!string.IsNullOrWhiteSpace(outputPath) &&
                AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath) != null)
                EditorGUILayout.HelpBox(
                    "Já existe um controller neste caminho — ele será REESCRITO com o clipe deste FBX. " +
                    "Se OUTRO NPC usa este mesmo arquivo, ele vai passar a tocar a animação errada. " +
                    "Dê um .controller próprio para cada personagem.",
                    MessageType.Warning);

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

            // Reaproveita o asset que já está lá em vez de apagar e criar outro. Apagar geraria
            // um GUID NOVO e TODA referência existente (o Animator na cena, num prefab, numa
            // cena de backup) viraria "Missing" — sem controller o personagem fica em T-pose ou
            // deslizando parado. É esse o sintoma de "gerei para um NPC e o outro parou de andar".
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(outputPath);
            else
                ClearBaseLayer(controller);

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
        /// Sugere o caminho de saída a partir do nome do FBX, para cada personagem ganhar o
        /// .controller DELE. Só escreve no campo enquanto ele estiver como a janela deixou —
        /// caminho digitado à mão é respeitado.
        /// </summary>
        private void SuggestOutputPath()
        {
            if (modelFbx == null)
                return;

            if (!string.IsNullOrWhiteSpace(outputPath) && outputPath != suggestedPath)
                return;

            suggestedPath = $"{DefaultFolder}/{modelFbx.name}Walk.controller";
            outputPath = suggestedPath;
        }

        /// <summary>
        /// Esvazia a Base Layer do controller existente para ele ser remontado, mantendo o ASSET
        /// (e o GUID) de pé — é o que torna o botão idempotente sem quebrar quem já aponta para cá.
        /// </summary>
        private static void ClearBaseLayer(AnimatorController controller)
        {
            if (controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                return;
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // 'states'/'stateMachines' devolvem CÓPIAS do array, então remover durante o foreach
            // não invalida a iteração.
            foreach (ChildAnimatorState child in sm.states)
                sm.RemoveState(child.state);

            foreach (ChildAnimatorStateMachine child in sm.stateMachines)
                sm.RemoveStateMachine(child.stateMachine);
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

            // O Animator de um modelo arrastado para a cena PERTENCE ao prefab do FBX: mexer
            // nos campos dele cria um OVERRIDE de instância, e override só é gravado no .unity
            // com esta chamada. Sem ela a atribuição aparece no Inspector mas some ao salvar/
            // recarregar a cena — e o personagem volta a deslizar sem animação.
            if (PrefabUtility.IsPartOfPrefabInstance(animator))
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

            // Marca a cena como suja para o Ctrl+S realmente gravar (alvo que é asset de prefab
            // não tem cena válida — nesse caso o SetDirty acima já basta).
            if (assignTarget.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(assignTarget.scene);
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

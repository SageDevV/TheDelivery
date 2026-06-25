using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Constrói um AnimatorController de locomoção Idle/Walking/Running a partir de 3 clipes
    /// arrastados pelo usuário. Genérico: TODOS os dados (clipes, nomes de parâmetro, limiares,
    /// caminho de saída) vêm da janela, nada é hardcoded — serve a qualquer personagem, não só
    /// ao Antagonista. A máquina de estados resultante é dirigida por dois parâmetros:
    /// <list type="bullet">
    /// <item><b>Speed</b> (float): velocidade horizontal. Abaixo do limiar = Idle; acima = Walking.</item>
    /// <item><b>IsChasing</b> (bool): true força Running (estado de perseguição, ex.: Ato 4).</item>
    /// </list>
    /// Esses são exatamente os parâmetros que o <c>AntagonistAI</c> alimenta. Opcionalmente
    /// atribui o controller (e um Avatar) ao Animator de um GameObject de cena/prefab.
    ///
    /// O wiring é por API (AnimatorController/AssetDatabase), então os clipes são referenciados
    /// pelos próprios assets — sem depender de fileIDs internos do FBX escritos à mão.
    /// </summary>
    public sealed class LocomotionControllerBuilderWindow : EditorWindow
    {
        private AnimationClip idleClip;
        private AnimationClip walkClip;
        private AnimationClip runClip;

        private string speedParam = "Speed";
        private string chasingParam = "IsChasing";
        private float idleSpeedThreshold = 0.1f;
        private float transitionDuration = 0.15f;

        private string outputPath = "Assets/Animation/Antagonist.controller";

        private GameObject assignTarget;   // recebe/ganha um Animator
        private GameObject modelFbx;       // FBX do modelo riggado — de onde sai o Avatar humanoide

        [MenuItem("Tools/The Delivery/Build Locomotion Controller")]
        private static void Open()
        {
            var window = GetWindow<LocomotionControllerBuilderWindow>("Locomotion Controller");
            window.minSize = new Vector2(380, 420);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animator de locomoção (Idle / Walking / Running)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Arraste os 3 clipes (do FBX, sub-asset AnimationClip) e gere o controller. " +
                "Dirigido por Speed (float) + IsChasing (bool) — os mesmos que o AntagonistAI alimenta.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            idleClip = (AnimationClip)EditorGUILayout.ObjectField("Idle (parado)", idleClip, typeof(AnimationClip), false);
            walkClip = (AnimationClip)EditorGUILayout.ObjectField("Walking (andando)", walkClip, typeof(AnimationClip), false);
            runClip = (AnimationClip)EditorGUILayout.ObjectField("Running (perseguindo)", runClip, typeof(AnimationClip), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parâmetros", EditorStyles.boldLabel);
            speedParam = EditorGUILayout.TextField("Nome do Speed (float)", speedParam);
            chasingParam = EditorGUILayout.TextField("Nome do IsChasing (bool)", chasingParam);
            idleSpeedThreshold = EditorGUILayout.FloatField("Limiar Idle↔Walk (Speed)", idleSpeedThreshold);
            transitionDuration = EditorGUILayout.FloatField("Duração da transição (s)", transitionDuration);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Saída", EditorStyles.boldLabel);
            outputPath = EditorGUILayout.TextField("Caminho do .controller", outputPath);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(idleClip == null || walkClip == null || runClip == null
                                               || string.IsNullOrWhiteSpace(outputPath)))
                if (GUILayout.Button("Gerar/atualizar Controller", GUILayout.Height(28)))
                    Build();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Atribuir a um Animator (opcional)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "O alvo precisa do modelo humanoide (mesh skinada) como visual para as animações " +
                "Humanoid tocarem. O Avatar é extraído automaticamente do FBX informado abaixo.",
                EditorStyles.wordWrappedLabel);
            assignTarget = (GameObject)EditorGUILayout.ObjectField("GameObject alvo", assignTarget, typeof(GameObject), true);
            modelFbx = (GameObject)EditorGUILayout.ObjectField("FBX do modelo (p/ Avatar)", modelFbx, typeof(GameObject), false);

            using (new EditorGUI.DisabledScope(assignTarget == null))
                if (GUILayout.Button("Atribuir controller (+Avatar) ao alvo"))
                    AssignToTarget();
        }

        private void Build()
        {
            string dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                EditorUtility.DisplayDialog("Locomotion Controller",
                    $"A pasta de destino não existe:\n{dir}\n\nCrie-a antes de gerar.", "OK");
                return;
            }

            // Recria do zero para o botão ser idempotente (apagar evita estados/params duplicados).
            AssetDatabase.DeleteAsset(outputPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(outputPath);

            controller.AddParameter(speedParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(chasingParam, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimatorState idle = sm.AddState("Idle");
            idle.motion = idleClip;
            AnimatorState walk = sm.AddState("Walking");
            walk.motion = walkClip;
            AnimatorState run = sm.AddState("Running");
            run.motion = runClip;
            sm.defaultState = idle;

            // A ordem em que as transições são adicionadas é a PRIORIDADE de avaliação.
            // Em cada estado, a checagem de perseguição (Running) vem primeiro.

            // Idle -> Running (perseguindo, mesmo partindo de parado) / Idle -> Walking (andou)
            AddTransition(idle, run, c => c.AddCondition(AnimatorConditionMode.If, 0f, chasingParam));
            AddTransition(idle, walk, c => c.AddCondition(AnimatorConditionMode.Greater, idleSpeedThreshold, speedParam));

            // Walking -> Running (passou a perseguir) / Walking -> Idle (parou)
            AddTransition(walk, run, c => c.AddCondition(AnimatorConditionMode.If, 0f, chasingParam));
            AddTransition(walk, idle, c => c.AddCondition(AnimatorConditionMode.Less, idleSpeedThreshold, speedParam));

            // Running -> Walking (parou de perseguir mas segue andando) / Running -> Idle (parou de tudo)
            AddTransition(run, walk, c =>
            {
                c.AddCondition(AnimatorConditionMode.IfNot, 0f, chasingParam);
                c.AddCondition(AnimatorConditionMode.Greater, idleSpeedThreshold, speedParam);
            });
            AddTransition(run, idle, c =>
            {
                c.AddCondition(AnimatorConditionMode.IfNot, 0f, chasingParam);
                c.AddCondition(AnimatorConditionMode.Less, idleSpeedThreshold, speedParam);
            });

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(controller);
            Debug.Log($"[LocomotionControllerBuilder] Controller gerado em \"{outputPath}\" " +
                      $"(Idle/Walking/Running, params {speedParam}/{chasingParam}).", controller);
        }

        /// <summary>Cria uma transição instantânea (sem exit time, duração fixa) e aplica as condições.</summary>
        private void AddTransition(AnimatorState from, AnimatorState to, System.Action<AnimatorStateTransition> addConditions)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = Mathf.Max(0f, transitionDuration);
            t.canTransitionToSelf = false;
            addConditions(t);
        }

        private void AssignToTarget()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Locomotion Controller",
                    $"Não encontrei o controller em:\n{outputPath}\n\nGere-o primeiro.", "OK");
                return;
            }

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

            Avatar avatar = ExtractAvatar(modelFbx);
            if (avatar != null)
                animator.avatar = avatar;

            EditorUtility.SetDirty(animator);
            Debug.Log($"[LocomotionControllerBuilder] Controller atribuído ao Animator de \"{assignTarget.name}\"" +
                      (avatar != null ? $" com Avatar \"{avatar.name}\"." : " (sem Avatar — informe o FBX do modelo humanoide)."),
                      assignTarget);
        }

        /// <summary>Acha o sub-asset Avatar dentro do FBX informado (o avatar Humanoid gerado na importação).</summary>
        private static Avatar ExtractAvatar(GameObject fbx)
        {
            if (fbx == null)
                return null;

            string path = AssetDatabase.GetAssetPath(fbx);
            if (string.IsNullOrEmpty(path))
                return null;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Avatar avatar)
                    return avatar;

            return null;
        }
    }
}

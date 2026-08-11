using System.Collections.Generic;
using TheDelivery.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Troca a Marina de greybox (cápsula) pelo modelo riggado com as DUAS animações do
    /// Ato 1 — Walking e Sitting — na cena da Cafeteria. Faz de uma vez as quatro etapas
    /// que, no Inspector, são fáceis de errar e deixam a personagem em T-pose, deslizando
    /// ou meio metro dentro do chão:
    /// <list type="number">
    /// <item><b>Imports:</b> <c>Marina_Walking.fbx</c> vira o modelo (rig Humanoid + Avatar
    /// próprio) e <c>Marina_Sitting.fbx</c> entra como fonte só do clipe, COPIANDO aquele
    /// Avatar — mesmo rig mixamo nos dois arquivos, então o clipe de sentar toca no corpo
    /// que já está em cena, sem um segundo skinned mesh;</item>
    /// <item><b>Clipes:</b> renomeados para <c>Marina_Walking</c>/<c>Marina_Sitting</c> e
    /// marcados como <b>Loop Time</b> (a importação vem SEM loop: a Marina sentaria e
    /// congelaria no último frame);</item>
    /// <item><b>Controller:</b> gera <c>Marina.controller</c> de dois estados com o bool
    /// <c>Sitting</c> ligando os dois sentidos — o beat 5 (ela levanta e vai embora) usa a
    /// volta Sitting -&gt; Walking;</item>
    /// <item><b>Cena:</b> remove a cápsula placeholder, pendura o modelo sob um empty
    /// <c>Model</c> que carrega a correção de eixo (o corpo vem DEITADO no FBX — ver
    /// <see cref="ModelCorrectionEuler"/>), zera o <c>baseOffset</c> do NavMeshAgent (era 1,
    /// compensando o pivô no CENTRO da cápsula; o modelo tem pivô nos PÉS) e sobe o centro do
    /// CapsuleCollider pelo mesmo motivo.</item>
    /// </list>
    ///
    /// Não mexe no <see cref="Marina"/> nem no Act1Director: a máquina de estados é dirigida
    /// pelas chamadas que o director JÁ faz (<c>WalkTo</c> / <c>Sit</c>).
    ///
    /// Idempotente: pode rodar de novo à vontade. O <c>.controller</c> existente é reescrito
    /// NO LUGAR (preserva o GUID, e portanto as referências já gravadas na cena) e o filho
    /// <c>Model</c> é reaproveitado em vez de duplicado.
    /// </summary>
    public static class MarinaSetup
    {
        private const string ScenePath = "Assets/Scenes/Cafeteria.unity";
        private const string WalkingFbxPath = "Assets/_Project/Prefabs/Marina_Walking.fbx";
        private const string SittingFbxPath = "Assets/_Project/Prefabs/Marina_Sitting.fbx";
        private const string ControllerPath = "Assets/_Project/Animation/Controllers/Marina.controller";

        private const string WalkingClipName = "Marina_Walking";
        private const string SittingClipName = "Marina_Sitting";

        /// <summary>Parâmetro bool lido pelo <see cref="Marina"/> (campo <c>sittingParameter</c>).</summary>
        private const string SittingParameter = "Sitting";

        private const string ModelChildName = "Model";
        private const string WalkingState = "Walking";
        private const string SittingState = "Sitting";

        /// <summary>Duração do blend entre andar e sentar. Curto: é corte de cutscene, não gameplay.</summary>
        private const float TransitionDuration = 0.25f;

        /// <summary>
        /// Correção de orientação do modelo, aplicada no empty <c>Model</c> — NUNCA na instância
        /// do FBX, que é a raiz do Avatar e tem a rotação reescrita pelo Animator todo frame
        /// (ver <see cref="EnsureModelChild"/>). É esse o motivo de o holder existir mesmo com a
        /// correção em zero: o lugar certo para o ajuste já fica pronto.
        ///
        /// ZERO por padrão porque, no estado atual da cena, a Marina fica de pé sem correção
        /// nenhuma. Este é o ÚNICO botão a mexer se isso mudar: <c>(90, 0, 0)</c> se ela voltar a
        /// deitar, ou um valor no Y se ficar de pé olhando para o lado errado.
        /// </summary>
        private static readonly Vector3 ModelCorrectionEuler = Vector3.zero;

        [MenuItem("Tools/The Delivery/Setup Marina (Ato 1 - Cafeteria)")]
        private static void Run()
        {
            // --- 1. Imports ------------------------------------------------
            var walkingImporter = AssetImporter.GetAtPath(WalkingFbxPath) as ModelImporter;
            var sittingImporter = AssetImporter.GetAtPath(SittingFbxPath) as ModelImporter;

            if (walkingImporter == null || sittingImporter == null)
            {
                Fail($"FBX da Marina não encontrado. Esperado:\n{WalkingFbxPath}\n{SittingFbxPath}");
                return;
            }

            // O modelo vem do FBX de andar: é ele que carrega o skinned mesh que fica em cena.
            Avatar avatar = ConfigureModelFbx(walkingImporter, WalkingClipName);
            if (avatar == null)
            {
                Fail($"Não consegui gerar um Avatar válido para \"{WalkingFbxPath}\".\n" +
                     "Sem Avatar o Animator não sabe mapear a hierarquia e a Marina fica em T-pose.");
                return;
            }

            // O FBX de sentar entra só como FONTE DE CLIPE. Copiando o Avatar do outro os dois
            // clipes falam do MESMO rig — nada de retarget entre esqueletos diferentes.
            ConfigureClipSourceFbx(sittingImporter, SittingClipName, avatar);

            AnimationClip walkingClip = FindClip(WalkingFbxPath, WalkingClipName);
            AnimationClip sittingClip = FindClip(SittingFbxPath, SittingClipName);

            // Copiar o avatar é o caminho preferido (rig idêntico, zero retarget), mas quando a
            // cópia não fecha o Unity não reclama: simplesmente não gera clipe algum. Em vez de
            // parar e mandar o usuário investigar, refaz o import com Avatar PRÓPRIO — os dois
            // esqueletos são humanoides mixamo iguais, então o clipe toca no modelo do mesmo
            // jeito, só passando pelo retarget humanoide.
            if (sittingClip == null)
            {
                Debug.LogWarning(
                    $"[MarinaSetup] \"{SittingFbxPath}\" não gerou clipe com o Avatar copiado do modelo. " +
                    "Refazendo com Avatar próprio (Create From This Model).");

                ConfigureClipSourceFbxWithOwnAvatar(sittingImporter, SittingClipName);
                sittingClip = FindClip(SittingFbxPath, SittingClipName);
            }

            if (walkingClip == null || sittingClip == null)
            {
                Fail("Clipe não encontrado depois do reimport:\n" +
                     $"{WalkingClipName}: {(walkingClip != null ? "ok" : "FALTANDO")}\n" +
                     $"{SittingClipName}: {(sittingClip != null ? "ok" : "FALTANDO")}\n\n" +
                     "Confira se 'Import Animation' está ligado nos dois FBX.");
                return;
            }

            // --- 2. Controller ---------------------------------------------
            AnimatorController controller = BuildController(walkingClip, sittingClip);

            // --- 3. Cena ----------------------------------------------------
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            Marina marina = FindMarina(scene);
            if (marina == null)
            {
                Fail($"Não achei nenhum componente Marina em \"{ScenePath}\".");
                return;
            }

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WalkingFbxPath);
            Animator animator = EnsureModelChild(marina.gameObject, modelAsset, controller, avatar);

            StripPlaceholderMesh(marina.gameObject);
            FixColliderAndAgentForFeetPivot(marina.gameObject);
            AssignSerializedFields(marina, animator, walkingClip);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorGUIUtility.PingObject(marina.gameObject);
            Debug.Log(
                $"[MarinaSetup] Pronto. Modelo \"{modelAsset.name}\" na cena com {WalkingState} <-> {SittingState} " +
                $"(bool \"{SittingParameter}\"), controller em \"{ControllerPath}\". " +
                $"Altura do modelo: {ModelHeight(animator.gameObject):0.00} m — se estiver longe de ~1.7 m, o FBX veio com escala errada. " +
                $"Passada do clipe: {MeasureStrideSpeed(walkingClip):0.00} m/s. " +
                $"Material(is) do modelo: {ModelMaterials(animator)}.",
                marina.gameObject);
        }

        // --- Imports --------------------------------------------------------

        /// <summary>
        /// Prepara o FBX que vira o MODELO em cena: rig humanoide com Avatar próprio e o clipe
        /// nomeado + em loop. Tenta Humanoid primeiro (rig mixamo mapeia sozinho) e cai para
        /// Generic se o mapeamento não fechar — Generic anima igual aqui, já que os dois FBX
        /// compartilham a MESMA hierarquia de ossos; só perde o retarget.
        /// </summary>
        private static Avatar ConfigureModelFbx(ModelImporter importer, string clipName)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            NormalizeImportScale(importer);
            ResetHumanDescription(importer);
            RenameAndLoopSingleClip(importer, clipName);
            importer.SaveAndReimport();

            Avatar avatar = FindAvatar(importer.assetPath);
            if (avatar != null && avatar.isValid)
            {
                ValidateAvatarBindScale(avatar, importer.assetPath);
                return avatar;
            }

            Debug.LogWarning(
                $"[MarinaSetup] O mapeamento Humanoid não fechou em \"{importer.assetPath}\"; " +
                "caindo para rig Generic. As duas animações continuam funcionando (mesmo esqueleto), " +
                "mas não dá para reaproveitar clipes de OUTROS personagens nela.");

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            avatar = FindAvatar(importer.assetPath);
            return avatar != null && avatar.isValid ? avatar : null;
        }

        /// <summary>
        /// Prepara o FBX que entra SÓ como fonte de clipe: mesmo tipo de rig do modelo, Avatar
        /// COPIADO dele e o clipe nomeado + em loop. O skinned mesh deste arquivo nunca vai para
        /// a cena — só o AnimationClip é referenciado pelo controller.
        ///
        /// NÃO chama <see cref="ResetHumanDescription"/>, ao contrário do FBX do modelo: em
        /// "Copy From Other" a descrição humana É a cópia vinda do <paramref name="sourceAvatar"/>,
        /// então limpá-la deixa o rig sem avatar válido, o retarget falha e o arquivo não gera
        /// clipe NENHUM. A escala aqui já vem certa de graça — o avatar copiado é o do modelo,
        /// que foi regerado no tamanho novo.
        /// </summary>
        private static void ConfigureClipSourceFbx(ModelImporter importer, string clipName, Avatar sourceAvatar)
        {
            importer.animationType = sourceAvatar.isHuman
                ? ModelImporterAnimationType.Human
                : ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = sourceAvatar;
            NormalizeImportScale(importer);
            RenameAndLoopSingleClip(importer, clipName);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Faz o FBX importar no TAMANHO CERTO, desligando o "Use File Scale".
        ///
        /// POR QUE: os dois arquivos declaram <c>UnitScaleFactor = 1.0</c>, ou seja "minhas
        /// unidades são centímetros" — mas a geometria foi autorada em METROS (a Marina tem ~1,7
        /// unidades de altura, não ~170). Export desencontrado, típico de Meshy/Blender. Com o
        /// Use File Scale LIGADO o Unity acredita no cabeçalho, multiplica tudo por 0,01 e a
        /// personagem entra com 1,7 CENTÍMETROS.
        ///
        /// Desligado, 1 unidade do arquivo = 1 metro e ela entra com 1,7 m — que é o conserto na
        /// RAIZ do problema. Compensar isso com escala 100 no GameObject da cena (o arranjo que
        /// estava na Cafeteria) resolve só a aparência: a velocidade do clipe, os raios do
        /// NavMeshAgent e as medidas do collider continuam num sistema de unidades que não é o
        /// do resto do jogo — e é daí que vem o pé deslizando.
        /// </summary>
        private static void NormalizeImportScale(ModelImporter importer)
        {
            importer.useFileScale = false;
            importer.globalScale = 1f;
        }

        /// <summary>
        /// Apaga o mapeamento humanoide gravado no <c>.meta</c> para o Unity RE-DERIVAR o avatar
        /// a partir do modelo como ele está agora.
        ///
        /// POR QUE ISSO É OBRIGATÓRIO AO MEXER NA ESCALA: o <c>humanDescription.skeleton</c> guarda
        /// as posições de bind de cada osso em METROS, congeladas no momento em que o avatar foi
        /// gerado. Mudar o import scale depois move a malha e o rig, mas NÃO reescreve essas
        /// posições — o Unity honra o mapeamento salvo. O resultado é um avatar que descreve um
        /// esqueleto 100x menor do que o corpo que ele controla.
        ///
        /// O sintoma é traiçoeiro porque só aparece no PLAY: em edit mode não há Animator
        /// avaliando e a bind pose (que veio da malha, na escala certa) desenha normal. No play o
        /// retarget humanoide passa a posicionar os ossos pelas medidas do avatar e a personagem
        /// COMPRIME.
        ///
        /// Regerar é seguro aqui porque o mapeamento é automático (ossos <c>mixamorig:*</c> batem
        /// com o humanoide do Unity sozinhos) — não há ajuste manual de Configure para perder.
        /// </summary>
        private static void ResetHumanDescription(ModelImporter importer)
        {
            HumanDescription description = importer.humanDescription;
            description.human = new HumanBone[0];
            description.skeleton = new SkeletonBone[0];
            importer.humanDescription = description;
        }

        /// <summary>
        /// Confere que o avatar REGERADO descreve um esqueleto do tamanho do corpo que ele
        /// controla, comparando a altura de bind do quadril com a altura da malha.
        ///
        /// Existe porque o desencontro entre os dois é invisível até o play (ver
        /// <see cref="ResetHumanDescription"/>) — sem esta checagem o menu terminaria com um
        /// "Pronto" tranquilo e o defeito só apareceria testando a cena.
        ///
        /// Lê o AVATAR gerado, e não o <c>humanDescription</c> do importer: com o mapeamento
        /// automático (o caso aqui) os campos <c>human</c>/<c>skeleton</c> do <c>.meta</c> ficam
        /// VAZIOS, porque o Unity deriva tudo no import sem gravar de volta. Consultar o importer
        /// devolveria uma lista vazia e a checagem passaria batido sem verificar nada.
        /// </summary>
        private static void ValidateAvatarBindScale(Avatar avatar, string fbxPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (asset == null)
                return;

            float height = ModelHeight(asset);
            if (height <= 0f)
                return;

            foreach (SkeletonBone bone in avatar.humanDescription.skeleton)
            {
                if (!bone.name.EndsWith("Hips"))
                    continue;

                // O quadril de uma pessoa em pé fica pouco acima da metade da altura; qualquer
                // coisa abaixo de 10% denuncia avatar e malha em escalas diferentes.
                if (bone.position.y < height * 0.1f)
                    Debug.LogError(
                        $"[MarinaSetup] O avatar de \"{fbxPath}\" está fora de escala: quadril a " +
                        $"{bone.position.y:0.0000} m num corpo de {height:0.00} m. No play a Marina vai " +
                        "COMPRIMIR. O mapeamento humanoide não foi regerado — abra o FBX, aba Rig, e " +
                        "clique em Configure > Done para forçar.");

                return;
            }
        }

        /// <summary>
        /// Plano B do <see cref="ConfigureClipSourceFbx"/>: reimporta a fonte de clipe com Avatar
        /// PRÓPRIO em vez de copiado. Aqui o <see cref="ResetHumanDescription"/> volta a ser
        /// necessário — como o avatar passa a ser criado deste modelo, a descrição velha (na
        /// escala antiga) precisa sair da frente para o Unity re-derivar no tamanho atual.
        /// </summary>
        private static void ConfigureClipSourceFbxWithOwnAvatar(ModelImporter importer, string clipName)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            NormalizeImportScale(importer);
            ResetHumanDescription(importer);
            RenameAndLoopSingleClip(importer, clipName);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Materializa o take automático do FBX como clipe explícito com nome próprio e
        /// <b>Loop Time</b> ligado. Sem isso os dois clipes se chamariam "mixamo.com" (o take
        /// original) e nenhum dos dois repetiria: a Marina daria um passo e travaria.
        /// </summary>
        private static void RenameAndLoopSingleClip(ModelImporter importer, string clipName)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning($"[MarinaSetup] \"{importer.assetPath}\" não tem nenhum take de animação.");
                return;
            }

            ModelImporterClipAnimation clip = clips[0];
            clip.name = clipName;
            clip.loopTime = true;

            importer.clipAnimations = new[] { clip };
        }

        // --- Controller -----------------------------------------------------

        /// <summary>
        /// Monta (ou remonta) o controller de dois estados. Walking é o default: a Marina ENTRA
        /// em cena andando; <c>Sitting = true</c> leva para a cadeira e <c>false</c> traz de
        /// volta. Sem <c>hasExitTime</c> — a troca é imediata quando o script pede, não no fim
        /// do ciclo de passada.
        /// </summary>
        private static AnimatorController BuildController(AnimationClip walkingClip, AnimationClip sittingClip)
        {
            string folder = System.IO.Path.GetDirectoryName(ControllerPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'),
                                           System.IO.Path.GetFileName(folder));

            // Reaproveita o asset existente em vez de apagar e recriar: apagar geraria um GUID
            // novo e o Animator já gravado na cena viraria "Missing" (T-pose de novo).
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
                ClearBaseLayer(controller);

            EnsureBoolParameter(controller, SittingParameter);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimatorState walking = sm.AddState(WalkingState);
            walking.motion = walkingClip;

            AnimatorState sitting = sm.AddState(SittingState);
            sitting.motion = sittingClip;

            sm.defaultState = walking;

            AddTransition(walking, sitting, AnimatorConditionMode.If);
            AddTransition(sitting, walking, AnimatorConditionMode.IfNot);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = TransitionDuration;
            transition.AddCondition(mode, 0f, SittingParameter);
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
                if (parameter.name == name)
                {
                    if (parameter.type != AnimatorControllerParameterType.Bool)
                    {
                        controller.RemoveParameter(parameter);
                        break;
                    }

                    return;
                }

            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        }

        /// <summary>
        /// Esvazia a Base Layer para o controller ser remontado mantendo o ASSET (e o GUID) —
        /// é o que torna este menu idempotente sem quebrar quem já aponta para cá.
        /// </summary>
        private static void ClearBaseLayer(AnimatorController controller)
        {
            if (controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                return;
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // 'states'/'stateMachines' devolvem CÓPIAS do array, então remover durante o
            // foreach não invalida a iteração.
            foreach (ChildAnimatorState child in sm.states)
                sm.RemoveState(child.state);

            foreach (ChildAnimatorStateMachine child in sm.stateMachines)
                sm.RemoveStateMachine(child.stateMachine);
        }

        // --- Cena -----------------------------------------------------------

        /// <summary>
        /// Garante a Cafeteria aberta. Se outra cena estiver carregada, oferece salvar antes —
        /// nunca descarta trabalho do usuário no meio do caminho.
        /// </summary>
        private static Scene EnsureSceneOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Acha a Marina inclusive INATIVA — ela começa desligada na cena (o Act1Director só a
        /// acende no beat 3), então varrer por GameObject.Find não acharia nada.
        /// </summary>
        private static Marina FindMarina(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<Marina>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Monta a hierarquia do corpo em DOIS níveis e devolve o Animator já apontando para o
        /// controller e o Avatar:
        /// <code>
        /// Marina            (NavMeshAgent + Marina.cs — o yaw da caminhada vive aqui)
        ///  └ Model          (empty; SÓ carrega a correção de eixo — o Animator não toca nele)
        ///     └ Marina_Walking  (instância do FBX + Animator)
        /// </code>
        ///
        /// O nível do meio é o ponto do conserto. O corpo vem DEITADO de fábrica dentro do FBX,
        /// então precisa de um <see cref="ModelCorrectionEuler"/> para ficar de pé — e esse
        /// ângulo NÃO pode morar na instância do FBX: aquele GameObject é a raiz do Avatar, e o
        /// Animator reescreve a rotação dela a cada frame. Em edit mode (Animator parado) a
        /// correção aplicada lá parece funcionar; no play ela é apagada no primeiro frame e a
        /// Marina volta a deitar com a animação rodando por cima. Num empty ACIMA do Avatar a
        /// rotação é intocável, e como as transforms compõem, a ordem sai certa: o yaw do
        /// NavMeshAgent (raiz, em volta do Y do mundo) por fora, o levantar por dentro.
        /// </summary>
        private static Animator EnsureModelChild(GameObject host, GameObject modelAsset,
            AnimatorController controller, Avatar avatar)
        {
            Transform holderTransform = host.transform.Find(ModelChildName);

            // Migração do layout ANTIGO, em que "Model" ERA a instância do FBX (com o Animator
            // nele). Aquele arranjo é exatamente o que deixa a Marina deitada no play, então o
            // filho é derrubado e remontado nos dois níveis.
            if (holderTransform != null &&
                PrefabUtility.IsPartOfPrefabInstance(holderTransform.gameObject))
            {
                Undo.DestroyObjectImmediate(holderTransform.gameObject);
                holderTransform = null;
            }

            GameObject holder;
            if (holderTransform != null)
            {
                holder = holderTransform.gameObject;
            }
            else
            {
                holder = new GameObject(ModelChildName);
                Undo.RegisterCreatedObjectUndo(holder, "Add Marina Model Holder");
                Undo.SetTransformParent(holder.transform, host.transform, "Parent Marina Model Holder");
            }

            // Varre instâncias do FBX penduradas DIRETO na Marina — sobra de setup feito à mão,
            // fora do holder. Sem isto elas virariam um segundo corpo sobreposto ao correto.
            for (int i = host.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = host.transform.GetChild(i).gameObject;
                if (child != holder &&
                    PrefabUtility.GetCorrespondingObjectFromSource(child) == modelAsset)
                    Undo.DestroyObjectImmediate(child);
            }

            Undo.RecordObject(holder.transform, "Fix Marina Model Orientation");
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.Euler(ModelCorrectionEuler);
            holder.transform.localScale = Vector3.one;

            GameObject model = EnsureModelInstance(holder.transform, modelAsset);

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
                animator = Undo.AddComponent<Animator>(model);
            else
                Undo.RecordObject(animator, "Configure Marina Animator");

            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;

            // Quem desloca a Marina é o NavMeshAgent (via Marina.WalkTo), não o clipe.
            animator.applyRootMotion = false;

            EditorUtility.SetDirty(animator);

            // O Animator de um modelo instanciado PERTENCE ao prefab do FBX: mexer nos campos
            // dele cria um OVERRIDE de instância, e override só é gravado no .unity com esta
            // chamada. Sem ela a atribuição aparece no Inspector e some ao recarregar a cena.
            if (PrefabUtility.IsPartOfPrefabInstance(animator))
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

            return animator;
        }

        /// <summary>
        /// Instancia (ou reaproveita) o FBX dentro do holder, sempre com transform ZERADA — é o
        /// holder que carrega a correção de eixo, e qualquer rotação posta aqui seria apagada
        /// pelo Animator no primeiro frame de play.
        /// </summary>
        private static GameObject EnsureModelInstance(Transform holder, GameObject modelAsset)
        {
            GameObject model = null;

            // Varre os filhos em vez de procurar por nome: o nome da instância acompanha o FBX,
            // e comparar pela ORIGEM é o que impede um segundo skinned mesh sobreposto.
            for (int i = holder.childCount - 1; i >= 0; i--)
            {
                GameObject child = holder.GetChild(i).gameObject;
                if (PrefabUtility.GetCorrespondingObjectFromSource(child) == modelAsset)
                    model = child;
                else
                    Undo.DestroyObjectImmediate(child);
            }

            if (model == null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, holder);
                Undo.RegisterCreatedObjectUndo(model, "Add Marina Model");
            }

            Undo.RecordObject(model.transform, "Reset Marina Model Transform");
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            if (PrefabUtility.IsPartOfPrefabInstance(model.transform))
                PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);

            return model;
        }

        /// <summary>Remove a cápsula de greybox da raiz — senão ela fica flutuando junto do modelo.</summary>
        private static void StripPlaceholderMesh(GameObject host)
        {
            var filter = host.GetComponent<MeshFilter>();
            var renderer = host.GetComponent<MeshRenderer>();

            if (renderer != null)
                Undo.DestroyObjectImmediate(renderer);
            if (filter != null)
                Undo.DestroyObjectImmediate(filter);
        }

        /// <summary>
        /// Reposiciona collider e agent para o pivô NOS PÉS do modelo. A cápsula primitiva tem
        /// pivô no CENTRO, e por isso o setup de greybox usava <c>baseOffset = 1</c> e o collider
        /// centrado na origem; mantidos, a Marina andaria 1 m no ar e o collider ficaria metade
        /// enterrado.
        /// </summary>
        private static void FixColliderAndAgentForFeetPivot(GameObject host)
        {
            var agent = host.GetComponent<NavMeshAgent>();
            if (agent != null && !Mathf.Approximately(agent.baseOffset, 0f))
            {
                Undo.RecordObject(agent, "Fix Marina NavMeshAgent");
                agent.baseOffset = 0f;
                EditorUtility.SetDirty(agent);
            }

            var capsule = host.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                float wanted = capsule.height * 0.5f;
                if (!Mathf.Approximately(capsule.center.y, wanted))
                {
                    Undo.RecordObject(capsule, "Fix Marina Collider");
                    capsule.center = new Vector3(capsule.center.x, wanted, capsule.center.z);
                    EditorUtility.SetDirty(capsule);
                }
            }

            NormalizeHostScale(host);

            // MeshCollider num personagem que ANDA não funciona: côncavo não colide direito e
            // ainda é caro de mover todo frame. Não removo porque pode ter sido posto de
            // propósito, mas fica o aviso.
            if (host.GetComponent<MeshCollider>() != null)
                Debug.LogWarning(
                    "[MarinaSetup] A Marina está com um MeshCollider. Para um personagem movido " +
                    "por NavMeshAgent o certo é um CapsuleCollider (altura ~1.7, centro y ~0.85).",
                    host);
        }

        /// <summary>
        /// Devolve a escala da Marina para 1.
        ///
        /// A escala 100 que estava na cena era a compensação manual do FBX importado 100x menor
        /// (ver <see cref="NormalizeImportScale"/>). Agora que o import entrega a personagem em
        /// metros de verdade, manter o 100 a deixaria com 170 m de altura — e, mais importante,
        /// é essa mistura de sistemas de unidades que fazia a passada do clipe e a velocidade do
        /// NavMeshAgent viverem em escalas diferentes, que é o pé deslizando.
        /// </summary>
        private static void NormalizeHostScale(GameObject host)
        {
            if (host.transform.localScale == Vector3.one)
                return;

            Debug.LogWarning(
                $"[MarinaSetup] Escala da Marina era {host.transform.localScale} e voltou para 1. " +
                "O tamanho agora vem do próprio FBX (Use File Scale desligado), não de esticar o " +
                "GameObject.", host);

            Undo.RecordObject(host.transform, "Normalize Marina Scale");
            host.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(host.transform);
        }

        /// <summary>
        /// Preenche os campos do <see cref="Marina"/> que dependem dos assets recém-importados:
        /// a referência do <c>animator</c> e a <c>clipStrideSpeed</c> medida do clipe.
        ///
        /// O script acharia o Animator sozinho no Awake, mas deixar a referência SERIALIZADA
        /// torna o wiring visível no Inspector — e imune a alguém pendurar um segundo Animator
        /// ali dentro depois.
        /// </summary>
        private static void AssignSerializedFields(Marina marina, Animator animator, AnimationClip walkingClip)
        {
            var so = new SerializedObject(marina);

            SerializedProperty animatorProperty = so.FindProperty("animator");
            if (animatorProperty != null)
                animatorProperty.objectReferenceValue = animator;
            else
                Debug.LogWarning("[MarinaSetup] Campo 'animator' não existe em Marina.cs; " +
                                 "o script vai resolvê-lo por GetComponentInChildren no Awake.");

            float stride = MeasureStrideSpeed(walkingClip);

            SerializedProperty strideProperty = so.FindProperty("clipStrideSpeed");
            if (strideProperty != null)
                strideProperty.floatValue = stride;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(marina);

            WarnIfWalkSpeedIsUnwalkable(so, stride, marina);
        }

        /// <summary>
        /// Avisa quando a <c>walkSpeed</c> está longe demais da passada do clipe.
        ///
        /// A correção de cadência conserta uma diferença PEQUENA acelerando a animação, mas ela
        /// não faz milagre: a 5 m/s (velocidade de CORRIDA) com um clipe de caminhada de ~1,4 m/s
        /// o Animator teria que rodar a 3,6x, e o resultado é a Marina em câmera rápida em vez de
        /// andando. Acima do dobro, o certo é baixar a velocidade — ou trocar por um clipe de
        /// corrida.
        ///
        /// Só avisa, não corrige: <c>walkSpeed</c> é decisão de ritmo da cena (o Act1Director
        /// conta com ela para o timing do beat), não um detalhe técnico para um script arbitrar.
        /// </summary>
        private static void WarnIfWalkSpeedIsUnwalkable(SerializedObject so, float stride, Marina marina)
        {
            SerializedProperty speedProperty = so.FindProperty("walkSpeed");
            if (speedProperty == null || stride <= 0f)
                return;

            float speed = speedProperty.floatValue;
            if (speed <= stride * 2f)
                return;

            Debug.LogWarning(
                $"[MarinaSetup] walkSpeed = {speed:0.0} m/s, mas a passada do clipe é {stride:0.00} m/s. " +
                $"Mesmo com a correção de cadência (Animator a {speed / stride:0.0}x) ela vai parecer " +
                $"acelerada, não andando. Para uma caminhada natural use walkSpeed ~{stride:0.0} m/s.",
                marina);
        }

        /// <summary>
        /// Mede a velocidade PRÓPRIA do clipe de caminhada — o deslocamento embutido na raiz
        /// dividido pela duração — para o <see cref="Marina"/> casar a cadência da animação com
        /// a do NavMeshAgent e o pé parar de deslizar.
        ///
        /// Só o componente HORIZONTAL conta: o <c>averageSpeed</c> traz também o sobe-e-desce do
        /// quadril durante a passada, que não desloca ninguém e só inflaria a medida.
        ///
        /// Devolve 0 quando o clipe é "in place" (mixamo exportado sem root motion): aí não há o
        /// que medir, e o jeito é baixar a <c>walkSpeed</c> à mão até o pé plantar.
        /// </summary>
        private static float MeasureStrideSpeed(AnimationClip clip)
        {
            Vector3 velocity = clip.averageSpeed;
            float stride = new Vector2(velocity.x, velocity.z).magnitude;

            if (stride < 0.05f)
            {
                Debug.LogWarning(
                    $"[MarinaSetup] O clipe \"{clip.name}\" não tem deslocamento na raiz (in place): " +
                    "não dá para medir a passada automaticamente. Se o pé deslizar, ajuste a " +
                    "'Walk Speed' da Marina no Inspector até plantar.");
                return 0f;
            }

            return stride;
        }

        // --- Utilidades -----------------------------------------------------

        /// <summary>
        /// Altura do skinned mesh, para flagrar FBX importado com escala errada. Mede pelo
        /// bounds do MESH (multiplicado pela escala) e não por <c>Renderer.bounds</c>: a Marina
        /// está DESLIGADA na cena até o beat 3, e renderer de objeto inativo devolve bounds
        /// zerado/velho.
        /// </summary>
        private static float ModelHeight(GameObject root)
        {
            float height = 0f;

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null)
                    continue;

                height = Mathf.Max(height, renderer.sharedMesh.bounds.size.y * renderer.transform.lossyScale.y);
            }

            return height;
        }

        /// <summary>
        /// Lista os materiais do modelo. O corpo que vai para a cena vem do
        /// <c>Marina_Walking.fbx</c>, que traz os materiais EMBUTIDOS dele — e não o
        /// <c>Marina.mat</c> montado à mão em cima do <c>Marina.fbx</c> estático. Se a Marina
        /// aparecer sem textura ou com a pele errada, é aqui que se vê o culpado: basta
        /// remapear o material na aba Materials do FBX.
        /// </summary>
        private static string ModelMaterials(Animator animator)
        {
            var names = new List<string>();

            foreach (Renderer renderer in animator.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && !names.Contains(material.name))
                        names.Add(material.name);

            return names.Count > 0 ? string.Join(", ", names) : "nenhum";
        }

        /// <summary>Acha um clipe pelo nome no FBX, ignorando os "__preview__" do Editor.</summary>
        private static AnimationClip FindClip(string fbxPath, string wanted)
        {
            var candidates = new List<AnimationClip>();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    candidates.Add(clip);

            AnimationClip named = candidates.Find(c => c.name == wanted);
            return named != null ? named : (candidates.Count > 0 ? candidates[0] : null);
        }

        private static Avatar FindAvatar(string fbxPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is Avatar avatar)
                    return avatar;

            return null;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[MarinaSetup] {message}");
            EditorUtility.DisplayDialog("Setup Marina", message, "OK");
        }
    }
}

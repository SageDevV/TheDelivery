using System.Collections.Generic;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Liga o BEAT FINAL do Pesadelo — a pegada — no <see cref="PesadeloDirector"/> da cena:
    /// acha o <c>CreatureGrab</c> e o marcador <c>GrabPoint</c>, garante que a criatura TEM
    /// COMO SE MEXER (Avatar no FBX + Animator + controller montado a partir do clipe) e
    /// cria/atribui o pensamento da parada.
    ///
    /// POR QUE ISTO EXISTE COMO COMANDO: as coisas que faltam para o beat funcionar são
    /// exatamente as que não dão erro nenhum quando ficam faltando. Sem a referência, o beat
    /// roda e nada aparece. Sem o controller — ou sem o AVATAR, que é o caso mais traiçoeiro,
    /// porque o Inspector mostra o controller lá e parece resolvido — a criatura aparece na
    /// pose de bind e fica parada de braços abertos. Sem o pensamento, os três segundos de
    /// parada passam em silêncio e parecem um travamento. Nenhum sintoma aponta para a causa.
    ///
    /// Uso: <c>Tools ▸ The Delivery ▸ Pesadelo - Beat Final (Pegada)</c>. A cena Pesadelo é
    /// aberta se ainda não estiver. Depois, para acertar onde a Clear fica pendurada e em que
    /// ponto do clipe ela é solta, use a
    /// <see cref="PesadeloGrabPreviewWindow"/> (<c>Tools ▸ The Delivery ▸ Pesadelo - Ajustar
    /// a Pegada</c>), que faz isso com o clipe scrubável e sem sujar a cena.
    ///
    /// IDEMPOTENTE: rodar de novo reaproveita o que já existe. O controller é reescrito NO
    /// LUGAR (o GUID sobrevive, e com ele as referências), e um pensamento já escrito NÃO é
    /// sobrescrito — o texto é trabalho de roteiro, não de setup.
    /// </summary>
    public static class PesadeloGrabSetup
    {
        private const string ScenePath = "Assets/Scenes/Pesadelo.unity";
        private const string GrabObjectName = "CreatureGrab";

        /// <summary>
        /// O Transform vazio que marca onde e com que rotação a criatura nasce para o
        /// agarrão. Nome fixo porque o comando o procura na cena — renomear o objeto faz o
        /// setup deixar de encontrá-lo, e aí o beat volta ao cálculo automático em silêncio.
        /// </summary>
        private const string GrabPointName = "GrabPoint";
        private const string ControllerPath = "Assets/_Project/Animation/Controllers/CreatureGrab.controller";
        private const string ThoughtPath = "Assets/_Project/ScriptableObjects/Thoughts/Thought_ActPes-2.asset";

        /// <summary>
        /// O texto que a Clear pensa nos segundos parados no fim do corredor. É um PADRÃO:
        /// serve para o beat não estrear mudo, e existe para ser reescrito no Inspector do
        /// asset. Por isso o comando nunca o sobrescreve depois de criado.
        /// </summary>
        private const string DefaultThoughtText = "Acabou o corredor.";

        [MenuItem("Tools/The Delivery/Pesadelo - Beat Final (Pegada)")]
        private static void Run()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PesadeloDirector não encontrado na cena Pesadelo.");
                return;
            }

            var so = new SerializedObject(director);

            GameObject grab = ResolveGrabObject(so, scene);
            if (grab == null)
            {
                Fail($"Não achei nenhum objeto \"{GrabObjectName}\" na cena Pesadelo.\n\n" +
                     "Arraste o FBX CreatureGrab para a cena (desativado) e rode o comando de novo.");
                return;
            }

            bool wiredCreature = SetReference(so, "creatureGrabObject", grab);

            // DESATIVADO na cena, sempre: é assim que o Animator dele fica parado no frame 0,
            // que é onde o agarrão precisa começar. E é o que garante que a criatura APAREÇA
            // no beat — uma que já estava de pé no corredor não aparece.
            if (grab.activeSelf)
            {
                Undo.RecordObject(grab, "Desativar CreatureGrab");
                grab.SetActive(false);
            }

            string animatorReport = EnsureAnimator(grab);
            string markerReport = EnsureGrabPoint(so, scene);

            ThoughtData thought = EnsureThought(out bool thoughtCreated);
            bool wiredThought = thought != null && SetReference(so, "abyssThought", thought);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = grab;
            EditorGUIUtility.PingObject(grab);

            Debug.Log(
                "[PesadeloGrabSetup] Beat final pronto. " +
                $"Campo creatureGrabObject {(wiredCreature ? $"ligado em \"{grab.name}\"" : "NÃO ligado — ver avisos acima")}; " +
                $"{animatorReport}; " +
                $"{markerReport}; " +
                $"pensamento da parada {(wiredThought ? $"ligado em \"{thought.name}\"{(thoughtCreated ? " (criado agora)" : " (já existia)")}" : "NÃO ligado")}.\n" +
                "Para testar só este beat: ligue o Debug Start At Abyss no Inspector do director e dê Play " +
                "com esta cena aberta (ou use a tecla 5 com o Debug Mode ligado).",
                director);
        }

        /// <summary>
        /// A pose em que o beat planta a criatura, reproduzida no editor: o fim do corredor
        /// mais <c>grabDistance</c> ao longo do eixo do corredor, encarando de volta para
        /// quem chega. É a mesma conta do <c>StageGrab</c>, só que a partir da marcação Abyss
        /// em vez de a partir da Clear — que em edição não está lá.
        /// </summary>
        internal static bool TryGetStagingPose(SerializedObject directorSo, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            SerializedProperty yawFromMarker = directorSo.FindProperty("grabYaw");

            // O MARCADOR TEM PRECEDÊNCIA, igual ao StageGrab do director. Se a ferramenta
            // plantasse pela conta automática enquanto o jogo planta pelo marcador, o
            // encaixe seria ajustado contra uma pose que nunca vai para a tela — que é o
            // pior tipo de ferramenta de ajuste que existe.
            var marker = directorSo.FindProperty("grabSpawnPoint")?.objectReferenceValue as Transform;
            if (marker != null)
            {
                position = marker.position;
                rotation = marker.rotation * Quaternion.Euler(0f, yawFromMarker != null ? yawFromMarker.floatValue : 0f, 0f);
                return true;
            }

            if (!TryGetCorridorAxis(directorSo, out Vector3 axis, out Transform abyss))
                return false;

            SerializedProperty distanceProperty = directorSo.FindProperty("grabDistance");
            SerializedProperty yawProperty = directorSo.FindProperty("grabYaw");
            float distance = distanceProperty != null ? distanceProperty.floatValue : 1.4f;
            float yaw = yawProperty != null ? yawProperty.floatValue : 0f;

            position = abyss.position + axis * Mathf.Max(0.1f, distance);
            rotation = Quaternion.LookRotation(-axis, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
            return true;
        }

        /// <summary>
        /// A direção em que o corredor corre e o marcador do fim dele. Mesma conta do
        /// <c>CorridorAxis</c> do director: do ponto do rosnado (ou do spawn) para a beira.
        /// É por este eixo que tudo do beat final é medido — onde a criatura nasce, para
        /// onde a Clear encara e para que lado ela é jogada.
        /// </summary>
        internal static bool TryGetCorridorAxis(SerializedObject directorSo, out Vector3 axis, out Transform abyss)
        {
            axis = Vector3.forward;

            abyss = directorSo.FindProperty("abyssPoint")?.objectReferenceValue as Transform;
            if (abyss == null)
                return false;

            var from = directorSo.FindProperty("growlPoint")?.objectReferenceValue as Transform;
            if (from == null)
                from = directorSo.FindProperty("spawnPoint")?.objectReferenceValue as Transform;

            Vector3 direction = from != null ? abyss.position - from.position : abyss.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                axis = direction.normalized;

            return true;
        }

        // --- Animator ------------------------------------------------------

        /// <summary>
        /// Garante que o CreatureGrab tem Animator COM controller. Sem isso o modelo aparece
        /// em T-pose — o beat "funciona" e a criatura não encosta em ninguém.
        ///
        /// O controller é de UM ESTADO SÓ, sem parâmetro nenhum, como o dos figurantes: a
        /// criatura entra tocando o agarrão e não sai dele. A diferença é que aqui o clipe
        /// NÃO é marcado como Loop Time — o arremesso acontece uma vez, e um agarrão em loop
        /// deixaria a criatura pegando a Clear de novo depois de já a ter jogado no chão.
        /// </summary>
        /// <returns>Uma frase para o log, dizendo o que foi feito.</returns>
        private static string EnsureAnimator(GameObject grab)
        {
            Animator animator = grab.GetComponentInChildren<Animator>(includeInactive: true);
            if (animator == null)
            {
                Undo.RegisterCompleteObjectUndo(grab, "Adicionar Animator ao CreatureGrab");
                animator = Undo.AddComponent<Animator>(grab);
            }

            string fbxPath = ResolveModelPath(grab);
            if (string.IsNullOrEmpty(fbxPath))
            {
                Debug.LogWarning("[PesadeloGrabSetup] Não achei o FBX de origem do CreatureGrab, então não dá para " +
                                 "montar o controller. Monte-o com Tools ▸ The Delivery ▸ Build Looping Clip " +
                                 "Controller (desmarcando o Loop Time) e atribua ao Animator à mão.", grab);
                return "Animator SEM controller — a criatura vai aparecer em T-pose";
            }

            // O AVATAR VEM PRIMEIRO, e vem MESMO QUE JÁ EXISTA UM CONTROLLER. Um controller
            // atribuído à mão num rig sem Avatar é exatamente o caso que parece resolvido e
            // não anima — se este método desistisse aqui por "já tem controller", o comando
            // nunca chegaria a consertar o que de fato está quebrado.
            bool reimported = PrepareModelImport(fbxPath);
            AssignAvatar(animator, fbxPath, grab);

            if (animator.runtimeAnimatorController != null)
            {
                FinishAnimator(animator, grab);
                return $"Animator já tinha controller (\"{animator.runtimeAnimatorController.name}\")" +
                       (reimported ? ", e o FBX foi reimportado para ganhar Avatar" : string.Empty);
            }

            AnimationClip clip = FindClip(fbxPath);
            if (clip == null)
            {
                Debug.LogWarning($"[PesadeloGrabSetup] Nenhum AnimationClip em \"{fbxPath}\". Confira se o " +
                                 "'Import Animation' está ligado no FBX do CreatureGrab.", grab);
                return "Animator SEM controller — o FBX não trouxe animação";
            }

            AnimatorController controller = BuildController(clip);

            Undo.RecordObject(animator, "Atribuir controller ao CreatureGrab");
            animator.runtimeAnimatorController = controller;

            FinishAnimator(animator, grab);

            return $"controller \"{controller.name}\" montado com o clipe \"{clip.name}\"" +
                   (reimported ? " (FBX reimportado para ganhar Avatar)" : string.Empty);
        }

        /// <summary>
        /// Liga o Avatar do FBX ao Animator. É o mapa da hierarquia de ossos: sem ele o
        /// Animator não tem como ligar as curvas do clipe aos transforms do modelo, e a
        /// criatura fica na pose de bind — sem erro nenhum no console.
        /// </summary>
        private static void AssignAvatar(Animator animator, string fbxPath, GameObject grab)
        {
            Avatar avatar = FindAvatar(fbxPath);
            if (avatar == null)
            {
                Debug.LogWarning($"[PesadeloGrabSetup] O FBX \"{fbxPath}\" continua SEM Avatar mesmo depois do " +
                                 "reimport — um rig Generic sem Avatar não anima. Abra o FBX, aba Rig, ponha " +
                                 "Animation Type em Generic e Avatar Definition em \"Create From This Model\", " +
                                 "e rode o comando de novo.", grab);
                return;
            }

            if (animator.avatar == avatar)
                return;

            Undo.RecordObject(animator, "Atribuir Avatar ao CreatureGrab");
            animator.avatar = avatar;
        }

        /// <summary>Fecha a configuração do Animator e GRAVA o override na cena.</summary>
        private static void FinishAnimator(Animator animator, GameObject grab)
        {
            // O agarrão é coreografia parada: quem move a Clear é a câmera do beat, e root
            // motion aqui faria a criatura sair andando para fora do corredor durante o bote.
            //
            // É SÓ O PADRÃO, e não a palavra final: o PesadeloDirector reescreve este campo
            // na montagem do beat, a partir do Grab Root Motion do Inspector dele. O passo à
            // frente do fim do clipe é o motivo — dependendo de o FBX ter sido exportado
            // in-place ou com raiz, ele precisa ou não de root motion para SAIR DO LUGAR, e
            // isso não se decide por dedução. Quem grava aqui é o setup; quem manda em play
            // é o director.
            animator.applyRootMotion = false;

            EditorUtility.SetDirty(animator);

            // O Animator de um modelo arrastado para a cena PERTENCE ao prefab do FBX: mexer
            // nos campos dele cria um OVERRIDE de instância, e override só é gravado no
            // .unity com esta chamada. Sem ela a atribuição aparece no Inspector e SOME ao
            // salvar/recarregar a cena — e a criatura volta a aparecer parada.
            if (PrefabUtility.IsPartOfPrefabInstance(animator))
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        }

        /// <summary>
        /// Conserta as import settings do FBX que impedem a animação de existir.
        ///
        /// O QUE ISTO RESOLVE, e é a causa mais comum de "a criatura aparece parada": um rig
        /// <b>Generic SEM Avatar</b>. O Avatar é o mapa da hierarquia de ossos — sem ele o
        /// Animator não tem como ligar as curvas do clipe aos transforms do modelo, e o
        /// resultado é o modelo na pose de bind, imóvel. Não sai erro nenhum no console: do
        /// ponto de vista da Unity está tudo certo, só não há para onde mandar a animação.
        ///
        /// Um FBX importado com o Animation Type em None também não traz clipe algum, então
        /// ele é promovido a Generic no caminho.
        ///
        /// O Loop Time NÃO é tocado, ao contrário do que o Build Looping Clip Controller
        /// faz: o agarrão acontece uma vez. Em loop, a criatura pegaria a Clear de novo
        /// depois de já a ter jogado no chão.
        /// </summary>
        /// <returns>true se o asset precisou ser reimportado.</returns>
        private static bool PrepareModelImport(string fbxPath)
        {
            if (FindAvatar(fbxPath) != null)
                return false;

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
                return false;

            if (importer.animationType == ModelImporterAnimationType.None)
                importer.animationType = ModelImporterAnimationType.Generic;

            importer.importAnimation = true;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            Debug.Log($"[PesadeloGrabSetup] \"{fbxPath}\" reimportado com Avatar (Create From This Model). " +
                      "Sem Avatar, um rig Generic aparece na pose de bind e não anima — era isso que deixava " +
                      "a criatura parada.");
            return true;
        }

        /// <summary>
        /// Controller de estado único com o clipe do agarrão. Reaproveita o asset que já
        /// estiver no caminho em vez de apagar e criar outro: apagar geraria um GUID novo e
        /// toda referência existente viraria "Missing".
        /// </summary>
        private static AnimatorController BuildController(AnimationClip clip)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else if (controller.layers.Length > 0)
            {
                AnimatorStateMachine existing = controller.layers[0].stateMachine;

                // 'states' devolve uma CÓPIA do array, então remover durante o foreach não
                // invalida a iteração.
                foreach (ChildAnimatorState child in existing.states)
                    existing.RemoveState(child.state);
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState state = sm.AddState("Grab");
            state.motion = clip;
            sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // --- Pensamento ----------------------------------------------------

        /// <summary>
        /// O pensamento dos segundos parados. Criado com um texto padrão se ainda não
        /// existir; NUNCA reescrito depois — o texto é roteiro, e um comando de setup que
        /// apagasse a fala escrita seria uma armadilha.
        /// </summary>
        private static ThoughtData EnsureThought(out bool created)
        {
            created = false;

            var existing = AssetDatabase.LoadAssetAtPath<ThoughtData>(ThoughtPath);
            if (existing != null)
                return existing;

            var thought = ScriptableObject.CreateInstance<ThoughtData>();

            var so = new SerializedObject(thought);
            SerializedProperty lines = so.FindProperty("lines");
            if (lines != null)
            {
                lines.arraySize = 1;
                SerializedProperty line = lines.GetArrayElementAtIndex(0);
                line.FindPropertyRelative("text").stringValue = DefaultThoughtText;
                // duration 0 = calculada pelo tamanho do texto (ver ThoughtSystem). O delay
                // deixa a frase chegar DEPOIS de o silêncio começar: ela para, percebe, e só
                // então pensa.
                line.FindPropertyRelative("duration").floatValue = 0f;
                line.FindPropertyRelative("delay").floatValue = 0.6f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.CreateAsset(thought, ThoughtPath);
            AssetDatabase.SaveAssets();
            created = true;
            return thought;
        }

        // --- Utilidades ----------------------------------------------------

        /// <summary>
        /// O CreatureGrab: o que já estiver no campo do director, ou o objeto de mesmo nome
        /// na cena. A busca inclui INATIVOS — ele vive desativado, que é justamente o estado
        /// em que o beat precisa encontrá-lo.
        /// </summary>
        private static GameObject ResolveGrabObject(SerializedObject directorSo, Scene scene)
        {
            var assigned = directorSo.FindProperty("creatureGrabObject")?.objectReferenceValue as GameObject;
            if (assigned != null)
                return assigned;

            Transform found = FindByName(scene, GrabObjectName);
            return found != null ? found.gameObject : null;
        }

        /// <summary>
        /// Liga o MARCADOR DA PEGADA (o GrabPoint) ao director: o Transform vazio que carrega
        /// a posição e a rotação em que a criatura nasce para o agarrão.
        ///
        /// Não achar o marcador NÃO é erro: sem ele o beat cai no cálculo automático
        /// (Grab Distance ao longo do eixo do corredor) e funciona. É por isso que este passo
        /// só relata, em vez de abortar o comando inteiro como a falta do CreatureGrab faz.
        ///
        /// O que já estiver atribuído tem precedência: quem apontou o campo para outro
        /// marcador à mão não pode ter isso desfeito por um comando de setup.
        /// </summary>
        /// <returns>Uma frase para o log, dizendo o que foi feito.</returns>
        private static string EnsureGrabPoint(SerializedObject directorSo, Scene scene)
        {
            var assigned = directorSo.FindProperty("grabSpawnPoint")?.objectReferenceValue as Transform;
            if (assigned != null)
                return $"marcador da pegada já ligado em \"{assigned.name}\"";

            Transform marker = FindByName(scene, GrabPointName);
            if (marker == null)
            {
                return $"nenhum \"{GrabPointName}\" na cena — a pose da criatura vai ser calculada pelo Grab Distance";
            }

            SetReference(directorSo, "grabSpawnPoint", marker);
            return $"marcador da pegada ligado em \"{marker.name}\"";
        }

        /// <summary>
        /// Acha um objeto pelo nome na cena, INCLUSIVE inativo — o CreatureGrab vive
        /// desativado, e um marcador pode estar dentro de um grupo desligado.
        /// </summary>
        internal static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t.name == name)
                        return t;
                }
            }

            return null;
        }

        /// <summary>Caminho do FBX de origem da instância na cena.</summary>
        private static string ResolveModelPath(GameObject instance)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }

            // Sem instância de prefab (alguém desfez o vínculo): procura o FBX pelo nome.
            foreach (string guid in AssetDatabase.FindAssets($"{GrabObjectName} t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }

        /// <summary>Primeiro AnimationClip do FBX, ignorando os "__preview__" do Editor.</summary>
        private static AnimationClip FindClip(string fbxPath)
        {
            var candidates = new List<AnimationClip>();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    candidates.Add(clip);

            return candidates.Count > 0 ? candidates[0] : null;
        }

        private static Avatar FindAvatar(string fbxPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is Avatar avatar)
                    return avatar;

            return null;
        }

        private static bool SetReference(SerializedObject directorSo, string propertyName, Object value)
        {
            SerializedProperty property = directorSo.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning("[PesadeloGrabSetup] O PesadeloDirector desta cena não tem o campo " +
                                 $"\"{propertyName}\" — recompilou depois de atualizar o script?");
                return false;
            }

            property.objectReferenceValue = value;
            directorSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(directorSo.targetObject);
            return true;
        }

        internal static PesadeloDirector FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<PesadeloDirector>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>Garante a cena Pesadelo aberta, oferecendo salvar o que estiver aberto antes.</summary>
        internal static Scene EnsureSceneOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[PesadeloGrabSetup] {message}");
            EditorUtility.DisplayDialog("Beat Final do Pesadelo", message, "OK");
        }
    }
}

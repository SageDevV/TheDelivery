using TheDelivery.Narrative;
using TheDelivery.Player;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MONTA O AVISO "Segure SHIFT para correr" e o liga ao PesadeloDirector.
    ///
    /// O aviso é um irmão do StandUpPrompt dentro do Canvas do PLAYER, e não um objeto solto
    /// na cena Pesadelo — é onde a UI de instrução deste jogo mora (o StandUpPrompt e o
    /// InteractionPrompt já estão lá), e é o que faz o aviso existir em qualquer cena que use
    /// o player, sem ninguém remontá-lo.
    ///
    /// E ele é feito por CÓPIA do StandUpPrompt, de propósito: fonte, tamanho, cor,
    /// ancoragem e material vêm prontos do irmão que já está autorado e aprovado na tela.
    /// Criar um TextMeshProUGUI do zero daria um texto com a fonte padrão do TMP, em outro
    /// tamanho e em outro lugar — e a diferença só apareceria rodando o beat.
    ///
    /// Entra DESATIVADO: quem o acende é o director, no começo da fuga (ver
    /// <c>UpdateRunPrompt</c>). Aceso no prefab, ele apareceria em todas as cenas do jogo.
    /// </summary>
    public static class PesadeloRunPromptSetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Models/Player.prefab";
        private const string ScenePath = "Assets/Scenes/Pesadelo.unity";
        private const string ModelName = "StandUpPrompt";
        private const string PromptName = "RunPrompt";
        private const string PromptText = "Segure SHIFT para correr";

        [MenuItem("Tools/The Delivery/Pesadelo - Aviso da Corrida (SHIFT)")]
        private static void Run()
        {
            if (!EnsurePromptInPrefab(out string prefabStatus))
                return;

            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PesadeloDirector não encontrado na cena Pesadelo.");
                return;
            }

            GameObject prompt = FindPromptInScene(scene);
            if (prompt == null)
            {
                Fail($"O \"{PromptName}\" foi criado no prefab do Player, mas não apareceu na cena Pesadelo.\n\n" +
                     "O Player desta cena é uma instância de outro prefab? Rode o comando de novo depois de conferir.");
                return;
            }

            var so = new SerializedObject(director);
            SerializedProperty field = so.FindProperty("runPrompt");
            if (field == null)
            {
                Fail("O campo \"runPrompt\" não existe no PesadeloDirector. Os scripts estão compilando? " +
                     "Espere a compilação terminar e rode de novo.");
                return;
            }

            bool alreadyWired = field.objectReferenceValue == prompt;
            if (!alreadyWired)
            {
                field.objectReferenceValue = prompt;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            string resumo =
                $"Prefab do Player: {prefabStatus}\n" +
                $"Texto: \"{PromptText}\"\n" +
                $"PesadeloDirector: {(alreadyWired ? "já estava ligado" : "ligado agora, cena salva")}";

            Debug.Log($"[PesadeloRunPromptSetup] {resumo.Replace("\n", " | ")}", director);
            EditorUtility.DisplayDialog("Aviso da Corrida",
                                        resumo + "\n\nEle acende no começo da fuga, assim que o pensamento do rosnado " +
                                        "sai da tela, e some quando a Clear corre (ou pelo Run Prompt Duration).",
                                        "Ok");
        }

        /// <summary>
        /// Garante o objeto do aviso dentro do prefab do Player, copiando o StandUpPrompt.
        /// Idempotente: com o aviso já lá, o prefab não é reescrito — só o texto é conferido,
        /// porque é a única coisa que alguém pode ter mexido sem querer.
        /// </summary>
        private static bool EnsurePromptInPrefab(out string status)
        {
            status = string.Empty;

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Fail($"Não achei o prefab do Player em {PlayerPrefabPath}.");
                return false;
            }

            try
            {
                Transform existing = FindChild(root.transform, PromptName);
                if (existing != null)
                {
                    status = $"\"{PromptName}\" já existia";
                    return true;
                }

                Transform model = FindChild(root.transform, ModelName);
                if (model == null)
                {
                    Fail($"Não achei o \"{ModelName}\" dentro do prefab do Player para copiar.\n\n" +
                         "Ele é o molde do aviso (fonte, tamanho, posição na tela). Sem ele eu não sei com que cara " +
                         "o texto deve aparecer — monte o aviso à mão como irmão dele no Canvas.");
                    return false;
                }

                GameObject copy = Object.Instantiate(model.gameObject, model.parent);
                copy.name = PromptName;
                // Logo abaixo do molde na hierarquia: são a mesma família de UI, e um filho
                // solto no fim da lista é um convite a que alguém o mova sem saber o que é.
                copy.transform.SetSiblingIndex(model.GetSiblingIndex() + 1);

                var label = copy.GetComponent<TMP_Text>();
                if (label != null)
                    label.text = PromptText;

                copy.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                status = $"\"{PromptName}\" criado (cópia do {ModelName})";
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject FindPromptInScene(Scene scene)
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                PlayerController player = rootObject.GetComponentInChildren<PlayerController>(includeInactive: true);
                if (player == null)
                    continue;

                Transform found = FindChild(player.transform.root, PromptName);
                if (found != null)
                    return found.gameObject;
            }

            return null;
        }

        /// <summary>Procura um filho pelo nome em toda a hierarquia, inclusive nos desativados.</summary>
        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name == name)
                    return t;
            }

            return null;
        }

        private static PesadeloDirector FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                PesadeloDirector found = root.GetComponentInChildren<PesadeloDirector>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Scene EnsureSceneOpen()
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
            Debug.LogError($"[PesadeloRunPromptSetup] {message}");
            EditorUtility.DisplayDialog("Aviso da Corrida", message, "Ok");
        }
    }
}

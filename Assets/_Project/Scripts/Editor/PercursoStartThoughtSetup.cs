using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// LIGA O PENSAMENTO DE PARTIDA ao PercursoDirector da Estrada — a primeira frase do
    /// ato, dita no quadro em que a Clear aparece na rua.
    ///
    /// NÃO HÁ CÓDIGO NOVO POR TRÁS DISTO: o director já dispara o Start Thought na abertura
    /// (sem bloquear — ela anda enquanto a frase está na tela). O que faltava era o asset e
    /// a referência, e é isso que este comando resolve, pelo mesmo motivo dos outros
    /// comandos de montagem: uma referência arrastada à mão numa cena que alguém pode
    /// descartar não é uma montagem, é uma lembrança.
    ///
    /// O TEXTO NÃO É DAQUI. A fala mora no asset (Thought_ActPer-1) e se edita lá, no
    /// Inspector, como qualquer outro pensamento do jogo.
    /// </summary>
    public static class PercursoStartThoughtSetup
    {
        private const string ScenePath = "Assets/Scenes/Estrada.unity";
        private const string ThoughtPath = "Assets/_Project/ScriptableObjects/Thoughts/Thought_ActPer-1.asset";
        private const string FieldName = "startThought";
        private const string DefaultLine = "Preciso ir para a casa";

        [MenuItem("Tools/The Delivery/Estrada - Pensamento de partida")]
        private static void Run()
        {
            ThoughtData thought = AssetDatabase.LoadAssetAtPath<ThoughtData>(ThoughtPath);
            if (thought == null)
            {
                thought = CreateThought();
                if (thought == null)
                    return;
            }

            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PercursoDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PercursoDirector não encontrado na cena Estrada.");
                return;
            }

            var so = new SerializedObject(director);
            SerializedProperty field = so.FindProperty(FieldName);
            if (field == null)
            {
                Fail($"O campo \"{FieldName}\" não existe no PercursoDirector.");
                return;
            }

            if (field.objectReferenceValue == thought)
            {
                Debug.Log($"[PercursoStartThoughtSetup] \"{thought.name}\" já está ligado ao PercursoDirector.");
                EditorUtility.DisplayDialog("Pensamento de partida",
                                            $"\"{thought.name}\" já está no campo Start Thought.\n\n" +
                                            "O texto se edita no asset:\n" + ThoughtPath,
                                            "Ok");
                return;
            }

            field.objectReferenceValue = thought;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[PercursoStartThoughtSetup] \"{thought.name}\" ligado ao PercursoDirector; cena Estrada salva.", director);
            EditorUtility.DisplayDialog("Pensamento de partida",
                                        $"\"{thought.name}\" ligado ao campo Start Thought do PercursoDirector, e a " +
                                        "cena Estrada foi salva.\n\n" +
                                        "Ele entra na abertura do ato, sem travar a caminhada.\n\n" +
                                        "O texto se edita no asset:\n" + ThoughtPath,
                                        "Ok");
        }

        /// <summary>
        /// Cria o asset com a fala. O delay da linha cobre o fade do GameManager, que ainda
        /// está clareando quando o ato começa — sem ele a frase apareceria por baixo do preto
        /// e teria metade do tempo dela consumida por uma tela que ninguém ainda vê.
        /// </summary>
        private static ThoughtData CreateThought()
        {
            var thought = ScriptableObject.CreateInstance<ThoughtData>();
            var so = new SerializedObject(thought);

            SerializedProperty lines = so.FindProperty("lines");
            lines.arraySize = 1;

            SerializedProperty line = lines.GetArrayElementAtIndex(0);
            line.FindPropertyRelative("text").stringValue = DefaultLine;
            // duration 0 = o ThoughtSystem calcula pelo tamanho do texto, como nos outros.
            line.FindPropertyRelative("duration").floatValue = 0f;
            line.FindPropertyRelative("delay").floatValue = 2f;

            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(thought, ThoughtPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PercursoStartThoughtSetup] Asset criado: {ThoughtPath}");

            return AssetDatabase.LoadAssetAtPath<ThoughtData>(ThoughtPath);
        }

        private static PercursoDirector FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                PercursoDirector found = root.GetComponentInChildren<PercursoDirector>(includeInactive: true);
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
            Debug.LogError($"[PercursoStartThoughtSetup] {message}");
            EditorUtility.DisplayDialog("Pensamento de partida", message, "Ok");
        }
    }
}

using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// LIGA O PENSAMENTO DE PARTIDA ao JourneyDirector da Road — a primeira frase do
    /// ato, dita no quadro em que a Clear aparece na rua.
    ///
    /// NÃO HÁ CÓDIGO NOVO POR TRÁS DISTO: o director já dispara o Start Thought na abertura
    /// (sem bloquear — ela anda enquanto a frase está na tela). O que faltava era o asset e
    /// a referência, e é isso que este comando resolve, pelo mesmo motivo dos outros
    /// comandos de montagem: uma referência arrastada à mão numa cena que alguém pode
    /// descartar não é uma montagem, é uma lembrança.
    ///
    /// O TEXTO NÃO É DAQUI. A fala mora no asset (Thought_ActJourney-1) e se edita lá, no
    /// Inspector, como qualquer outro pensamento do jogo.
    /// </summary>
    public static class JourneyStartThoughtSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string ThoughtPath = "Assets/_Project/ScriptableObjects/Thoughts/Thought_ActJourney-1.asset";
        private const string FieldName = "startThought";
        private const string DefaultLine = "Preciso ir para a casa";

        [MenuItem("Tools/The Delivery/Road - Pensamento de partida")]
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

            JourneyDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("JourneyDirector não encontrado na cena Road.");
                return;
            }

            var so = new SerializedObject(director);
            SerializedProperty field = so.FindProperty(FieldName);
            if (field == null)
            {
                Fail($"O campo \"{FieldName}\" não existe no JourneyDirector.");
                return;
            }

            if (field.objectReferenceValue == thought)
            {
                Debug.Log($"[JourneyStartThoughtSetup] \"{thought.name}\" já está ligado ao JourneyDirector.");
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

            Debug.Log($"[JourneyStartThoughtSetup] \"{thought.name}\" ligado ao JourneyDirector; cena Road salva.", director);
            EditorUtility.DisplayDialog("Pensamento de partida",
                                        $"\"{thought.name}\" ligado ao campo Start Thought do JourneyDirector, e a " +
                                        "cena Road foi salva.\n\n" +
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
            Debug.Log($"[JourneyStartThoughtSetup] Asset criado: {ThoughtPath}");

            return AssetDatabase.LoadAssetAtPath<ThoughtData>(ThoughtPath);
        }

        private static JourneyDirector FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                JourneyDirector found = root.GetComponentInChildren<JourneyDirector>(includeInactive: true);
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
            Debug.LogError($"[JourneyStartThoughtSetup] {message}");
            EditorUtility.DisplayDialog("Pensamento de partida", message, "Ok");
        }
    }
}

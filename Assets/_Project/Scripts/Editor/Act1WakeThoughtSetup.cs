using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// LIGA O PENSAMENTO DO PESADELO ao Act1Director da Cafeteria — o primeiro pensamento
    /// do ato, o que a Clear pensa antes de tirar a cabeça da mesa.
    ///
    /// POR QUE ISTO EXISTE: o campo é uma referência de asset num componente de CENA, e
    /// arrastar à mão é onde este tipo de coisa some — a cena fica suja, alguém dá Play,
    /// descarta, e a única prova de que o beat foi montado era o arraste. Aqui o vínculo é
    /// declarado uma vez, com o caminho do asset no código, e o comando é repetível: se já
    /// estiver ligado, não escreve nada.
    ///
    /// O TEXTO NÃO É DAQUI. Este comando só faz a ligação; a fala está no asset
    /// (Thought_Act1-0) e é lá que se escreve, com o Inspector, como qualquer outro
    /// pensamento do jogo. O asset é CRIADO aqui se não existir, com uma primeira versão
    /// da fala, para o comando funcionar num projeto que ainda não o tem.
    /// </summary>
    public static class Act1WakeThoughtSetup
    {
        private const string ScenePath = "Assets/Scenes/Cafeteria.unity";
        private const string ThoughtPath = "Assets/_Project/ScriptableObjects/Thoughts/Thought_Act1-0.asset";
        private const string FieldName = "nightmareThought";

        [MenuItem("Tools/The Delivery/Ato 1 - Pensamento ao acordar do pesadelo")]
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

            Act1Director director = FindDirector(scene);
            if (director == null)
            {
                Fail("Act1Director não encontrado na cena Cafeteria.");
                return;
            }

            var so = new SerializedObject(director);
            SerializedProperty prop = so.FindProperty(FieldName);
            if (prop == null)
            {
                Fail($"O campo \"{FieldName}\" não existe no Act1Director. " +
                     "Os scripts recompilaram? Espere a compilação terminar e rode de novo.");
                return;
            }

            if (prop.objectReferenceValue == thought)
            {
                Debug.Log($"[Act1WakeThoughtSetup] \"{thought.name}\" já está ligado ao Act1Director. Nada a fazer.");
                EditorUtility.DisplayDialog("Pensamento ao acordar",
                                            $"\"{thought.name}\" já está no campo Nightmare Thought do Act1Director.\n\n" +
                                            "O texto se edita no próprio asset:\n" + ThoughtPath,
                                            "Ok");
                return;
            }

            prop.objectReferenceValue = thought;
            so.ApplyModifiedProperties();

            // A cena é salva no ato: uma referência ligada que só existe na cena aberta se
            // perde no primeiro "descartar alterações", e o sintoma disso aparece só em
            // Play, com o ato abrindo direto no fim de tarde de novo.
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Act1WakeThoughtSetup] \"{thought.name}\" ligado ao Act1Director e a cena Cafeteria foi salva.");
            EditorUtility.DisplayDialog("Pensamento ao acordar",
                                        $"\"{thought.name}\" ligado ao campo Nightmare Thought do Act1Director, " +
                                        "e a cena Cafeteria foi salva.\n\n" +
                                        "Ele aparece com a cabeça ainda na mesa e o pensamento de fim de tarde " +
                                        "(Thought_Act1-1) entra atrás dele.\n\n" +
                                        "O texto se edita no asset:\n" + ThoughtPath,
                                        "Ok");
        }

        /// <summary>
        /// Cria o asset com a fala inicial. As duas linhas são um rascunho para o beat
        /// existir de ponta a ponta desde o primeiro Play — o texto definitivo é do autor,
        /// no Inspector. O delay da primeira cobre o fade do GameManager, que ainda está
        /// clareando quando o beat começa.
        /// </summary>
        private static ThoughtData CreateThought()
        {
            var thought = ScriptableObject.CreateInstance<ThoughtData>();
            var so = new SerializedObject(thought);
            SerializedProperty lines = so.FindProperty("lines");
            lines.arraySize = 2;

            SetLine(lines.GetArrayElementAtIndex(0), "Que sonho foi esse...", 2f);
            SetLine(lines.GetArrayElementAtIndex(1), "Foi só um sonho... só um sonho.", 0.6f);

            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(thought, ThoughtPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Act1WakeThoughtSetup] Asset criado: {ThoughtPath}");

            return AssetDatabase.LoadAssetAtPath<ThoughtData>(ThoughtPath);
        }

        private static void SetLine(SerializedProperty line, string text, float delay)
        {
            line.FindPropertyRelative("text").stringValue = text;
            // duration 0 = o ThoughtSystem calcula pelo tamanho do texto, que é o padrão
            // dos outros pensamentos do jogo.
            line.FindPropertyRelative("duration").floatValue = 0f;
            line.FindPropertyRelative("delay").floatValue = delay;
        }

        private static Act1Director FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Act1Director found = root.GetComponentInChildren<Act1Director>(includeInactive: true);
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
            Debug.LogError($"[Act1WakeThoughtSetup] {message}");
            EditorUtility.DisplayDialog("Pensamento ao acordar", message, "Ok");
        }
    }
}

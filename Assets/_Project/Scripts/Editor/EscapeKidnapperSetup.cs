using System.Collections.Generic;
using TheDelivery.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// COLOCA O SEQUESTRADOR NA ESCAPE com a MESMA caçada do killer do Ato 4: a FSM do
    /// <see cref="AntagonistAI"/> (Patrol → Suspicious → Search → Chase → Attack), com
    /// <see cref="PatrolBehavior"/>, <see cref="AIVision"/>, <see cref="AIHearing"/> e o
    /// NavMeshAgent.
    ///
    /// POR QUE COPIAR O DO APARTAMENTO em vez de montar do zero: lá o killer é o
    /// Antagonist.fbx com a IA acrescentada NA CENA (componentes adicionados à instância,
    /// não ao prefab), e todos os valores — velocidades, tempos, raio de ataque, ângulo e
    /// raio de visão, máscaras, olho, agente — foram afinados jogando. Clonar o objeto
    /// traz exatamente esse ajuste, sem transcrever número nenhum. O Apartment é aberto
    /// só para leitura e fechado sem salvar.
    ///
    /// O QUE MUDA NA CÓPIA: nasce ATIVO (no Ato 4 ele começa desligado e o diretor o
    /// liga), no primeiro ponto da EnemyRoute, patrulhando os pontos dessa rota, e com
    /// tag/layer Antagonist — é pela tag que o <c>PlayerHiding</c> o encontra para a regra
    /// do esconderijo comprometido. Player, visão e audição se resolvem sozinhos por tag
    /// "Player", então não há referência de cena para religar.
    ///
    /// É uma CÓPIA, não um vínculo: um ajuste feito depois no killer do Apartment não
    /// chega aqui. Rodar de novo o comando recopia (perguntando antes).
    /// </summary>
    public static class EscapeKidnapperSetup
    {
        private const string EscapePath = "Assets/Scenes/Escape.unity";
        private const string SourcePath = "Assets/Scenes/Apartment.unity";
        private const string KidnapperName = "Kidnapper";
        private const string RoutePath = "Escape_Greybox/Markers/EnemyRoute";
        private const string AntagonistTag = "Antagonist";
        private const int AntagonistLayer = 9;

        [MenuItem("Tools/The Delivery/Escape - Sequestrador (patrol, search, chase do Ato 4)")]
        private static void Run()
        {
            Scene escape = EnsureEscapeOpen();
            if (!escape.IsValid())
                return;

            List<Transform> route = FindRoute(escape);
            if (route.Count == 0)
            {
                Fail("A EnemyRoute não existe na Escape.\n\nRode antes \"Escape - Montar greybox do porão\".");
                return;
            }

            AntagonistAI existing = Find<AntagonistAI>(escape);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Sequestrador",
                        $"\"{existing.name}\" já está na Escape.\n\nRecopiar do Apartment substitui esse objeto " +
                        "(e perde ajustes feitos nele aqui).",
                        "Recopiar", "Só religar a rota"))
                {
                    LinkRoute(existing.GetComponent<PatrolBehavior>(), route);
                    Save(escape, $"Rota religada em \"{existing.name}\" ({route.Count} pontos).", existing);
                    return;
                }

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject kidnapper = CloneFromApartment(escape);
            if (kidnapper == null)
                return;

            Place(kidnapper, route[0]);
            LinkRoute(kidnapper.GetComponent<PatrolBehavior>(), route);

            Save(escape,
                 $"\"{KidnapperName}\" copiado do killer do Apartment e patrulhando a EnemyRoute ({route.Count} pontos).",
                 kidnapper);
        }

        /// <summary>
        /// Chamado pela reconstrução da greybox: ela apaga e recria os marcadores, e a
        /// patrulha ficaria apontando para transforms destruídos. No-op sem sequestrador.
        /// </summary>
        public static bool RelinkIfPresent(Scene escape)
        {
            AntagonistAI kidnapper = Find<AntagonistAI>(escape);
            if (kidnapper == null)
                return false;

            List<Transform> route = FindRoute(escape);
            if (route.Count == 0)
                return false;

            LinkRoute(kidnapper.GetComponent<PatrolBehavior>(), route);
            return true;
        }

        private static GameObject CloneFromApartment(Scene escape)
        {
            Scene source = EditorSceneManager.GetSceneByPath(SourcePath);
            bool openedHere = !(source.IsValid() && source.isLoaded);
            if (openedHere)
                source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Additive);

            try
            {
                AntagonistAI original = Find<AntagonistAI>(source);
                if (original == null)
                {
                    Fail("Nenhum AntagonistAI encontrado no Apartment.");
                    return null;
                }

                // Instantiate de um objeto de cena gera uma cópia solta (sem vínculo de
                // prefab), com as referências INTERNAS remapeadas para a cópia (agent,
                // patrol, vision, hearing, olho). O que aponta para fora — os pontos de
                // patrulha do apartamento — é trocado logo em seguida.
                GameObject clone = Object.Instantiate(original.gameObject);
                clone.name = KidnapperName;
                SceneManager.MoveGameObjectToScene(clone, escape);
                Undo.RegisterCreatedObjectUndo(clone, "Escape Sequestrador");

                Debug.Log($"[EscapeKidnapperSetup] Copiado de \"{original.name}\" (Apartment).", clone);
                return clone;
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(source, removeScene: true);
            }
        }

        private static void Place(GameObject kidnapper, Transform start)
        {
            kidnapper.SetActive(true);
            kidnapper.tag = AntagonistTag;
            kidnapper.layer = AntagonistLayer;

            // Nasce no início da rota, olhando para o segundo trecho dela (leste).
            kidnapper.transform.SetPositionAndRotation(start.position, Quaternion.Euler(0f, 90f, 0f));

            // O agente sobe no NavMesh sozinho no Play se a posição estiver sobre ele; o
            // aviso aqui é para o caso de o bake não ter rodado.
            if (!NavMesh.SamplePosition(start.position, out _, 1f, NavMesh.AllAreas))
                Debug.LogWarning("[EscapeKidnapperSetup] Sem NavMesh no início da EnemyRoute — a caçada não anda. " +
                                 "Faça o Bake no NavMeshSurface do Escape_Greybox.", kidnapper);
        }

        private static void LinkRoute(PatrolBehavior patrol, List<Transform> route)
        {
            if (patrol == null)
                return;

            var so = new SerializedObject(patrol);
            SerializedProperty points = so.FindProperty("patrolPoints");
            points.arraySize = route.Count;
            for (int i = 0; i < route.Count; i++)
                points.GetArrayElementAtIndex(i).objectReferenceValue = route[i];
            so.ApplyModifiedProperties();
        }

        private static List<Transform> FindRoute(Scene scene)
        {
            var route = new List<Transform>();
            string rootName = RoutePath.Substring(0, RoutePath.IndexOf('/'));
            string rest = RoutePath.Substring(rootName.Length + 1);

            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.name != rootName)
                    continue;

                Transform parent = r.transform.Find(rest);
                if (parent == null)
                    break;

                foreach (Transform child in parent)
                    route.Add(child);
                break;
            }

            return route;
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                T found = r.GetComponentInChildren<T>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void Save(Scene scene, string message, Object context)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = context;

            Debug.Log($"[EscapeKidnapperSetup] {message} Cena Escape salva.", context);
            EditorUtility.DisplayDialog("Sequestrador",
                $"{message}\n\nCena Escape salva.\n\n" +
                "Ajustes (velocidades, tempos, visão, audição) ficam no Inspector do objeto, nos mesmos " +
                "componentes do killer do Ato 4.",
                "Ok");
        }

        private static Scene EnsureEscapeOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(EscapePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(EscapePath, OpenSceneMode.Single);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[EscapeKidnapperSetup] {message}");
            EditorUtility.DisplayDialog("Sequestrador", message, "Ok");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Padroniza a hierarquia da CENA ATIVA: cria os grupos-container vazios (com transform
    /// IDENTIDADE — pos 0, rot 0, escala 1, para os filhos não se deslocarem) e reparenta os
    /// objetos da RAIZ por heurística (componentes/nome). Serve a todas as cenas dos atos
    /// (Cafeteria/Recepcao/Apartamento), dando a MESMA estrutura. Tudo via Undo (Ctrl+Z
    /// desfaz). Só processa objetos da raiz — o que já está dentro de um grupo é preservado.
    ///
    /// Grupos: _Managers (directors, NavMeshSurface, sistemas), _UI (Canvas, EventSystem),
    /// _Actors (Player, personagens), _Lighting (Lights), _Environment (geometria),
    /// _Markers (empties/zonas — spawn/sit/exit points). Containers existentes são reusados
    /// (case-INSENSITIVE: `_environment`→`_Environment`) e o antigo `_points`→`_Markers`.
    /// </summary>
    public static class SceneHierarchyOrganizer
    {
        // Ordem em que os grupos aparecem no topo da Hierarchy.
        private static readonly string[] GroupOrder =
        {
            "_Managers", "_UI", "_Actors", "_Lighting", "_Environment", "_Markers",
        };

        // Containers antigos tratados como um grupo novo (renomeados ao organizar).
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "_points", "_Markers" },
        };

        // Tipos de componente que classificam o objeto (por NOME de tipo, p/ pegar os 4 directors
        // e não acoplar o assembly do Editor a cada classe).
        private static readonly HashSet<string> ManagerTypes = new HashSet<string>
        {
            "NavMeshSurface", "DialogueSystem", "ThoughtSystem",
        };

        // Componentes que EXIGEM ficar na RAIZ (DontDestroyOnLoad): o organizador nunca os
        // reparenta, senão a persistência entre cenas quebra (ex.: GameManager na Boot).
        private static readonly HashSet<string> RootOnlyTypes = new HashSet<string>
        {
            "GameManager",
        };
        private static readonly HashSet<string> ActorTypes = new HashSet<string>
        {
            "PlayerController", "Marina", "AntagonistAI", "NavMeshAgent",
        };

        // Personagens scriptados SEM componente próprio reconhecível (props dirigidos por
        // Director). Match por nome EXATO (trimado, case-insensitive).
        private static readonly HashSet<string> ActorNameHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Zelador", "Janitor", "Marina", "Entregador", "Antagonist",
            "Vizinho", "Neighbor", "ShadowFigure",
        };

        [MenuItem("Tools/The Delivery/Organize Scene Hierarchy")]
        private static void OrganizeActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog("Organizar Hierarquia", "Nenhuma cena ativa válida.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Organizar Hierarquia",
                    $"Cria os grupos padrão e reparenta os objetos da raiz da cena \"{scene.name}\".\n\n" +
                    "É reversível com Ctrl+Z. Continuar?",
                    "Organizar", "Cancelar"))
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Organize Scene Hierarchy");

            Dictionary<string, Transform> groups = EnsureGroups(scene);

            // Snapshot DEPOIS de garantir/renomear os containers (a lista de roots muda a cada reparent).
            GameObject[] roots = scene.GetRootGameObjects();
            var movedByGroup = new Dictionary<string, List<string>>();

            foreach (GameObject go in roots)
            {
                // Pula grupos-container: os canônicos E QUALQUER empty com prefixo "_"
                // (convenção do projeto — ex.: _Act3_assets/_Act4_assets). Sem isso, o
                // organizador "engoliria" esses containers (e tudo dentro) num grupo errado.
                if (CanonicalContainerName(go.name) != null || go.name.TrimStart().StartsWith("_"))
                    continue;

                // Singletons persistentes (DontDestroyOnLoad) DEVEM ficar na raiz — não mover.
                if (go.GetComponents<Component>().Any(c => c != null && RootOnlyTypes.Contains(c.GetType().Name)))
                    continue;

                string target = Classify(go);
                if (groups.TryGetValue(target, out Transform parent) && go.transform.parent != parent)
                {
                    Undo.SetTransformParent(go.transform, parent, $"Group {go.name}");
                    if (!movedByGroup.TryGetValue(target, out List<string> list))
                        movedByGroup[target] = list = new List<string>();
                    list.Add(go.name.Trim());
                }
            }

            for (int i = 0; i < GroupOrder.Length; i++)
                if (groups.TryGetValue(GroupOrder[i], out Transform g) && g != null)
                    g.SetSiblingIndex(i);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            LogSummary(scene, movedByGroup);
        }

        [MenuItem("Tools/The Delivery/Create Group Containers Only")]
        private static void CreateGroupsOnly()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Group Containers");
            Dictionary<string, Transform> groups = EnsureGroups(scene);
            for (int i = 0; i < GroupOrder.Length; i++)
                if (groups.TryGetValue(GroupOrder[i], out Transform g) && g != null)
                    g.SetSiblingIndex(i);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[SceneHierarchyOrganizer] Grupos garantidos na cena \"{scene.name}\".");
        }

        /// <summary>Garante todos os grupos: reusa containers existentes (case-insensitive),
        /// normaliza o nome p/ o canônico (ex.: `_environment`→`_Environment`, `_points`→`_Markers`)
        /// e cria os faltantes na identidade.</summary>
        private static Dictionary<string, Transform> EnsureGroups(Scene scene)
        {
            var found = new Dictionary<string, GameObject>(); // canônico -> objeto
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                string canon = CanonicalContainerName(go.name);
                if (canon == null || found.ContainsKey(canon))
                    continue;
                if (go.name != canon)
                {
                    Undo.RecordObject(go, "Rename group");
                    go.name = canon;
                }
                found[canon] = go;
            }

            var groups = new Dictionary<string, Transform>();
            foreach (string name in GroupOrder)
            {
                if (found.TryGetValue(name, out GameObject existing))
                {
                    groups[name] = existing.transform;
                    continue;
                }

                var g = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(g, "Create group");
                SceneManager.MoveGameObjectToScene(g, scene);
                g.transform.SetParent(null, false);
                g.transform.localPosition = Vector3.zero;
                g.transform.localRotation = Quaternion.identity;
                g.transform.localScale = Vector3.one;
                groups[name] = g.transform;
            }

            return groups;
        }

        /// <summary>Nome canônico do grupo se o objeto for um container (case-insensitive,
        /// resolve aliases); senão null.</summary>
        private static string CanonicalContainerName(string objectName)
        {
            foreach (string g in GroupOrder)
                if (string.Equals(objectName, g, StringComparison.OrdinalIgnoreCase))
                    return g;
            if (Aliases.TryGetValue(objectName, out string canon))
                return canon;
            return null;
        }

        /// <summary>Decide o grupo do objeto pela 1ª regra que casar.</summary>
        private static string Classify(GameObject go)
        {
            var typeNames = new HashSet<string>();
            foreach (Component c in go.GetComponents<Component>())
                if (c != null) // ignora scripts faltando
                    typeNames.Add(c.GetType().Name);

            // UI
            if (typeNames.Contains("Canvas") || typeNames.Contains("EventSystem"))
                return "_UI";

            // Managers: qualquer *Director + sistemas/managers conhecidos.
            if (typeNames.Any(n => n.EndsWith("Director")) || typeNames.Overlaps(ManagerTypes))
                return "_Managers";

            // Actors: player/personagens por componente OU por nome (props scriptados).
            if (typeNames.Overlaps(ActorTypes) || ActorNameHints.Contains(go.name.Trim()))
                return "_Actors";

            // Volume de pós-processo (ex.: Global Volume) mora com a iluminação/atmosfera.
            if (typeNames.Contains("Volume"))
                return "_Lighting";

            // Lighting: luz no próprio objeto OU em algum filho (ex.: empty pai de point lights).
            if (go.GetComponentInChildren<Light>(true) != null)
                return "_Lighting";

            // Environment: geometria no próprio objeto OU em filhos.
            if (go.GetComponentInChildren<Renderer>(true) != null)
                return "_Environment";

            // Resto (sem geometria/luz/componente reconhecido): empties/zonas/markers.
            return "_Markers";
        }

        private static void LogSummary(Scene scene, Dictionary<string, List<string>> movedByGroup)
        {
            int total = movedByGroup.Values.Sum(l => l.Count);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[SceneHierarchyOrganizer] Cena \"{scene.name}\": {total} objeto(s) reparentado(s).");
            foreach (string g in GroupOrder)
                if (movedByGroup.TryGetValue(g, out List<string> list) && list.Count > 0)
                    sb.AppendLine($"  {g} ({list.Count}): {string.Join(", ", list)}");
            Debug.Log(sb.ToString().TrimEnd());
        }
    }
}

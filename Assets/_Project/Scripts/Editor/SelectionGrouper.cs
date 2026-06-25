using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Agrupa a SELEÇÃO atual sob um novo empty-container (transform IDENTIDADE), preservando
    /// a posição mundial dos objetos. Genérico: serve para criar qualquer nível de contexto
    /// em qualquer cena (ex.: selecionar as paredes/chão/teto do hall → agrupar em "Hall").
    /// O container nasce com nome "Group" e já fica selecionado para você renomear (F2).
    /// Reversível (Ctrl+Z). Atalho: Ctrl+G.
    /// </summary>
    public static class SelectionGrouper
    {
        [MenuItem("Tools/The Delivery/Group Selection Under New Container %g")]
        private static void GroupSelection()
        {
            // Selection.transforms já filtra filhos cujo pai também está selecionado.
            Transform[] selection = Selection.transforms;
            if (selection.Length == 0)
            {
                EditorUtility.DisplayDialog("Agrupar Seleção", "Selecione um ou mais objetos na Hierarchy.", "OK");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Group Selection Under New Container");

            // Mantém a ordem atual e ancora no pai/posição do primeiro (na ordem da hierarquia).
            Transform[] ordered = selection.OrderBy(t => t.GetSiblingIndex()).ToArray();
            Transform anchor = ordered[0];
            Transform parent = anchor.parent;
            int insertAt = anchor.GetSiblingIndex();

            var group = new GameObject("Group");
            Undo.RegisterCreatedObjectUndo(group, "Create group");
            if (parent != null)
                group.transform.SetParent(parent, false);
            else
                SceneManager.MoveGameObjectToScene(group, anchor.gameObject.scene);
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;
            group.transform.SetSiblingIndex(insertAt);

            foreach (Transform t in ordered)
                Undo.SetTransformParent(t, group.transform, "Group child");

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(group.scene);

            Selection.activeGameObject = group;
            EditorGUIUtility.PingObject(group);
            Debug.Log($"[SelectionGrouper] {ordered.Length} objeto(s) agrupado(s) em \"{group.name}\" (renomeie com F2).");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Renomeia objetos da CENA ATIVA em massa a partir de um mapa colado pelo usuário —
    /// uma linha por par <c>NomeAtual = NomeNovo</c> (linhas vazias ou começando com // são
    /// ignoradas). Genérico: os dados (o mapa) vêm da janela, não do código, então serve a
    /// qualquer cena (ex.: padronizar nomes PT→EN). Casa por nome TRIMADO (pega nomes com
    /// espaço sobrando). Reversível (Ctrl+Z). Renomeia TODAS as ocorrências de um nome.
    /// </summary>
    public sealed class BatchRenameWindow : EditorWindow
    {
        private const string Placeholder =
            "// Um par por linha:  NomeAtual = NomeNovo\n" +
            "// Linhas vazias e // são ignoradas.\n\n" +
            "Parede_Oeste = Wall_West\n";

        private string input = Placeholder;
        private Vector2 scroll;

        [MenuItem("Tools/The Delivery/Batch Rename From Map")]
        private static void Open()
        {
            var window = GetWindow<BatchRenameWindow>("Batch Rename");
            window.minSize = new Vector2(360, 280);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Renomear em massa (cena ativa)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Cole pares 'NomeAtual = NomeNovo', um por linha. Aplica na cena ativa.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            input = EditorGUILayout.TextArea(input, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(input)))
                if (GUILayout.Button("Aplicar na cena ativa", GUILayout.Height(28)))
                    Apply();
        }

        private void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog("Batch Rename", "Nenhuma cena ativa válida.", "OK");
                return;
            }

            Dictionary<string, string> map = ParseMap(input);
            if (map.Count == 0)
            {
                EditorUtility.DisplayDialog("Batch Rename", "Nenhum par válido (use 'Atual = Novo').", "OK");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Batch Rename From Map");

            int renamed = 0;
            var pending = new HashSet<string>(map.Keys);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    string trimmed = t.name.Trim();
                    if (!map.TryGetValue(trimmed, out string newName))
                        continue;

                    pending.Remove(trimmed);
                    if (t.name != newName)
                    {
                        Undo.RecordObject(t.gameObject, "Rename");
                        t.gameObject.name = newName;
                        renamed++;
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[BatchRename] Cena \"{scene.name}\": {renamed} objeto(s) renomeado(s) de {map.Count} regra(s).");
            if (pending.Count > 0)
                sb.AppendLine($"  Sem correspondência na cena: {string.Join(", ", pending.OrderBy(s => s))}");
            Debug.Log(sb.ToString().TrimEnd());
        }

        /// <summary>Parseia o texto em pares Atual→Novo (separador '='), ignorando vazias e //.</summary>
        private static Dictionary<string, string> ParseMap(string text)
        {
            var map = new Dictionary<string, string>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1)
                    continue;

                string from = line.Substring(0, eq).Trim();
                string to = line.Substring(eq + 1).Trim();
                if (from.Length > 0 && to.Length > 0)
                    map[from] = to;
            }
            return map;
        }
    }
}

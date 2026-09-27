using System.Collections.Generic;
using System.Text;
using TheDelivery.Core;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// REGISTRA AS CENAS DO FLUXO no Build Settings, na ordem cronológica do jogo.
    ///
    /// POR QUE ISTO EXISTE: o <see cref="GameManager"/> carrega cena POR NOME
    /// (<c>SceneManager.LoadScene(GameScene.ToString())</c>), e o Unity só resolve nome de
    /// cena que esteja na lista do Build Settings. Uma cena de fora da lista não dá erro de
    /// compilação nem aviso no Inspector: ela falha em RUNTIME, no quadro da transição, com
    /// a tela já no preto do fade — o sintoma é o jogo travar no preto depois do Play, que
    /// não se parece em nada com a causa.
    ///
    /// A ORDEM DA LISTA não é o que encadeia os atos (quem encadeia é cada diretor, ao
    /// chamar TransitionToScene). Ela importa por uma coisa só: o índice 0 é a cena que
    /// SOBE no Play do build, e essa tem que ser a Boot. O resto está em ordem cronológica
    /// porque a lista é lida por gente, e uma lista fora de ordem convida a conclusões
    /// erradas sobre o fluxo.
    ///
    /// É idempotente: rodar de novo com tudo no lugar não escreve nada.
    /// </summary>
    public static class BootSceneListSetup
    {
        /// <summary>
        /// A CRONOLOGIA, em cenas: Boot sobe o GameManager, que abre o cold open; daí cada
        /// diretor entrega ao próximo (PesadeloDirector -> Act1Director -> PercursoDirector
        /// -> Act2Director -> Act3Director, e o Ato 4 acontece na mesma cena do 3; o
        /// Act4Director entrega à Escape depois do sequestro).
        /// </summary>
        private static readonly GameScene[] Flow =
        {
            GameScene.Boot,        // índice 0 — a cena que sobe no Play
            GameScene.Pesadelo,    // ActPesadelo (cold open)
            GameScene.Cafeteria,   // Act1
            GameScene.Estrada,     // ActPercurso
            GameScene.Recepcao,    // Act2
            GameScene.Apartamento, // Act3 e Act4
            GameScene.Escape       // ActEscape
        };

        [MenuItem("Tools/The Delivery/Boot - Registrar as cenas do fluxo (Build Settings)")]
        private static void Run()
        {
            var scenes = new List<EditorBuildSettingsScene>(Flow.Length);
            var faltando = new List<string>();
            var report = new StringBuilder();

            foreach (GameScene scene in Flow)
            {
                string path = FindScenePath(scene);
                if (path == null)
                {
                    faltando.Add($"{scene}.unity");
                    continue;
                }

                scenes.Add(new EditorBuildSettingsScene(path, true));
                report.AppendLine($"{scenes.Count - 1}. {path}");
            }

            // Uma cena do enum sem arquivo no disco é erro de nome, e não algo a contornar:
            // o GameManager carrega pelo ToString() do enum, então o .unity TEM que se
            // chamar igual. Escrever a lista sem ela só empurraria a falha para o runtime.
            if (faltando.Count > 0)
            {
                string aviso = "Não achei estes arquivos de cena:\n\n" + string.Join("\n", faltando) +
                               "\n\nO nome do arquivo tem que bater com o valor em GameScene — é por ele que o " +
                               "GameManager carrega. Nada foi alterado.";
                Debug.LogError("[BootSceneListSetup] " + aviso.Replace("\n\n", " ").Replace("\n", ", "));
                EditorUtility.DisplayDialog("Cenas do fluxo", aviso, "Ok");
                return;
            }

            if (JaEstaCerto(scenes))
            {
                Debug.Log("[BootSceneListSetup] A lista do Build Settings já está na ordem do fluxo, tudo habilitado. " +
                          "Nada a fazer.");
                EditorUtility.DisplayDialog("Cenas do fluxo",
                                            "A lista do Build Settings já está certa:\n\n" + report + "\nNada a fazer.",
                                            "Ok");
                return;
            }

            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[BootSceneListSetup] Build Settings atualizado com as cenas do fluxo:\n" + report);
            EditorUtility.DisplayDialog("Cenas do fluxo",
                                        "Build Settings atualizado. A lista agora é:\n\n" + report +
                                        "\nA Boot está no índice 0, que é a cena que sobe no Play do build.",
                                        "Ok");
        }

        /// <summary>
        /// Acha o .unity da cena pelo NOME, onde ele estiver na pasta Assets. Procura em vez
        /// de assumir Assets/Scenes/ porque mover uma cena de pasta é reorganização comum e
        /// não deveria quebrar esta ferramenta — o que o GameManager exige é o nome, não o
        /// caminho.
        /// </summary>
        private static string FindScenePath(GameScene scene)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:Scene {scene}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == scene.ToString())
                    return path;
            }

            return null;
        }

        /// <summary>
        /// A lista atual já é EXATAMENTE esta — mesmos caminhos, mesma ordem, todas
        /// habilitadas? Comparar antes de escrever é o que deixa o comando repetível sem
        /// sujar o EditorBuildSettings.asset (e o diff do git) à toa.
        /// </summary>
        private static bool JaEstaCerto(List<EditorBuildSettingsScene> desejada)
        {
            EditorBuildSettingsScene[] atual = EditorBuildSettings.scenes;
            if (atual == null || atual.Length != desejada.Count)
                return false;

            for (int i = 0; i < atual.Length; i++)
            {
                if (!atual[i].enabled || atual[i].path != desejada[i].path)
                    return false;
            }

            return true;
        }
    }
}

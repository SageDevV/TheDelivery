using System.Collections.Generic;
using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Preenche na cena o <c>fallHideObjects</c> do <see cref="PesadeloDirector"/>: os objetos
    /// que SOMEM no meio da queda da pegada, atrás da piscada da Clear.
    ///
    /// POR QUE UM COMANDO em vez de arrastar quatro coisas para uma lista: os quatro estão
    /// espalhados pela hierarquia da cena, um deles (o CreatureGrab) vive DESATIVADO — ou
    /// seja, some da busca do Inspector — e a lista é fácil de preencher pela metade sem que
    /// nada reclame. Uma lista com três dos quatro não dá erro: dá uma queda em que o
    /// corredor some e a névoa fica, e isso só se descobre olhando.
    ///
    /// O TEMPO NÃO É ESCRITO AQUI. O <c>fallBlinkDelay</c> é o slider do Inspector, e é a única
    /// decisão do conjunto que é de OLHO: em que ponto da queda ela pisca — e, portanto, em que
    /// ponto o sonho se desfaz. Um valor gravado por comando pareceria autorado sem nunca ter
    /// sido visto.
    ///
    /// Idempotente: rodar de novo com a lista já certa não muda nada, e diz isso.
    /// </summary>
    public static class PesadeloFallHideSetup
    {
        /// <summary>
        /// O cenário do sonho, na ordem em que ele deixa de existir. CreatureGrab por último
        /// porque é o único que não é cenário: é quem a soltou, e some junto com o resto.
        /// </summary>
        private static readonly string[] Targets =
        {
            "DreamFog",
            "DreamParticle",
            "Corredor",
            "CreatureGrab",
        };

        [MenuItem("Tools/The Delivery/Pesadelo - Sumiço na Queda (preencher a lista)")]
        private static void Run()
        {
            Scene scene = PesadeloGrabSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = PesadeloGrabSetup.FindDirector(scene);
            if (director == null)
            {
                EditorUtility.DisplayDialog(
                    "Sumiço na Queda",
                    "PesadeloDirector não encontrado na cena Pesadelo.",
                    "Ok");
                return;
            }

            var so = new SerializedObject(director);
            SerializedProperty list = so.FindProperty("fallHideObjects");
            if (list == null)
            {
                Debug.LogWarning("[PesadeloFallHideSetup] O PesadeloDirector desta cena não tem o campo " +
                                 "\"fallHideObjects\" — recompilou depois de atualizar o script?", director);
                return;
            }

            var found = new List<GameObject>();
            var missing = new List<string>();

            foreach (string name in Targets)
            {
                GameObject target = Resolve(scene, so, name);

                if (target != null)
                    found.Add(target);
                else
                    missing.Add(name);
            }

            if (Matches(list, found))
            {
                Debug.Log($"[PesadeloFallHideSetup] A lista já está certa ({Describe(found)}) — nada a fazer.\n" +
                          Advice(so, missing), director);
                return;
            }

            list.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = director.gameObject;
            EditorGUIUtility.PingObject(director);

            Debug.Log($"[PesadeloFallHideSetup] Cena salva. Somem na queda: {Describe(found)}.\n" +
                      Advice(so, missing), director);
        }

        /// <summary>
        /// O objeto de um nome. O CreatureGrab sai do CAMPO do diretor quando ele está
        /// preenchido, e não da busca por nome: é a mesma instância que o beat usa, e uma
        /// segunda cópia com o mesmo nome largada na cena faria a lista apagar a errada — a
        /// que ninguém está vendo.
        /// </summary>
        private static GameObject Resolve(Scene scene, SerializedObject so, string name)
        {
            if (name == "CreatureGrab" &&
                so.FindProperty("creatureGrabObject")?.objectReferenceValue is GameObject assigned)
            {
                return assigned;
            }

            Transform found = PesadeloGrabSetup.FindByName(scene, name);
            return found != null ? found.gameObject : null;
        }

        /// <summary>true se a lista gravada já é exatamente esta, na mesma ordem.</summary>
        private static bool Matches(SerializedProperty list, List<GameObject> found)
        {
            if (list.arraySize != found.Count)
                return false;

            for (int i = 0; i < found.Count; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != found[i])
                    return false;

            return true;
        }

        private static string Describe(List<GameObject> found)
        {
            if (found.Count == 0)
                return "nada";

            var text = new StringBuilder();
            for (int i = 0; i < found.Count; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(found[i].name);
            }

            return text.ToString();
        }

        /// <summary>
        /// O que ficou por fazer. São duas coisas, e nenhuma das duas o comando pode resolver
        /// sozinha: o que ele não achou na cena, e o tempo — que é decisão de olho.
        /// </summary>
        private static string Advice(SerializedObject so, List<string> missing)
        {
            var text = new StringBuilder();

            if (missing.Count > 0)
            {
                text.Append("NÃO ACHEI na cena: ");
                text.Append(string.Join(", ", missing));
                text.Append(". Se o objeto existe com outro nome, arraste-o para o Fall Hide Objects à mão; se não " +
                            "existe mais, ignore.\n");
            }

            float delay = so.FindProperty("fallBlinkDelay")?.floatValue ?? 0f;
            float fall = so.FindProperty("grabFallDuration")?.floatValue ?? 0f;

            float close = so.FindProperty("fallBlinkCloseDuration")?.floatValue ?? 0f;
            float hold = so.FindProperty("fallBlinkHoldDuration")?.floatValue ?? 0f;
            float open = so.FindProperty("fallBlinkOpenDuration")?.floatValue ?? 0f;
            float blink = close + hold + open;

            text.Append($"O TEMPO é o Fall Blink Delay, no Inspector: ela pisca {delay:0.##} s depois da soltura, e o " +
                        $"sumiço acontece com o olho fechado — {delay + close:0.##} s, contando o fechamento. A " +
                        $"piscada inteira dura {blink:0.##} s");

            if (fall > 0f)
            {
                text.Append(delay + blink >= fall
                    ? $", e não cabe no que sobra de uma queda de {fall:0.##} s — ela vai ser puxada para trás sozinha " +
                      "para terminar antes do pouso. Baixe o slider para escolher o instante em vez de aceitar o " +
                      "último que cabe."
                    : $", numa queda de {fall:0.##} s.");
            }
            else
            {
                text.Append(". Com o Grab Fall Duration em 0 a queda é livre e a duração sai da altura — Tools ▸ The " +
                            "Delivery ▸ Pesadelo - Ajustar a Pegada mostra quantos segundos ela está durando.");
            }

            return text.ToString();
        }
    }
}

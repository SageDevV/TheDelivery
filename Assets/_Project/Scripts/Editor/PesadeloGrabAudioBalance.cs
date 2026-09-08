using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Põe o SOM DA PEGADA no equilíbrio padrão: o baque do contato por cima, o loop do
    /// aperto por baixo.
    ///
    /// POR QUE ISTO EXISTE COMO COMANDO. Baixar o valor no código não baixa o som de uma cena
    /// que já existe: o Inspector grava o número dentro do arquivo da cena, e o padrão escrito
    /// no campo só vale para um director criado do zero. A cena Pesadelo foi montada com o
    /// aperto em 0,8 — alto o bastante para cobrir o baque, que dura um instante e é o único
    /// som do beat com hora marcada —, e esse 0,8 continua lá até alguém mexer nele.
    ///
    /// Uso: <c>Tools ▸ The Delivery ▸ Pesadelo - Equilibrar o Som da Pegada</c>. Depois disso
    /// os dois campos são de ouvido, no Inspector do PesadeloDirector: Grab Sound Volume para
    /// o baque, Grab Loop Volume para o aperto. Rodar de novo devolve o padrão.
    /// </summary>
    public static class PesadeloGrabAudioBalance
    {
        /// <summary>
        /// O aperto é FUNDO, e fundo mora abaixo do que acontece por cima dele. 0,35 deixa o
        /// rosnado presente durante todo o trecho sem disputar o quadro do contato.
        /// </summary>
        private const float LoopVolume = 0.35f;

        /// <summary>O baque fica inteiro: é ele que diz que a mão fechou.</summary>
        private const float ImpactVolume = 1f;

        [MenuItem("Tools/The Delivery/Pesadelo - Equilibrar o Som da Pegada")]
        private static void Run()
        {
            Scene scene = PesadeloGrabSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = PesadeloGrabSetup.FindDirector(scene);
            if (director == null)
            {
                const string missing = "PesadeloDirector não encontrado na cena Pesadelo.";
                Debug.LogError("[PesadeloGrabAudioBalance] " + missing);
                EditorUtility.DisplayDialog("Som da Pegada", missing, "OK");
                return;
            }

            // Undo pelo SerializedObject: ApplyModifiedProperties já registra o passo, e um
            // Undo.RecordObject por fora só duplicaria a entrada no histórico.
            var so = new SerializedObject(director);

            string loopReport = SetFloat(so, "grabLoopVolume", LoopVolume);
            string impactReport = SetFloat(so, "grabSoundVolume", ImpactVolume);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = director.gameObject;
            EditorGUIUtility.PingObject(director);

            Debug.Log(
                "[PesadeloGrabAudioBalance] Som da pegada equilibrado. " +
                impactReport + "; " + loopReport + ". " +
                "Daqui em diante é de ouvido, nos dois campos do Inspector: se o baque sumir de novo, " +
                "desça o Grab Loop Volume em vez de subir o Grab Sound Volume — o que se escuta é a " +
                "relação entre os dois, e subir os dois juntos não muda nada.",
                director);
        }

        private static string SetFloat(SerializedObject directorSo, string propertyName, float value)
        {
            SerializedProperty property = directorSo.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning("[PesadeloGrabAudioBalance] O PesadeloDirector desta cena não tem o campo " +
                                 propertyName + " — recompilou depois de atualizar o script?");
                return propertyName + " NÃO encontrado";
            }

            float before = property.floatValue;
            property.floatValue = value;

            return Mathf.Approximately(before, value)
                ? propertyName + " já estava em " + value.ToString("0.##")
                : propertyName + ": " + before.ToString("0.##") + " para " + value.ToString("0.##");
        }
    }
}

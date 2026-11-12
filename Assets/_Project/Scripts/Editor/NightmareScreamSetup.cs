using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Liga o GRITO DA CLEAR ao campo <c>screamSound</c> do <c>NightmareDirector</c> — o som
    /// que toca no beat da PEGADA, no quadro em que ela termina o giro e encara a criatura
    /// que veio pegá-la, um instante antes de a animação do bote soltar.
    ///
    /// POR QUE ISTO EXISTE COMO COMANDO, e não como um valor no código: o campo é uma
    /// REFERÊNCIA, e referência mora dentro do arquivo da cena. Um padrão escrito no script
    /// só valeria para um director criado do zero; a cena Nightmare já existe, e nela o campo
    /// nasce vazio.
    ///
    /// Uso: <c>Tools ▸ The Delivery ▸ Nightmare - Grito ao Ver a Criatura</c>. Equivale a
    /// arrastar o clipe para o campo Scream Sound no Inspector — existe para não depender de
    /// ninguém lembrar QUAL arquivo é, nem em que beat ele entra.
    ///
    /// NÃO SOBRESCREVE um clipe já atribuído: se o campo tiver outro som (uma regravação, um
    /// take diferente), o comando avisa e não toca em nada. Para trocar, limpe o campo e rode
    /// de novo — ou arraste o novo por cima, que é a mesma coisa.
    ///
    /// O VOLUME não é mexido aqui: ele é de ouvido, no Scream Volume do Inspector.
    /// </summary>
    public static class NightmareScreamSetup
    {
        private const string ClipPath = "Assets/_Project/SoundEffects/WomanScream.mp3";

        [MenuItem("Tools/The Delivery/Nightmare - Grito ao Ver a Criatura")]
        private static void Run()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (clip == null)
            {
                Fail($"Não achei o clipe do grito em:\n{ClipPath}\n\nSe o arquivo tem outro nome, arraste-o à mão " +
                     "para o campo Scream Sound do NightmareDirector.");
                return;
            }

            Scene scene = NightmareGrabSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            NightmareDirector director = NightmareGrabSetup.FindDirector(scene);
            if (director == null)
            {
                Fail("NightmareDirector não encontrado na cena Nightmare.");
                return;
            }

            // Undo pelo SerializedObject: ApplyModifiedProperties já registra o passo.
            var so = new SerializedObject(director);
            SerializedProperty property = so.FindProperty("screamSound");
            if (property == null)
            {
                Fail("O NightmareDirector desta cena não tem o campo screamSound — recompilou depois de atualizar o " +
                     "script?");
                return;
            }

            var current = property.objectReferenceValue as AudioClip;
            if (current == clip)
            {
                Debug.Log($"[NightmareScreamSetup] O grito já estava ligado ({clip.name}); nada a fazer.", director);
                return;
            }

            if (current != null)
            {
                Debug.LogWarning($"[NightmareScreamSetup] O campo Scream Sound já tem outro clipe (\"{current.name}\") " +
                                 "e NÃO foi trocado. Limpe o campo e rode de novo, ou arraste o novo por cima.",
                                 director);
                return;
            }

            property.objectReferenceValue = clip;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = director.gameObject;
            EditorGUIUtility.PingObject(director);

            Debug.Log($"[NightmareScreamSetup] Grito ligado: \"{clip.name}\" no campo Scream Sound. Ele toca no beat " +
                      "da pegada (Beat 5), no quadro em que a Clear termina o giro e encara a criatura — antes de a " +
                      "animação do bote soltar. O volume é de ouvido, no Scream Volume ao lado.", director);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[NightmareScreamSetup] {message}");
            EditorUtility.DisplayDialog("Grito ao Ver a Criatura", message, "OK");
        }
    }
}

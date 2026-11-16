using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// GATILHO DE AUTOMAÇÃO para o <see cref="StaminaSetup"/>: como o Editor fica sempre
    /// aberto (batchmode não roda com o projeto travado), quem quer aplicar o setup de fora
    /// cria o arquivo <c>Temp/StaminaSetup.pending</c>. No próximo reload de scripts este
    /// hook o encontra, APAGA (roda uma vez só) e executa o setup sem diálogos.
    /// Sem o arquivo, não faz nada — abrir o projeto não reaplica nada sozinho.
    /// </summary>
    [InitializeOnLoad]
    internal static class StaminaSetupAutoRun
    {
        private const string MarkerPath = "Temp/StaminaSetup.pending";

        static StaminaSetupAutoRun()
        {
            if (File.Exists(MarkerPath))
                EditorApplication.update += TryRun;
        }

        private static void TryRun()
        {
            // Espera um momento seguro: nada de mexer em cena/prefab no meio de Play,
            // compilação ou import.
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
                return;

            EditorApplication.update -= TryRun;

            if (!File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);

            Debug.Log("[StaminaSetupAutoRun] Marcador encontrado — aplicando StaminaSetup.");
            StaminaSetup.Apply(interactiveMode: false);
        }
    }
}

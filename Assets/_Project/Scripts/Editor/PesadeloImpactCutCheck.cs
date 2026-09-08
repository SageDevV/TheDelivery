using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIZ ONDE O BAQUE DO POUSO VAI SER CORTADO, sem rodar a cena.
    ///
    /// POR QUE ISTO EXISTE: o corte do impacto acontece no meio de um beat que leva um
    /// minuto de Play para chegar, e quando ele não funciona o sintoma é sempre o mesmo —
    /// "o som saiu do começo" —, que pode ser meia dúzia de coisas: o clipe está no campo
    /// errado, o Load Type não deixa ler a onda, o limiar não achou subida nenhuma. Nada
    /// disso se distingue de ouvido. Aqui a mesma conta que o beat faz roda no Editor e
    /// imprime o que encontrou.
    ///
    /// A CONTA É A MESMA, de propósito: pico do arquivo, limiar como fração dele, primeira
    /// amostra que passa, menos o pré-rolo. Se este comando disser "corte em 2,41 s", é em
    /// 2,41 s que o beat vai cortar.
    /// </summary>
    public static class PesadeloImpactCutCheck
    {
        /// <summary>O mesmo pré-rolo do <c>PesadeloDirector</c> — 20 ms antes da subida.</summary>
        private const float OnsetPreRoll = 0.02f;

        [MenuItem("Tools/The Delivery/Pesadelo - Baque: onde a onda sobe")]
        private static void Run()
        {
            Scene scene = PesadeloGrabSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = PesadeloGrabSetup.FindDirector(scene);
            if (director == null)
            {
                EditorUtility.DisplayDialog("Baque do pouso", "PesadeloDirector não encontrado na cena Pesadelo.", "Ok");
                return;
            }

            var so = new SerializedObject(director);

            var clip = so.FindProperty("fallImpactSound")?.objectReferenceValue as AudioClip;
            var cutSound = so.FindProperty("impactSound")?.objectReferenceValue as AudioClip;

            if (clip == null)
            {
                // O erro mais provável de todos, e o único que o Play não denuncia: o clipe
                // foi parar no baque do CORTE (beat 6), que toca por PlayOneShot e ignora
                // qualquer corte — o som sai do começo do arquivo e parece que o campo do
                // corte não faz nada.
                string extra = cutSound != null
                    ? $"\n\nMAS o Impact Sound (Beat 6 - Corte) está com \"{cutSound.name}\". Se era esse o som do " +
                      "pouso, ele está no campo errado: aquele toca por PlayOneShot, sempre do começo do arquivo, e " +
                      "não passa pelo corte. Mova para o Fall Impact Sound."
                    : "";

                Debug.LogWarning($"[BaqueDoPouso] O Fall Impact Sound está VAZIO — o pouso acontece sem baque.{extra}",
                                 director);
                Selection.activeGameObject = director.gameObject;
                EditorGUIUtility.PingObject(director);
                return;
            }

            bool auto = so.FindProperty("fallImpactAutoCut")?.boolValue ?? true;
            float threshold = so.FindProperty("fallImpactOnsetThreshold")?.floatValue ?? 0.1f;
            float manual = so.FindProperty("fallImpactStartTime")?.floatValue ?? 0f;

            var report = new StringBuilder();
            report.Append($"[BaqueDoPouso] \"{clip.name}\": {clip.length:0.###} s, {clip.channels} canal(is), " +
                          $"{clip.frequency} Hz, Load Type {clip.loadType}.\n");

            if (clip.loadType == AudioClipLoadType.Streaming)
            {
                Debug.LogWarning(report + "STREAMING: as amostras não estão na memória, então não há onda para ler " +
                                 "nem o que cortar — em Play o beat vai tocar o arquivo INTEIRO e avisar. Selecione " +
                                 "o clipe e ponha o Load Type em Decompress On Load.", clip);
                Selection.activeObject = clip;
                return;
            }

            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            int channels = Mathf.Max(1, clip.channels);
            var samples = new float[clip.samples * channels];

            if (!clip.GetData(samples, 0))
            {
                Debug.LogWarning(report + "NÃO DEU PARA LER AS AMOSTRAS deste clipe. Em Play o beat vai tocar o " +
                                 "arquivo inteiro. Verifique o Load Type no importador.", clip);
                Selection.activeObject = clip;
                return;
            }

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float value = Mathf.Abs(samples[i]);
                if (value > peak)
                    peak = value;
            }

            report.Append($"Pico da onda: {peak:0.###}.\n");

            if (peak <= 0f)
            {
                Debug.LogWarning(report + "O CLIPE ESTÁ MUDO (pico zero). Não há golpe para achar.", clip);
                Selection.activeObject = clip;
                return;
            }

            if (!auto)
            {
                report.Append($"Auto Cut DESLIGADO: o corte é o Fall Impact Start Time, {manual:0.###} s");
                report.Append(manual >= clip.length
                    ? " — que passa do fim do clipe, então o beat vai tocar o arquivo inteiro e avisar.\n"
                    : $", sobrando {clip.length - manual:0.##} s de som.\n");

                report.Append("Ligue o Fall Impact Auto Cut para o beat achar a subida sozinho.");
                Debug.Log(report.ToString(), director);
                return;
            }

            float level = peak * Mathf.Clamp(threshold, 0.01f, 0.5f);
            int onset = -1;

            for (int i = 0; i < samples.Length; i++)
            {
                if (Mathf.Abs(samples[i]) >= level)
                {
                    onset = i / channels;
                    break;
                }
            }

            if (onset < 0)
            {
                Debug.LogWarning(report + $"NENHUMA amostra passa de {threshold:P0} do pico — baixe o Fall Impact " +
                                 "Onset Threshold. Em Play o beat vai tocar o arquivo inteiro.", clip);
                Selection.activeObject = clip;
                return;
            }

            float onsetTime = onset / (float)clip.frequency;
            float cutTime = Mathf.Max(0f, onsetTime - OnsetPreRoll);

            report.Append($"Limiar: {threshold:P0} do pico = {level:0.####}.\n");
            report.Append($"A ONDA SOBE em {onsetTime:0.###} s. Corte em {cutTime:0.###} s (pré-rolo de " +
                          $"{OnsetPreRoll * 1000f:0} ms), sobrando {clip.length - cutTime:0.##} s de som.\n");

            report.Append(cutTime <= 0f
                ? "Como o corte cai em 0, o beat vai tocar o arquivo inteiro: este clipe já começa no golpe."
                : $"Ou seja: dos {clip.length:0.##} s do arquivo, o beat joga fora os primeiros {cutTime:0.###} s — é " +
                  "esse tanto de entrada que estava atrasando o baque.");

            Debug.Log(report.ToString(), clip);

            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
        }
    }
}

#if UNITY_EDITOR
using System.Collections;
using System.IO;
using TheDelivery.AI;
using UnityEngine;

namespace TheDelivery.Player
{
    /// <summary>
    /// SÓ EDITOR. Grava uma demonstração da barra de ruído: entra num esconderijo, arma o
    /// <see cref="MicrophoneNoiseSensor"/>, roda um ROTEIRO de ruído simulado (injetado antes do
    /// gate — passa por gate, envelope e detecção como um microfone real) e salva cada quadro
    /// em <see cref="OutputDir"/> a <see cref="Fps"/> quadros/s, com o tempo de jogo travado no
    /// ritmo da gravação (Time.captureFramerate). Adicionado em Play pelo MicrophoneHideSetup.
    /// </summary>
    public sealed class MicMeterDemoRecorder : MonoBehaviour
    {
        public const string OutputDir = "Temp/MicVideoFrames";
        public const string DoneMarker = "Temp/MicVideoFrames/done.txt";
        private const int Fps = 20;

        // (instante em s desde o início do roteiro, fração acima do gate)
        // Entre chaves o nível é interpolado. 0 = no gate (silêncio efetivo), 1 = crítico.
        private static readonly Vector2[] Script =
        {
            new Vector2(0.0f, -1f),   // calibração + silêncio
            new Vector2(1.6f, -1f),
            new Vector2(1.9f, 0.32f), // sussurro
            new Vector2(3.6f, 0.38f),
            new Vector2(3.8f, -1f),   // silêncio (barra desce)
            new Vector2(5.2f, -1f),
            new Vector2(5.5f, 0.70f), // fala
            new Vector2(6.6f, 0.80f),
            new Vector2(7.4f, 0.65f),
            new Vector2(7.6f, -1f),   // silêncio
            new Vector2(9.0f, -1f),
            new Vector2(9.2f, 1.30f), // grito — passa do limite e sustenta
            new Vector2(10.6f, 1.35f),
            new Vector2(10.8f, -1f),  // silêncio de novo (latched: pulsa em vermelho)
            new Vector2(12.6f, -1f),
        };

        private IEnumerator Start()
        {
            if (Directory.Exists(OutputDir))
                Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            HideSpot spot = null;
            foreach (HideSpot s in FindObjectsByType<HideSpot>(FindObjectsInactive.Exclude))
            {
                if (s.name.Contains("Wardrobe")) { spot = s; break; }
                if (spot == null) spot = s;
            }

            MicrophoneNoiseSensor sensor = MicrophoneNoiseSensor.Instance;
            if (spot == null || PlayerHiding.Instance == null || sensor == null)
            {
                Debug.LogWarning("[MicMeterDemoRecorder] Faltou HideSpot, PlayerHiding ou sensor.");
                File.WriteAllText(DoneMarker, "fail");
                yield break;
            }

            PlayerHiding.Instance.EnterHideSpot(spot);
            sensor.SetArmed(true);
            sensor.DebugSimulateNoise(-1f);

            // Espera a transição de entrada terminar (o microfone liga ao final dela).
            while (PlayerHiding.Instance.IsTransitioning)
                yield return null;

            Time.captureFramerate = Fps;
            float duration = Script[Script.Length - 1].x;
            int frame = 0;
            float t0 = Time.time;

            while (Time.time - t0 <= duration)
            {
                sensor.DebugSimulateNoise(Evaluate(Time.time - t0));
                yield return new WaitForEndOfFrame();

                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(OutputDir, $"f{frame:0000}.jpg"), tex.EncodeToJPG(90));
                Destroy(tex);
                frame++;
            }

            Time.captureFramerate = 0;
            sensor.DebugSimulateNoise(-1f);
            File.WriteAllText(DoneMarker, $"{frame} {Fps}");
            Debug.Log($"[MicMeterDemoRecorder] {frame} quadros gravados em {OutputDir} ({Fps} fps).");
        }

        private static float Evaluate(float t)
        {
            for (int i = 0; i < Script.Length - 1; i++)
            {
                Vector2 a = Script[i], b = Script[i + 1];
                if (t < a.x || t > b.x)
                    continue;
                // Silêncio é degrau: sem rampa entre "desligado" e falando.
                if (a.y < 0f || b.y < 0f)
                    return t < b.x ? a.y : b.y;
                return Mathf.Lerp(a.y, b.y, (t - a.x) / Mathf.Max(0.0001f, b.x - a.x));
            }
            return -1f;
        }

        private void OnDestroy() => Time.captureFramerate = 0;
    }
}
#endif

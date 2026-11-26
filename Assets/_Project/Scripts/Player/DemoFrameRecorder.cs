#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace TheDelivery.Player
{
    /// <summary>
    /// SÓ EDITOR. Grava quadros (JPG) e, opcionalmente, o ÁUDIO do jogo (WAV, via
    /// <see cref="AudioRenderer"/>) em <see cref="OutputDir"/>, com o tempo de jogo travado no
    /// ritmo da gravação (Time.captureFramerate) — imagem e som saem sincronizados mesmo se
    /// o Editor renderizar devagar. Ações de roteiro entram com <see cref="At"/>.
    /// Ao terminar escreve <see cref="DoneMarker"/> ("quadros fps").
    /// </summary>
    public sealed class DemoFrameRecorder : MonoBehaviour
    {
        public const string OutputDir = "Temp/DemoFrames";
        public const string DoneMarker = "Temp/DemoFrames/done.txt";
        public const string AudioFile = "Temp/DemoFrames/audio.wav";

        private float duration = 10f;
        private int fps = 24;
        private bool recordAudio;
        private readonly List<(float time, Action action)> cues = new List<(float, Action)>();
        private readonly List<float> audioSamples = new List<float>();
        private int audioChannels = 2;

        public void Begin(float duration, int fps, bool recordAudio)
        {
            this.duration = duration;
            this.fps = fps;
            this.recordAudio = recordAudio;
            StartCoroutine(Record());
        }

        /// <summary>Encerra a gravação no próximo quadro (roteiros que terminam antes da duração prevista).</summary>
        public void FinishEarly() => duration = 0f;

        /// <summary>Agenda uma ação para <paramref name="time"/> s depois do início da gravação.</summary>
        public void At(float time, Action action) => cues.Add((time, action));

        private IEnumerator Record()
        {
            if (Directory.Exists(OutputDir))
                Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            Time.captureFramerate = fps;
            audioChannels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            if (recordAudio)
            {
                // Uma gravação interrompida (Play encerrado no meio) deixaria o AudioRenderer
                // aberto e o Start seguinte falharia ("already recording") — quem fecha é o
                // OnDestroy (audioOpen). Não chamar Stop aqui: sem gravação aberta ele loga
                // ERRO, e com "Error Pause" ligado o Editor pausa o Play.
                AudioRenderer.Start();
                audioOpen = true;
            }

            float t0 = Time.time;
            int frame = 0;
            while (Time.time - t0 <= duration)
            {
                float t = Time.time - t0;
                for (int i = cues.Count - 1; i >= 0; i--)
                {
                    if (t >= cues[i].time)
                    {
                        try { cues[i].action?.Invoke(); }
                        catch (Exception e) { Debug.LogException(e); }
                        cues.RemoveAt(i);
                    }
                }

                yield return new WaitForEndOfFrame();

                // A captura pode vir nula num quadro sem Game View pronta (troca de cena, saída
                // do Play): pula o quadro em vez de matar a gravação inteira.
                Texture2D tex = RenderCamera != null || RenderActiveCamera ? CaptureFromCamera() : ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null)
                    continue;
                File.WriteAllBytes(Path.Combine(OutputDir, $"f{frame:0000}.jpg"), tex.EncodeToJPG(90));
                Destroy(tex);

                if (recordAudio)
                {
                    int n = AudioRenderer.GetSampleCountForCaptureFrame();
                    if (n > 0)
                    {
                        var buf = new NativeArray<float>(n * audioChannels, Allocator.Temp);
                        if (AudioRenderer.Render(buf))
                            audioSamples.AddRange(buf);
                        buf.Dispose();
                    }
                }
                frame++;
            }

            if (recordAudio)
            {
                AudioRenderer.Stop();
                audioOpen = false;
                WriteWav(AudioFile, audioSamples, audioChannels, AudioSettings.outputSampleRate);
            }
            Time.captureFramerate = 0;
            File.WriteAllText(DoneMarker, $"{frame} {fps}");
            Debug.Log($"[DemoFrameRecorder] {frame} quadros ({fps} fps){(recordAudio ? $" + áudio {audioSamples.Count / Mathf.Max(1, audioChannels)} amostras" : "")} em {OutputDir}.");
        }

        private static void WriteWav(string path, List<float> samples, int channels, int rate)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataBytes = samples.Count * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (float s in samples)
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), short.MinValue, short.MaxValue));
        }

        /// <summary>
        /// OPCIONAL: grava desta câmera num RenderTexture de <see cref="RenderSize"/>, em vez de
        /// capturar a Game View. A Game View segue o layout do Editor — encolhida, ela dava quadros
        /// de 875×237. Overlays de UI não entram (as gravações de cinema já os desligam).
        /// </summary>
        public Camera RenderCamera;
        /// <summary>Sem <see cref="RenderCamera"/>: grava a câmera LIGADA de maior depth em cada quadro
        /// (segue cortes de câmera de cutscene, como o take em 3ª pessoa do agarrão).</summary>
        public bool RenderActiveCamera;
        public Vector2Int RenderSize = new Vector2Int(1920, 1080);
        private RenderTexture renderTarget;
        private readonly List<Canvas> overlayCanvases = new List<Canvas>();

        /// <summary>
        /// Desenha a câmera de novo, sob demanda, num RenderTexture do tamanho pedido — COM a UI:
        /// os Canvas em Overlay (pensamentos, pálpebras, fade) não aparecem num render de câmera,
        /// então viram Screen Space - Camera só durante este render e voltam em seguida.
        /// </summary>
        private Texture2D CaptureFromCamera()
        {
            Camera cam = RenderCamera;
            if (cam == null || !cam.isActiveAndEnabled)
            {
                cam = null;
                foreach (var c in Camera.allCameras)
                    if (c.targetTexture == null && (cam == null || c.depth > cam.depth)) cam = c;
            }
            if (cam == null) return null;

            if (renderTarget == null || renderTarget.width != RenderSize.x || renderTarget.height != RenderSize.y)
            {
                if (renderTarget != null) renderTarget.Release();
                renderTarget = new RenderTexture(RenderSize.x, RenderSize.y, 24, RenderTextureFormat.ARGB32);
            }

            // O quadro de destino ANTES de converter as telas sobrepostas: com a câmera ainda na janela do
            // Editor (achatada), o canvas era dimensionado para ela e cobria só uma faixa do quadro 16:9
            // (o clarão aparecia como um retângulo). 2026-11-20.
            var prevTarget = cam.targetTexture;
            cam.targetTexture = renderTarget;

            overlayCanvases.Clear();
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (cv.isRootCanvas && cv.enabled && cv.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    overlayCanvases.Add(cv);
                    cv.renderMode = RenderMode.ScreenSpaceCamera;
                    cv.worldCamera = cam;
                    cv.planeDistance = cam.nearClipPlane + 0.001f; // colado na lente: nada da cena passa na frente da UI
                }
            Canvas.ForceUpdateCanvases();

            // limpa de preto: uma câmera com recorte (tela larga) não pinta as faixas
            var prevActive = RenderTexture.active;
            RenderTexture.active = renderTarget; GL.Clear(true, true, Color.black); RenderTexture.active = prevActive;
            cam.Render();
            cam.targetTexture = prevTarget;

            foreach (var cv in overlayCanvases)
            {
                cv.renderMode = RenderMode.ScreenSpaceOverlay;
                cv.worldCamera = null;
            }

            var prev = RenderTexture.active;
            RenderTexture.active = renderTarget;
            var tex = new Texture2D(RenderSize.x, RenderSize.y, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, RenderSize.x, RenderSize.y), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            return tex;
        }

        private bool audioOpen;

        private void OnDestroy()
        {
            Time.captureFramerate = 0;
            if (renderTarget != null) renderTarget.Release();
            if (audioOpen)
            {
                AudioRenderer.Stop(); // não deixa o AudioRenderer aberto para a próxima gravação
                audioOpen = false;
            }
        }
    }
}
#endif

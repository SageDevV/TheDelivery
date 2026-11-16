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
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
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

        private bool audioOpen;

        private void OnDestroy()
        {
            Time.captureFramerate = 0;
            if (audioOpen)
            {
                AudioRenderer.Stop(); // não deixa o AudioRenderer aberto para a próxima gravação
                audioOpen = false;
            }
        }
    }
}
#endif

using System.IO;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "menuvideo" — a tela de início parada, só ouvindo a trilha (M-00) com o
    /// drone, os apagões e o cantarolar. Play pela Boot (marcador <c>Temp/GameScreenshot.pending</c>
    /// = "600 Assets/Scenes/Boot.unity menuvideo"). Os primeiros 4 s são aquecimento do gravador
    /// (cortados na montagem). Nenhuma entrada: o menu fica aberto do começo ao fim.
    /// </summary>
    [InitializeOnLoad]
    public static class MenuMusicDemo
    {
        private const float Duration = 52f;

        static MenuMusicDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep == "audioprobe")
            {
                AudioProbe();
                return;
            }
            if (prep != "menuvideo")
                return;

            // Mesmo cuidado do NightmareAudioDemo: com o "Mute Audio" da Game view ligado a
            // gravação sai muda — libera durante o Play e restaura ao sair.
            bool wasMuted = EditorUtility.audioMasterMute;
            Debug.Log($"[MenuMusicDemo] Mute Audio={wasMuted}, AudioListener.volume={AudioListener.volume}");
            if (wasMuted)
            {
                EditorUtility.audioMasterMute = false;
                void Restore(PlayModeStateChange s)
                {
                    if (s != PlayModeStateChange.EnteredEditMode) return;
                    EditorUtility.audioMasterMute = true;
                    EditorApplication.playModeStateChanged -= Restore;
                }
                EditorApplication.playModeStateChanged += Restore;
            }

            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (Object.FindFirstObjectByType<MainMenuAtmosphere>() == null)
                    return;
                EditorApplication.update -= Tick;

                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(Duration, 24, true);
                Debug.Log("[MenuMusicDemo] gravando o menu (4 s de aquecimento + trilha).");
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), "menuvideo\n");
            }
            EditorApplication.update += Tick;
        }

        /// <summary>
        /// "audioprobe": 6 s depois do Play, despeja em <c>Temp/AudioProbe.txt</c> o estado do
        /// áudio da cena — ouvintes, mute, pausa e cada AudioSource tocando. Diagnóstico de
        /// gravação muda.
        /// </summary>
        private static void AudioProbe()
        {
            double at = EditorApplication.timeSinceStartup + 6.0;
            var events = new System.Text.StringBuilder();
            void OnCfg(bool deviceWasChanged) =>
                events.AppendLine($"AUDIO CONFIG CHANGED deviceWasChanged={deviceWasChanged} t={Time.time:0.00} realtime={Time.realtimeSinceStartup:0.00}");
            AudioSettings.OnAudioConfigurationChanged += OnCfg;
            string last = "";
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                // Linha do tempo: registra cada mudança do estado das fontes em loop + pausa global.
                var now = new System.Text.StringBuilder();
                now.Append($"pause={AudioListener.pause} ts={Time.timeScale} focus={Application.isFocused} ");
                foreach (AudioSource src in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
                    if (src.clip != null && src.loop)
                        now.Append($"{src.name}:{(src.isPlaying ? "ON" : "off")} ");
                if (now.ToString() != last)
                {
                    last = now.ToString();
                    events.AppendLine($"[t={Time.time:0.00} f={Time.frameCount}] {last}");
                }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Tick;

                AudioSettings.OnAudioConfigurationChanged -= OnCfg;
                var sb = new System.Text.StringBuilder();
                sb.Append(events.Length > 0 ? events.ToString() : "nenhuma troca de configuração de áudio\n");
                sb.AppendLine($"driverCaps={AudioSettings.driverCapabilities} dspBuffer={AudioSettings.GetConfiguration().dspBufferSize}");
                sb.AppendLine($"audioMasterMute={EditorUtility.audioMasterMute} listener.volume={AudioListener.volume} pause={AudioListener.pause}");
                sb.AppendLine($"dsp={AudioSettings.dspTime:0.00} outputRate={AudioSettings.outputSampleRate} speakerMode={AudioSettings.speakerMode}");
                foreach (AudioListener l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                    sb.AppendLine($"LISTENER {l.name} enabled={l.enabled} active={l.gameObject.activeInHierarchy}");
                foreach (AudioSource s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
                    if (s.isPlaying)
                        sb.AppendLine($"SRC {s.name} clip={(s.clip ? s.clip.name : "-")} vol={s.volume:0.00} mute={s.mute} blend={s.spatialBlend:0.0} t={s.time:0.0}");
                sb.AppendLine($"timeScale={Time.timeScale} frame={Time.frameCount} t={Time.time:0.0}");
                foreach (TheDelivery.FX.AmbientMusic m in Object.FindObjectsByType<TheDelivery.FX.AmbientMusic>(FindObjectsInactive.Include))
                {
                    var s = m.GetComponent<AudioSource>();
                    sb.AppendLine($"AMBIENT {m.name} active={m.gameObject.activeInHierarchy} enabled={m.enabled} srcClip={(s && s.clip ? s.clip.name : "-")} " +
                                  $"loadState={(s && s.clip ? s.clip.loadState.ToString() : "-")} isPlaying={(s ? s.isPlaying : false)} vol={(s ? s.volume : -1):0.00} srcEnabled={(s ? s.enabled : false)}");
                }
                foreach (AudioSource s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
                    if (s.clip != null && s.loop)
                        sb.AppendLine($"LOOP {s.name} clip={s.clip.name} load={s.clip.loadState} playing={s.isPlaying} enabled={s.enabled} active={s.gameObject.activeInHierarchy}");
                foreach (TheDelivery.FX.AmbientMusic m in Object.FindObjectsByType<TheDelivery.FX.AmbientMusic>(FindObjectsInactive.Exclude))
                {
                    var s = m.GetComponent<AudioSource>();
                    sb.AppendLine($"PROPS pitch={s.pitch} time={s.time} timeSamples={s.timeSamples} priority={s.priority} mixer={(s.outputAudioMixerGroup ? s.outputAudioMixerGroup.name : "-")} " +
                                  $"ignoreListenerPause={s.ignoreListenerPause} ignoreListenerVolume={s.ignoreListenerVolume} loop={s.loop} spatialize={s.spatialize} bypass={s.bypassEffects} " +
                                  $"comps={string.Join(",", System.Array.ConvertAll(m.GetComponents<Component>(), c => c.GetType().Name))}");
                    s.Play();
                    sb.AppendLine($"TEST Play() direto em {m.name}: isPlaying={s.isPlaying} resource={(s.resource ? s.resource.GetType().Name + ":" + s.resource.name : "null")} clip={(s.clip ? s.clip.name : "null")} gen={s.generator}");
                    var go = new GameObject("ProbeSource");
                    var t = go.AddComponent<AudioSource>();
                    t.clip = s.clip; t.Play();
                    sb.AppendLine($"TEST fonte nova com o mesmo clipe: isPlaying={t.isPlaying}");
                }
                float[] buf = new float[1024];
                AudioListener.GetOutputData(buf, 0);
                float sum = 0f; foreach (float v in buf) sum += v * v;
                sb.AppendLine($"listener RMS={Mathf.Sqrt(sum / buf.Length):0.0000}");
                File.WriteAllText("Temp/AudioProbe.txt", sb.ToString());
                Debug.Log("[MenuMusicDemo] AudioProbe -> Temp/AudioProbe.txt");
                EditorApplication.ExitPlaymode();
            }
            EditorApplication.update += Tick;
        }
    }
}

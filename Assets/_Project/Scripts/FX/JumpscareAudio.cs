using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// O SOM DE SUSTO DO JOGO (2026-11-23) — a base de todo jumpscare. Toca o golpe em 2D, "na cabeça" do
    /// jogador, e no MESMO instante abaixa todos os outros sons do jogo (≈ -15 dB), que voltam devagar.
    /// O contraste é metade do susto; e como o golpe já vem masterizado no teto (-0,5 dBFS), nada pode somar
    /// por cima dele e clipar.
    ///
    /// Clipe-base: <c>SoundEffects/JumpscareHit.wav</c> (o golpe — estalo, sub-grave caindo, metais graves
    /// dissonantes, cordas raspando, chapa de metal — com uma estática de TV leve do começo ao fim). É o MESMO arquivo em todas as cenas (a Recepção também
    /// usa ele direto — a variação "golpe + balde" foi descartada a pedido em 2026-11-23).
    ///
    /// Uso: <c>JumpscareAudio.Preload(clip)</c> antes (início da cena/beat) e <c>JumpscareAudio.Play(clip)</c> na hora.
    /// </summary>
    public static class JumpscareAudio
    {
        /// <summary>Quanto sobra dos outros sons no auge do susto (0,18 ≈ -15 dB).</summary>
        public const float DefaultDuckTo = 0.18f;

        /// <summary>
        /// Deixa o susto CARREGADO antes da hora. Um clipe sem "Preload Audio Data" só começa a carregar no Play —
        /// e o golpe saiu 1,3 s DEPOIS do zelador aparecer (gravação de 2026-11-23). Chame no começo da cena ou
        /// do beat que antecede o susto.
        /// </summary>
        public static void Preload(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }

        /// <summary>
        /// Toca o susto e abaixa o resto. <paramref name="hold"/> = quanto tempo o resto fica abaixado depois que o
        /// golpe SOA; <paramref name="release"/> = quanto tempo leva para voltar. Sobrevive a uma troca de cena.
        /// </summary>
        public static AudioSource Play(AudioClip clip, float volume = 1f, float duckTo = DefaultDuckTo, float hold = 0.9f, float release = 2.2f)
        {
            if (clip == null)
                return null;
            Preload(clip); // se ninguém pré-carregou, ao menos começa agora (o abaixamento espera o som de fato soar)

            var go = new GameObject("Jumpscare");
            Object.DontDestroyOnLoad(go);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;           // 2D: o susto é no ouvido, não "lá"
            source.priority = 0;                // nunca é cortado por falta de vozes
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);

            var ducker = go.AddComponent<JumpscareDucker>();
            ducker.Begin(source, Mathf.Clamp01(duckTo), Mathf.Max(0f, hold), Mathf.Max(0.01f, release));
            source.Play();
            return source;
        }
    }

    /// <summary>
    /// Abaixa as outras fontes durante o susto e devolve o volume delas depois (curva em dB). O relógio do
    /// "segurar/voltar" só anda depois que o golpe está DE FATO tocando — se o clipe atrasar para carregar, o
    /// resto continua abaixado esperando, em vez de voltar por cima do golpe (e clipar).
    /// </summary>
    internal sealed class JumpscareDucker : MonoBehaviour
    {
        private const float Attack = 0.03f;
        private const float MaxWaitForStart = 3f;

        private struct Ducked { public AudioSource Source; public float Original; }

        private readonly List<Ducked> ducked = new List<Ducked>();
        private AudioSource own;
        private float duckTo, hold, release, elapsed, waited, lastGain = 1f;
        private bool running, started;

        public void Begin(AudioSource own, float duckTo, float hold, float release)
        {
            this.own = own; this.duckTo = duckTo; this.hold = hold; this.release = release;
            // Todas as fontes ligadas, tocando ou não: um PlayOneShot disparado durante o susto (um passo, a
            // sola do zelador) também sai abaixado.
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (s != null && s != own && s.isActiveAndEnabled)
                {
                    ducked.Add(new Ducked { Source = s, Original = s.volume });
                    s.volume = Mathf.Clamp01(s.volume * duckTo); // abaixa JÁ, no quadro do Play: o estalo do golpe soa
                }                                                 // antes do próximo Update e somava com o resto cheio (clipava)
            lastGain = duckTo; elapsed = Attack;
            running = true;
        }

        private void Update()
        {
            if (!running)
                return;
            // tempo de jogo SEM a escala (câmera lenta não estica o susto), mas seguindo o captureFramerate das
            // gravações — o unscaledDeltaTime segue o relógio real e encurtaria o abaixamento no vídeo
            float dt = Time.timeScale > 0.01f ? Time.deltaTime / Time.timeScale : Time.unscaledDeltaTime;

            if (!started)
            {
                waited += dt;
                started = own == null || (own.isPlaying && own.timeSamples > 0) || waited > MaxWaitForStart;
            }
            if (started) elapsed += dt;
            else elapsed = Mathf.Min(elapsed + dt, Attack); // já abaixa tudo, mas segura até o golpe soar

            float duckDb = 20f * Mathf.Log10(Mathf.Max(duckTo, 0.0001f));
            float db;
            if (elapsed < Attack) db = duckDb * (elapsed / Attack);
            else if (elapsed < Attack + hold || !started) db = duckDb;
            else db = duckDb * (1f - Mathf.SmoothStep(0f, 1f, (elapsed - Attack - hold) / release));
            float gain = Mathf.Pow(10f, db / 20f);

            for (int i = 0; i < ducked.Count; i++)
            {
                var d = ducked[i];
                if (d.Source == null) continue;
                // Alguém mexeu no volume no meio do susto (um fade, um script de fôlego)? Respeita: o novo valor
                // vira a referência, em vez de ser sobrescrito na volta.
                float expected = d.Original * lastGain;
                if (Mathf.Abs(d.Source.volume - expected) > 0.002f)
                    d.Original = d.Source.volume / Mathf.Max(lastGain, 0.0001f);
                d.Source.volume = Mathf.Clamp01(d.Original * gain);
                ducked[i] = d;
            }
            lastGain = gain;

            if (started && elapsed >= Attack + hold + release)
                Restore();
        }

        private void Restore()
        {
            if (!running) return;
            running = false;
            foreach (var d in ducked)
                if (d.Source != null)
                    d.Source.volume = Mathf.Clamp01(d.Original);
            ducked.Clear();
        }

        private void LateUpdate()
        {
            // terminou de devolver o volume e o golpe acabou: some
            if (!running && (own == null || !own.isPlaying))
                Destroy(gameObject);
        }

        // Se o objeto for destruído antes (troca de cena), ninguém fica abaixado.
        private void OnDestroy() => Restore();
    }
}

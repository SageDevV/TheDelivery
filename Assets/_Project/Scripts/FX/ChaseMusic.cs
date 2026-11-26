using TheDelivery.AI;
using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Música de PERSEGUIÇÃO do killer (2026-11-25), no estilo Puppet Combo: entra de uma vez, agressiva, no instante
    /// em que o killer a vê (AIState.Chase) e fica enquanto ele persegue. Quando ele a perde, segura
    /// <see cref="holdAfterLost"/> s e some. Se ela se esconde, some mais rápido e mascara o microfone pelo tempo do
    /// fade, porque o som das caixas não pode denunciá-la no esconderijo.
    /// Sem clip atribuído, só registra as transições no log; o som do ElevenLabs entra depois.
    /// </summary>
    public sealed class ChaseMusic : MonoBehaviour
    {
        [Tooltip("Golpe de entrada (one-shot) quando a perseguição começa. Opcional.")]
        [SerializeField] private AudioClip chaseSting;
        [Tooltip("Loop da perseguição.")]
        [SerializeField] private AudioClip chaseLoop;
        [Range(0f, 1f)] [SerializeField] private float volume = 0.9f;
        [SerializeField] private float fadeIn = 0.08f;
        [Tooltip("Segundos que a música segura depois que o killer a perde de vista.")]
        [SerializeField] private float holdAfterLost = 2.5f;
        [SerializeField] private float fadeOut = 2.5f;
        [Tooltip("Fade (s) quando ela entra num esconderijo.")]
        [SerializeField] private float fadeOutWhenHidden = 0.8f;

        private AudioSource loopSrc, stingSrc;
        private AntagonistAI[] killers;
        private float nextLookup, lostAt = -1f;
        private bool chasing;
        /// <summary>Só para testes: loga o estado a cada 0,5 s.</summary>
        public static bool verboseLog;
        private float nextLog, nextRetry;
        private static int CountPlaying() { int n = 0; foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (a.isPlaying) n++; return n; }

        private void Awake()
        {
            loopSrc = gameObject.AddComponent<AudioSource>();
            loopSrc.playOnAwake = false; loopSrc.loop = true; loopSrc.spatialBlend = 0f; loopSrc.volume = 0f;
            loopSrc.priority = 0; // prioridade máxima: com muitas fontes na cena, o Unity cortava a música (limite de vozes)
            stingSrc = gameObject.AddComponent<AudioSource>();
            stingSrc.playOnAwake = false; stingSrc.spatialBlend = 0f; stingSrc.priority = 0;
        }

        private void Update()
        {
            // pode haver mais de um AntagonistAI ativo na cena (o killer e, conforme o atalho de debug, outro corpo):
            // basta QUALQUER um estar em perseguição
            if (Time.time >= nextLookup) { killers = FindObjectsByType<AntagonistAI>(FindObjectsSortMode.None); nextLookup = Time.time + 0.5f; }
            bool seen = false;
            if (killers != null)
                foreach (var k in killers)
                    if (k != null && k.isActiveAndEnabled && (k.CurrentState == AntagonistAI.AIState.Chase || k.CurrentState == AntagonistAI.AIState.Attack)) { seen = true; break; }
            bool hidden = PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden;

            if (seen && !chasing)
            {
                chasing = true; lostAt = -1f;
                Debug.Log($"[ChaseMusic] PERSEGUIÇÃO: entra (loop={(chaseLoop != null ? chaseLoop.name : "nenhum")}, id {GetInstanceID()})");
                if (chaseSting != null) stingSrc.PlayOneShot(chaseSting, volume);
                if (chaseLoop != null)
                {
                    if (loopSrc.clip != chaseLoop) loopSrc.clip = chaseLoop;
                    if (!loopSrc.isPlaying) { loopSrc.time = 0f; loopSrc.Play(); }
                    if (verboseLog) Debug.Log($"[ChaseMusic] loop {chaseLoop.name} carregado={chaseLoop.loadState} tocando={loopSrc.isPlaying} fonte ativa={loopSrc.isActiveAndEnabled}");
                }
            }
            else if (!seen && chasing)
            {
                chasing = false; lostAt = Time.time;
                Debug.Log("[ChaseMusic] perdeu de vista: segura e some");
                if (hidden) TheDelivery.Player.MicrophoneNoiseSensor.MaskFor(fadeOutWhenHidden + 0.5f);
            }

            float target;
            float rate;
            if (chasing) { target = volume; rate = volume / Mathf.Max(0.01f, fadeIn); }
            else if (hidden) { target = 0f; rate = volume / Mathf.Max(0.01f, fadeOutWhenHidden); }
            else if (lostAt >= 0f && Time.time - lostAt < holdAfterLost) { target = volume; rate = volume / Mathf.Max(0.01f, fadeIn); }
            else { target = 0f; rate = volume / Mathf.Max(0.01f, fadeOut); }

            loopSrc.volume = Mathf.MoveTowards(loopSrc.volume, target, rate * Time.deltaTime);
            if (verboseLog && Time.time >= nextLog) { nextLog = Time.time + 0.5f; Debug.Log($"[ChaseMusic] t={Time.time:0.0} pausa={AudioListener.pause} vol={AudioListener.volume:0.00} fontes tocando={CountPlaying()} timeScale={Time.timeScale} chasing={chasing} hidden={hidden} alvo={target:0.00} volume={loopSrc.volume:0.00} tocando={loopSrc.isPlaying}"); }
            // se algo parar a fonte no meio da perseguição, religa (a música não pode sumir com ele atrás dela)
            if (target > 0f && chaseLoop != null && !loopSrc.isPlaying && Time.time >= nextRetry)
            {
                nextRetry = Time.time + 1f;
                if (loopSrc.clip != chaseLoop) loopSrc.clip = chaseLoop;
                loopSrc.Play();
                if (verboseLog) Debug.Log("[ChaseMusic] a fonte tinha parado: religada");
            }
            if (!chasing && loopSrc.isPlaying && loopSrc.volume <= 0.001f) loopSrc.Stop();
        }
    }
}

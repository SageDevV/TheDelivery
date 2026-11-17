using System;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// A CIDADE SE APAGANDO (AudioMap J-02..J-09): camadas de ambiente cujo volume segue o
    /// <see cref="NightfallController.Progress"/> (0 no fim de tarde, 1 na noite fechada), e sons
    /// únicos disparados em pontos do anoitecer. A rua começa viva — cidade ao longe, pássaros, um
    /// carro — e perde uma camada a cada trecho, até sobrarem os passos dela, o vento e o silêncio.
    ///
    /// Componente INDEPENDENTE, no espírito do <see cref="AmbientMusic"/>: não conhece beat nem
    /// diretor, só o relógio do anoitecer. Sem NightfallController (ou com ele parado) o progresso é
    /// 0 e a cena fica no "fim de tarde".
    ///
    /// Camada com <c>anchor</c> vazio é 2D (ambiente, dentro da cabeça); com anchor, é 3D e mora
    /// naquele objeto (o zumbido do poste). Evento sem anchor nasce num ponto RELATIVO AO OUVINTE
    /// (<c>listenerOffset</c>) — um latido "lá longe à esquerda" sem precisar de um marcador na cena.
    /// </summary>
    public sealed class NightfallSoundscape : MonoBehaviour
    {
        [Serializable]
        public sealed class Layer
        {
            public string name;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 0.5f;
            [Tooltip("Fração do volume no início, ANTES da entrada (0 = mudo; 1 = já cheio desde o começo).")]
            [Range(0f, 1f)] public float startLevel = 1f;
            [Tooltip("Progresso do anoitecer em que a camada começa a subir até o volume cheio.")]
            [Range(0f, 1f)] public float riseFrom = 0f;
            [Tooltip("Progresso em que ela chega ao volume cheio.")]
            [Range(0f, 1f)] public float riseTo = 0f;
            [Tooltip("Progresso em que ela começa a sumir. 1 = nunca some.")]
            [Range(0f, 1f)] public float fadeFrom = 1f;
            [Tooltip("Progresso em que ela some de vez.")]
            [Range(0f, 1f)] public float fadeTo = 1f;
            [Tooltip("Vazio = 2D. Preenchido = fonte 3D neste objeto.")]
            public Transform anchor;
            [Min(1f)] public float maxDistance = 25f;

            [NonSerialized] public AudioSource source;
        }

        [Serializable]
        public sealed class OneShot
        {
            public string name;
            public AudioClip clip;
            [Range(0f, 1f)] public float atProgress = 0.3f;
            [Range(0f, 1f)] public float volume = 0.6f;
            [Tooltip("Vazio = nasce em listenerOffset, relativo ao ouvinte.")]
            public Transform anchor;
            public Vector3 listenerOffset = new Vector3(-30f, 2f, 25f);
            [Range(0f, 1f)] public float spatialBlend = 0.8f;
            [Min(1f)] public float maxDistance = 80f;

            [NonSerialized] public bool played;
        }

        [SerializeField] private NightfallController nightfall;
        [Tooltip("Segundos para a paisagem toda entrar ao abrir a cena (evita o ambiente nascer seco).")]
        [Min(0f)] [SerializeField] private float sceneFadeIn = 2.5f;
        [Tooltip("Tempo de suavização do volume das camadas (s), para o progresso não 'degrau'.")]
        [Min(0f)] [SerializeField] private float smoothing = 0.5f;
        [SerializeField] private Layer[] layers = Array.Empty<Layer>();
        [SerializeField] private OneShot[] oneShots = Array.Empty<OneShot>();

        /// <summary>Progresso usado agora (0..1) — para debug e evidência.</summary>
        public float CurrentProgress => nightfall != null ? nightfall.Progress : 0f;

        private float clock;
        private Transform listener;

        private void Start()
        {
            foreach (Layer layer in layers)
            {
                if (layer.clip == null)
                    continue;
                var host = layer.anchor != null ? layer.anchor.gameObject : gameObject;
                var s = host.AddComponent<AudioSource>();
                s.clip = layer.clip;
                s.loop = true;
                s.playOnAwake = false;
                s.volume = 0f;
                s.spatialBlend = layer.anchor != null ? 1f : 0f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = layer.maxDistance;
                // Cada loop começa num ponto diferente, para as camadas não "baterem" juntas.
                s.time = UnityEngine.Random.Range(0f, layer.clip.length * 0.9f);
                s.Play();
                layer.source = s;
            }
        }

        private void Update()
        {
            clock += Time.deltaTime;
            float p = CurrentProgress;
            float sceneFade = sceneFadeIn <= 0f ? 1f : Mathf.Clamp01(clock / sceneFadeIn);
            float k = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / smoothing);

            foreach (Layer layer in layers)
            {
                if (layer.source == null)
                    continue;
                float target = layer.volume * LevelAt(layer, p) * sceneFade;
                layer.source.volume = Mathf.Lerp(layer.source.volume, target, k);
            }

            foreach (OneShot shot in oneShots)
            {
                if (shot.played || shot.clip == null || p < shot.atProgress)
                    continue;
                shot.played = true;
                // SÓ NA HORA DELE: começar o ato (ou pular) já de noite não pode soltar de uma vez o
                // latido e o carro do fim de tarde. Passou da janela, ou noite fechada = fica mudo.
                if (p > shot.atProgress + 0.15f || p >= 0.97f)
                    continue;
                Fire(shot);
            }
        }

        private static float LevelAt(Layer layer, float p)
        {
            float rise = layer.riseTo <= layer.riseFrom
                ? (p >= layer.riseFrom ? 1f : 0f)
                : Mathf.InverseLerp(layer.riseFrom, layer.riseTo, p);
            float level = Mathf.Lerp(layer.startLevel, 1f, Mathf.SmoothStep(0f, 1f, rise));
            if (layer.fadeFrom < 1f || layer.fadeTo < 1f)
            {
                float fade = layer.fadeTo <= layer.fadeFrom
                    ? (p >= layer.fadeFrom ? 1f : 0f)
                    : Mathf.InverseLerp(layer.fadeFrom, layer.fadeTo, p);
                level *= 1f - Mathf.SmoothStep(0f, 1f, fade);
            }
            return level;
        }

        private void Fire(OneShot shot)
        {
            Vector3 pos;
            if (shot.anchor != null)
                pos = shot.anchor.position;
            else
            {
                if (listener == null)
                {
                    var l = FindAnyObjectByType<AudioListener>();
                    listener = l != null ? l.transform : transform;
                }
                // Offset no plano do ouvinte (gira com ele), para "à esquerda" ser sempre à esquerda.
                Vector3 fwd = listener.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                Quaternion yaw = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                pos = listener.position + yaw * shot.listenerOffset;
            }

            var go = new GameObject($"OneShot_{shot.name}");
            go.transform.position = pos;
            var s = go.AddComponent<AudioSource>();
            s.spatialBlend = shot.spatialBlend;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 5f;
            s.maxDistance = shot.maxDistance;
            s.dopplerLevel = 0.3f;
            s.PlayOneShot(shot.clip, shot.volume);
            Destroy(go, shot.clip.length + 0.2f);
        }
    }
}

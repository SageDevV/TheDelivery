using UnityEngine;
using UnityEngine.Video;

namespace TheDelivery.FX
{
    /// <summary>
    /// Controla uma TV: LIGA/DESLIGA o vídeo E o brilho da tela juntos, por roteiro
    /// (os directors do Ato 3/Ato 4 chamam <see cref="TurnOn"/>/<see cref="TurnOff"/>).
    /// Encapsula três coisas que devem acender/apagar em conjunto: o
    /// <see cref="VideoPlayer"/> (a estática/ruído), a EMISSÃO do material da tela (a tela
    /// "brilha" com o vídeo) e uma <see cref="Light"/> opcional na frente da TV (o brilho
    /// tremeluzente que ilumina a sala escura).
    ///
    /// NÃO cria o VideoPlayer/RenderTexture — isso é setup de Editor. Assume o pipeline
    /// padrão: um VideoPlayer em Render Mode = Render Texture (loop), a Render Texture usada
    /// no Base Map E no Emission Map do material da tela. Este script só COMANDA (Play/Stop,
    /// liga/desliga a emissão e a luz, tremula o brilho).
    ///
    /// Atmosfera: no Ato 3 (cotidiano) a TV humaniza o ambiente; no Ato 4 (terror) pode
    /// desligar sozinha num susto. O flicker do brilho cria sombras dançantes na sala.
    ///
    /// Emissão: mexe em <see cref="Renderer.material"/> (INSTÂNCIA própria do renderer), NÃO
    /// em sharedMaterial — assim o brilho não vaza para outros objetos que compartilhem o
    /// material nem altera o asset no disco. Mesmo cuidado do TableLamp.
    /// </summary>
    public sealed class TVScreen : MonoBehaviour
    {
        [Header("Vídeo")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Tela / Brilho")]
        [Tooltip("O Renderer da tela da TV (pra controlar a emissão).")]
        [SerializeField] private Renderer screenRenderer;
        [Tooltip("Cor base da emissão da tela (brilho de TV, ex: branco levemente azulado).")]
        [SerializeField] private Color emissionColor = new Color(0.6f, 0.7f, 1f);
        [SerializeField] private float emissionIntensity = 1.5f;

        [Header("Luz ambiente da TV (opcional)")]
        [Tooltip("Uma Point Light opcional na frente da TV, pra iluminar a sala com o brilho tremeluzente.")]
        [SerializeField] private Light tvLight;
        [SerializeField] private float tvLightIntensity = 1f;

        [Header("Tremulação (realismo)")]
        [Tooltip("Se true, o brilho oscila levemente (a luz de TV nunca é estável).")]
        [SerializeField] private bool flickerGlow = true;
        [SerializeField] private float flickerAmount = 0.15f;
        [SerializeField] private float flickerSpeed = 12f;

        [Header("Estado inicial")]
        [SerializeField] private bool startOn = false;

        // "_EmissionColor" + keyword "_EMISSION" valem no URP/Lit e no Standard (Built-in).
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private const string EmissionKeyword = "_EMISSION";

        // Instância própria do material da tela (não sharedMaterial) — cacheada para não
        // reinstanciar a cada frame no flicker. .material já devolve uma instância clonada
        // no 1º acesso; guardamos essa referência.
        private Material screenMaterial;

        // Deslocamento aleatório na amostragem do Perlin, para duas TVs não tremularem em
        // sincronia (cada instância tremula "sozinha").
        private float flickerSeed;

        private bool isOn;

        /// <summary>Estado atual (true = ligada, mostrando vídeo e brilhando).</summary>
        public bool IsOn => isOn;

        private void Awake()
        {
            if (screenRenderer != null)
                screenMaterial = screenRenderer.material; // instância (não sharedMaterial)
            else
                Debug.LogWarning("[TVScreen] screenRenderer não atribuído; a tela não vai brilhar.", this);

            flickerSeed = Random.value * 100f;
        }

        private void Start()
        {
            // Garante o estado inicial coerente (e nunca deixa a emissão "presa" ligada).
            if (startOn)
                TurnOn();
            else
                TurnOff();
        }

        private void Update()
        {
            // A luz de TV nunca é estável: o brilho oscila levemente conforme o conteúdo.
            // Perlin dá um tremular mais orgânico que um seno puro.
            if (isOn && flickerGlow)
            {
                float noise = Mathf.PerlinNoise(flickerSeed + Time.time * flickerSpeed, 0f); // 0..1
                float mul = 1f + (noise - 0.5f) * 2f * flickerAmount;                        // 1 ± flickerAmount

                ApplyEmission(emissionIntensity * mul);
                if (tvLight != null)
                    tvLight.intensity = tvLightIntensity * mul;
            }
        }

        // --- API pública ---------------------------------------------------

        /// <summary>Liga a TV: toca o vídeo, acende a emissão da tela e a luz da TV.</summary>
        public void TurnOn()
        {
            isOn = true;

            if (videoPlayer != null)
                videoPlayer.Play();

            ApplyEmission(emissionIntensity);

            if (tvLight != null)
            {
                tvLight.enabled = true;
                tvLight.intensity = tvLightIntensity;
            }
        }

        /// <summary>Desliga a TV: para o vídeo, apaga a tela (emissão preta) e a luz.</summary>
        public void TurnOff()
        {
            isOn = false;

            if (videoPlayer != null)
                videoPlayer.Stop();

            // Apaga a emissão de vez (tela preta) — nunca deixa o brilho "preso".
            if (screenMaterial != null)
            {
                screenMaterial.SetColor(EmissionColorId, Color.black);
                screenMaterial.DisableKeyword(EmissionKeyword);
            }

            if (tvLight != null)
                tvLight.enabled = false;
        }

        // --- Interno -------------------------------------------------------

        /// <summary>
        /// Acende a emissão da tela na intensidade dada (habilita o keyword e escreve
        /// <c>emissionColor * intensity</c>). No-op sem material. Usado no ligar e a cada
        /// frame no flicker.
        /// </summary>
        private void ApplyEmission(float intensity)
        {
            if (screenMaterial == null)
                return;

            screenMaterial.EnableKeyword(EmissionKeyword);
            screenMaterial.SetColor(EmissionColorId, emissionColor * intensity);
        }
    }
}

using System;
using System.Collections;
using TheDelivery.Interaction;
using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Gerador do Ato 5 (2026-11-26). A Clear dá a partida: fica presa uns segundos no arranque (o motor engasga e pega),
    /// as luzes de emergência da área acendem e a luz do gerador fica verde. A partida FAZ BARULHO: um estouro de ruído
    /// que o Lenin ouve (quando ele já estiver caçando). Avisa o diretor por <see cref="Started"/>.
    /// </summary>
    public sealed class GeneratorStation : MonoBehaviour, IInteractable
    {
        [SerializeField] private string id = "G1";
        [SerializeField] private Light[] areaLights;
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private AudioClip startClip;
        [SerializeField] private AudioClip runLoop;
        [SerializeField] private float crankSeconds = 3f;
        [Tooltip("Raio (m) do barulho da partida.")]
        [SerializeField] private float noiseRadius = 22f;

        public string Id => id;
        public bool Running { get; private set; }
        public static event Action<GeneratorStation> Started;

        private bool busy;
        private AudioSource src;
        private float[] lightTargets;

        public string InteractionPrompt => "Dar partida no gerador";
        public bool CanInteract => !Running && !busy;

        private void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.minDistance = 2f; src.maxDistance = 30f; src.rolloffMode = AudioRolloffMode.Linear; src.playOnAwake = false;
            lightTargets = new float[areaLights != null ? areaLights.Length : 0];
            for (int i = 0; i < lightTargets.Length; i++)
                if (areaLights[i] != null) { lightTargets[i] = areaLights[i].intensity; areaLights[i].intensity = 0f; areaLights[i].enabled = false; }
            SetLamp(new Color(0.8f, 0.08f, 0.05f));
        }

        public void Interact(PlayerInteraction source) => StartCoroutine(Crank(source != null ? source.GetComponent<PlayerController>() : null));

        /// <summary>Partida por roteiro (demos/depuração).</summary>
        public void StartByScript() { if (CanInteract) StartCoroutine(Crank(FindAnyObjectByType<PlayerController>())); }

        private IEnumerator Crank(PlayerController pc)
        {
            busy = true;
            if (pc != null) pc.CanMove = false;
            if (startClip != null) src.PlayOneShot(startClip);
            // engasga: a lâmpada pisca amarela enquanto o motor tenta pegar
            for (float t = 0f; t < crankSeconds; t += Time.deltaTime)
            {
                SetLamp(Mathf.Repeat(t * 3f, 1f) < 0.5f ? new Color(0.9f, 0.6f, 0.05f) : new Color(0.2f, 0.12f, 0.02f));
                yield return null;
            }
            Running = true; busy = false;
            SetLamp(new Color(0.1f, 0.9f, 0.2f));
            if (runLoop != null) { src.clip = runLoop; src.loop = true; src.volume = 0.7f; src.Play(); }
            if (pc != null) pc.CanMove = true;
            var noise = FindAnyObjectByType<PlayerNoise>();
            if (noise != null) noise.EmitBurst(noiseRadius, 2.5f);
            Started?.Invoke(this);
            // as luzes da área acendem tremendo
            for (int i = 0; i < lightTargets.Length; i++) if (areaLights[i] != null) areaLights[i].enabled = true;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / 1.2f) * (UnityEngine.Random.value > 0.25f ? 1f : 0.3f);
                for (int i = 0; i < lightTargets.Length; i++) if (areaLights[i] != null) areaLights[i].intensity = lightTargets[i] * k;
                yield return null;
            }
            for (int i = 0; i < lightTargets.Length; i++) if (areaLights[i] != null) areaLights[i].intensity = lightTargets[i];
        }

        [Tooltip("Luzinha do LED (o navio é escuro: o LED ilumina o entorno do gerador). Cor segue a do LED.")]
        [SerializeField] private Light ledLight;
        [SerializeField] private float ledIntensity = 0.9f;

        private void SetLamp(Color c)
        {
            if (ledLight != null) { ledLight.color = c; ledLight.intensity = ledIntensity * Mathf.Clamp01(c.maxColorComponent * 1.2f); ledLight.enabled = c.maxColorComponent > 0.05f; }
            if (statusLamp == null) return;
            var mpb = new MaterialPropertyBlock();
            statusLamp.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c); mpb.SetColor("_EmissionColor", c * 3f);
            statusLamp.SetPropertyBlock(mpb);
        }
    }
}

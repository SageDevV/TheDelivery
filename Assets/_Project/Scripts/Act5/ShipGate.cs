using System.Collections;
using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// O portão de saída do navio (2026-11-26): um painel com 5 luzes (uma por gerador) e a folha que sobe quando as 5
    /// estão acesas.
    /// </summary>
    public sealed class ShipGate : MonoBehaviour
    {
        [SerializeField] private Transform leaf;
        [SerializeField] private float openHeight = 4.2f;
        [SerializeField] private float openSeconds = 4.5f;
        [SerializeField] private Renderer[] panelLamps;
        [SerializeField] private AudioClip openClip;
        [Tooltip("Luzinhas dos LEDs do painel (uma por lâmpada; o navio é escuro).")]
        [SerializeField] private Light[] panelLights;
        [SerializeField] private float panelLightIntensity = 0.35f;
        [Tooltip("Relé + bipe do painel quando acende mais uma luz.")]
        [SerializeField] private AudioClip powerClip;
        private int lastCount;

        public bool IsOpen { get; private set; }

        private void Awake() => SetPowered(0);

        public void SetPowered(int count)
        {
            if (count > lastCount && powerClip != null && panelLamps != null && panelLamps.Length > 0 && panelLamps[0] != null)
                AudioSource.PlayClipAtPoint(powerClip, panelLamps[0].transform.position, 0.9f);
            lastCount = count;
            if (panelLamps == null) return;
            for (int i = 0; i < panelLamps.Length; i++)
            {
                if (panelLamps[i] == null) continue;
                Color c = i < count ? new Color(0.1f, 0.95f, 0.25f) : new Color(0.25f, 0.03f, 0.02f);
                var mpb = new MaterialPropertyBlock();
                panelLamps[i].GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c); mpb.SetColor("_EmissionColor", c * (i < count ? 4f : 0.5f));
                panelLamps[i].SetPropertyBlock(mpb);
                if (panelLights != null && i < panelLights.Length && panelLights[i] != null)
                { panelLights[i].color = c; panelLights[i].intensity = panelLightIntensity * (i < count ? 1f : 0.35f); }
            }
        }

        public void Open() { if (!IsOpen) StartCoroutine(OpenRoutine()); }

        private IEnumerator OpenRoutine()
        {
            IsOpen = true;
            if (openClip != null) AudioSource.PlayClipAtPoint(openClip, leaf != null ? leaf.position : transform.position, 1f);
            if (leaf == null) yield break;
            Vector3 a = leaf.localPosition, b = a + Vector3.up * openHeight;
            for (float t = 0f; t < openSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / openSeconds);
                // sobe aos trancos: um portão velho e enferrujado
                k = Mathf.Clamp01(k + Mathf.Sin(t * 23f) * 0.004f);
                leaf.localPosition = Vector3.Lerp(a, b, k);
                yield return null;
            }
            leaf.localPosition = b;
        }
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// O DIFUSOR DA LUMINÁRIA ACOMPANHA A LUZ. A emissão do material (o tubo aceso) segue o estado de uma
    /// <see cref="Light"/>: desligada (o <c>NightmareDirector</c> apaga as do corredor em sequência, por
    /// <c>Light.enabled</c>) ou piscando (<see cref="FlickeringLight"/> mexe na intensidade) — o tubo apaga e
    /// pisca junto. Sem isso a sala escurecia com a luminária "acesa".
    /// <list type="bullet">
    /// <item><see cref="source"/>: a luz que manda (pode ser a da luminária vizinha — no corredor do pesadelo só
    /// metade das calhas tem luz real; a outra metade segue a mais próxima).</item>
    /// <item><see cref="broken"/>: lâmpada queimada (sempre apagada).</item>
    /// </list>
    /// Usa <see cref="MaterialPropertyBlock"/> (não cria material por instância) e só escreve quando muda.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class FixtureGlow : MonoBehaviour
    {
        [SerializeField] private Light source;
        [SerializeField] private bool broken;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private Renderer rend;
        private MaterialPropertyBlock block;
        private Color baseEmission = Color.white;
        private float authored = 1f;
        private float last = -1f;

        public void Configure(Light light, bool isBroken) { source = light; broken = isBroken; }

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            var m = rend.sharedMaterial;
            if (m != null && m.HasProperty(EmissionId)) baseEmission = m.GetColor(EmissionId);
            if (source != null) authored = Mathf.Max(0.0001f, source.intensity);
            Apply(Level());
        }

        private float Level()
        {
            if (broken) return 0f;
            if (source == null) return 1f;
            if (!source.enabled || !source.gameObject.activeInHierarchy) return 0f;
            return Mathf.Clamp01(source.intensity / authored);
        }

        private void LateUpdate()
        {
            float l = Level();
            if (Mathf.Abs(l - last) > 0.01f) Apply(l);
        }

        private void Apply(float l)
        {
            last = l;
            rend.GetPropertyBlock(block);
            block.SetColor(EmissionId, baseEmission * l);
            rend.SetPropertyBlock(block);
        }
    }
}

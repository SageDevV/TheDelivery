using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Anéis de gota na água do porão (shader TheDelivery/ShipWater): guarda os 24 impactos mais recentes (x, z, instante,
    /// amplitude) e os entrega ao material a cada quadro. <see cref="Add"/> é chamado pelo <see cref="DripEmitter"/> no
    /// instante exato em que a gota toca a superfície.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class WaterRipples : MonoBehaviour
    {
        private static WaterRipples instance;
        private static readonly int RipplesId = Shader.PropertyToID("_Ripples");
        private static readonly int NowId = Shader.PropertyToID("_RippleNow");
        private static readonly int WaderId = Shader.PropertyToID("_Wader");
        private Vector4 wader;

        /// <summary>Quem anda na água (a Clear): posição, intensidade 0..1 (0 = parada/fora) e direção do passo.</summary>
        public static void SetWader(Vector3 position, float intensity, Vector3 direction)
        {
            if (instance == null) return;
            float yaw = direction.sqrMagnitude > 1e-4f ? Mathf.Atan2(direction.x, direction.z) : instance.wader.w;
            instance.wader = new Vector4(position.x, position.z, intensity, yaw);
        }
        private readonly Vector4[] data = new Vector4[24];
        private int head;
        private Renderer rend;
        private MaterialPropertyBlock mpb;

        private void Awake()
        {
            instance = this;
            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
            for (int i = 0; i < data.Length; i++) data[i] = new Vector4(0f, 0f, -100f, 0f);
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        public static int Active(float life = 2.8f)
        {
            if (instance == null) return -1;
            int n = 0; float now = Time.timeSinceLevelLoad;
            foreach (var d in instance.data) if (now - d.z < life) n++;
            return n;
        }

        public static void Add(Vector3 position, float amplitude = 1f)
        {
            if (instance == null) return;
            instance.data[instance.head] = new Vector4(position.x, position.z, Time.timeSinceLevelLoad, amplitude);
            instance.head = (instance.head + 1) % instance.data.Length;
        }

        private void LateUpdate()
        {
            rend.GetPropertyBlock(mpb);
            mpb.SetVectorArray(RipplesId, data);
            mpb.SetFloat(NowId, Time.timeSinceLevelLoad);
            mpb.SetVector(WaderId, wader);
            rend.SetPropertyBlock(mpb);
        }
    }
}

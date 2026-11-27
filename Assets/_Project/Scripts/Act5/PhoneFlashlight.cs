using TheDelivery.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheDelivery.Act5
{
    /// <summary>
    /// A LANTERNA DO CELULAR (Ato 5, 2026-11-26: "o celular dela deve ser um meio para iluminar o ambiente... as únicas
    /// fontes de iluminação devem ser o celular dela e os leds"). O aparelho (Cellphone.fbx) fica visível na frente da
    /// câmera, embaixo à direita, como os itens segurados (sacola, hambúrguer) — sem mão (pedido do usuário).
    /// A lanterna é um Spot que sai do topo do celular e segue o olhar com um leve atraso; a tela dá um brilho frio fraco
    /// . Criado em tempo de jogo pelo Act5Director (o Player.prefab não muda).
    /// </summary>
    public sealed class PhoneFlashlight : MonoBehaviour
    {
        [SerializeField] private Vector3 holdPosition = new Vector3(0.14f, -0.155f, 0.36f);
        [SerializeField] private Vector3 holdEuler = new Vector3(-12f, -8f, 4f);
        [SerializeField] private float phoneLength = 0.15f;
        [SerializeField] private float lag = 9f;
        [SerializeField] private float beamIntensity = 18f;
        [SerializeField] private float beamRange = 26f;
        [SerializeField] private float beamAngle = 62f;

        private Transform cam, root, beamPivot;
        private Light beam, glow;
        private PlayerController player;
        private TheDelivery.Interaction.PlayerInteraction interaction;
        private AudioClip toggleClip;
        private AudioSource clickSource;
        private float bob;
        /// <summary>0 = o celular caído na mão junto ao chão (ela deitada); 1 = erguido na posição de sempre.</summary>
        public float Raise { get; set; } = 1f;
        private static readonly Vector3 LowPosition = new Vector3(0.22f, -0.42f, 0.30f);
        private static readonly Vector3 LowEuler = new Vector3(55f, -25f, 30f);
        private Quaternion beamRot;

        public Light Beam => beam;
        public bool On { get => beam != null && beam.enabled; set { if (beam != null) beam.enabled = value; } }   // a tela continua acesa (fraca) com a lanterna apagada

        public static PhoneFlashlight Create(Camera camera, GameObject phonePrefab, PlayerController pc, AudioClip toggleClip = null)
        {
            var go = new GameObject("PhoneFlashlight");
            go.transform.SetParent(camera.transform, false);
            var pf = go.AddComponent<PhoneFlashlight>();
            pf.Build(camera.transform, phonePrefab, pc);
            pf.toggleClip = toggleClip;
            pf.interaction = pc != null ? pc.GetComponentInChildren<TheDelivery.Interaction.PlayerInteraction>(true) : null;
            pf.clickSource = go.AddComponent<AudioSource>();
            pf.clickSource.playOnAwake = false; pf.clickSource.spatialBlend = 0f; pf.clickSource.volume = 0.7f;
            return pf;
        }

        /// <summary>F (a tecla de interagir) sem nada interativo na mira liga/desliga a lanterna. Só com ela livre:
        /// não escondida, não em cutscene, já de pé.</summary>
        private void HandleToggle()
        {
            if (interaction == null || !interaction.PressedOnNothing) return;
            if (player != null && !player.CanMove) return;
            var hiding = PlayerHiding.Instance;
            if (hiding != null && (hiding.IsHidden || hiding.IsTransitioning || hiding.IsCaptured)) return;
            On = !On;
            if (toggleClip != null && clickSource != null) { clickSource.pitch = On ? 1.08f : 0.94f; clickSource.PlayOneShot(toggleClip); }
        }

        private void Build(Transform camera, GameObject phonePrefab, PlayerController pc)
        {
            cam = camera; player = pc;
            root = new GameObject("HeldPhone").transform;
            root.SetParent(transform, false);
            root.localPosition = holdPosition; root.localRotation = Quaternion.Euler(holdEuler);

            if (phonePrefab != null)
            {
                var vis = Instantiate(phonePrefab, root);
                vis.name = "Cellphone";
                foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Destroy(c);
                // de pé, tela para a câmera: o eixo mais fino do aparelho aponta para trás (−Z do root), o mais comprido para cima
                vis.transform.localPosition = Vector3.zero; vis.transform.localRotation = Quaternion.identity; vis.transform.localScale = Vector3.one;
                var rs = vis.GetComponentsInChildren<Renderer>(true);
                Bounds lb = LocalBounds(vis.transform, rs);
                Vector3 s = lb.size;
                int thin = s.x <= s.y && s.x <= s.z ? 0 : (s.y <= s.z ? 1 : 2);
                int lng = s.x >= s.y && s.x >= s.z ? 0 : (s.y >= s.z ? 1 : 2);
                Vector3 ax(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                // a tela é a face do eixo fino: ScreenSign decide o lado (+1 = +eixo fino é a tela)
                vis.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(ax(thin) * -ScreenSign, ax(lng)));
                float k = phoneLength / Mathf.Max(0.0001f, s[lng]);
                vis.transform.localScale = Vector3.one * k;
                vis.transform.localPosition = -(vis.transform.localRotation * (lb.center * k));
                // a tela acesa, mas fraca (no breu, cheia ela estoura a imagem)
                var mpb = new MaterialPropertyBlock();
                foreach (var r in rs)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", new Color(0.12f, 0.14f, 0.18f)); r.SetPropertyBlock(mpb);
                }
                SetLayer(vis.transform, gameObject.layer);
            }

            // a lanterna: no topo do aparelho, apontando para a frente da câmera (com atraso)
            beamPivot = new GameObject("Flashlight").transform;
            beamPivot.SetParent(transform, false);
            beam = beamPivot.gameObject.AddComponent<Light>();
            beam.type = LightType.Spot; beam.color = new Color(1f, 0.96f, 0.88f);
            beam.intensity = beamIntensity; beam.range = beamRange; beam.spotAngle = beamAngle; beam.innerSpotAngle = beamAngle * 0.45f;
            beam.shadows = LightShadows.Soft; beam.shadowStrength = 0.95f; beam.shadowNearPlane = 0.2f;
            beam.renderMode = LightRenderMode.ForcePixel;
            beamRot = cam.rotation;

            // o brilho da tela (frio, fraco): ilumina a mão e o moletom
            var g = new GameObject("ScreenGlow"); g.transform.SetParent(root, false);
            g.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            glow = g.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(0.62f, 0.75f, 1f); glow.intensity = 0.05f; glow.range = 0.45f; glow.shadows = LightShadows.None;

        }

        /// <summary>+1: a tela é o lado +eixo fino do FBX; −1: o lado −eixo fino. Ajustado olhando a evidência.</summary>
        public static float ScreenSign = 1f;

        private void Update()
        {
            if (cam == null) return;
            HandleToggle();
            TheDelivery.AI.PlayerLight.On = On && isActiveAndEnabled;
            // balanço ao andar
            bool moving = player != null && player.IsMoving;
            bob += Time.deltaTime * (moving ? 7.5f : 1.2f);
            float amp = moving ? 0.008f : 0.002f;
            float r = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Raise));
            Vector3 target = Vector3.Lerp(LowPosition, holdPosition, r) + new Vector3(Mathf.Sin(bob) * amp, Mathf.Abs(Mathf.Cos(bob)) * amp, 0f) * r;
            root.localPosition = Vector3.Lerp(root.localPosition, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            root.localRotation = Quaternion.Slerp(root.localRotation, Quaternion.Slerp(Quaternion.Euler(LowEuler), Quaternion.Euler(holdEuler), r), 1f - Mathf.Exp(-12f * Time.deltaTime));
            // o feixe segue o olhar com um leve atraso (a mão não é uma câmera)
            beamRot = Quaternion.Slerp(beamRot, cam.rotation, 1f - Mathf.Exp(-lag * Time.deltaTime));
            beamPivot.position = root.TransformPoint(new Vector3(0f, phoneLength * 0.45f, 0.01f));
            beamPivot.rotation = beamRot;
        }

        private void OnDisable() => TheDelivery.AI.PlayerLight.On = false;

        private static Bounds LocalBounds(Transform root, Renderer[] rs)
        {
            var b = new Bounds(); bool init = false;
            foreach (var r in rs)
            {
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m == null) continue;
                var mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = root.InverseTransformPoint(r.transform.TransformPoint(p));
                    if (!init) { b = new Bounds(w, Vector3.zero); init = true; } else b.Encapsulate(w);
                }
            }
            return b;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}

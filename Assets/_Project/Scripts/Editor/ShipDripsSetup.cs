using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// GOTEIRAS DO NAVIO (Ato 5, 2026-11-26: "precisa cair algumas gotas... não o suficiente para parecer que é uma chuva").
    /// ~25 pontos no teto das salas pingando uma gota de vez em quando (1 a cada 1,5–4 s, cada ponto no seu ritmo). A gota
    /// cai na aceleração da gravidade e morre exatamente no chão (tempo de queda = √(2h/g)); ao morrer, espirra em gotículas
    /// e, na área inundada, abre um anel na água. Material de partícula ILUMINADO: no breu as gotas só aparecem na luz da
    /// lanterna do celular ou de um LED. Chamado no fim do <see cref="ShipFinishSetup"/>.
    /// </summary>
    public static class ShipDripsSetup
    {
        private const string Dir = "Assets/_Project/Materials/Ship";

        // (x, z, altura do teto, altura do chão/água, inundada)
        private static readonly (float x, float z, float ceil, float floor, bool wet)[] Spots =
        {
            (-23.8f, -14.2f, 3f, 0f, false), (-25.2f, -16.8f, 3f, 0f, false), (-19f, -13f, 3f, 0f, false),
            (-14f, -13.6f, 3f, 0f, false), (-7f, -14.6f, 3f, 0f, false), (-10.3f, -8f, 3f, 0f, false),
            (0f, -6f, 6f, 0f, false), (6f, -15f, 6f, 0f, false), (12f, -9f, 6f, 0f, false), (-2f, -12f, 6f, 0f, false),
            (17f, -6f, 3.5f, 0f, false), (23f, -12f, 3.5f, 0f, false), (19f, -18f, 3.5f, 0f, false),
            (-20f, 8f, 8f, 0f, false), (-12f, 16f, 8f, 0f, false), (-5f, 9f, 8f, 0f, false), (3f, 14f, 8f, 0f, false), (-16f, 0.8f, 8f, 0f, false),
            (10f, 4f, 6.1f, -0.04f, true), (15f, 12f, 6.1f, -0.04f, true), (20f, 6f, 6.1f, -0.04f, true),
            (25f, 10f, 6.1f, -0.04f, true), (12f, 17f, 6.1f, -0.04f, true), (18f, 1.5f, 6.1f, -0.04f, true), (8f, 11f, 6.1f, -0.04f, true),
        };

        public static void Build(Transform root, System.Text.StringBuilder log)
        {
            var old = root.Find("Drips"); if (old) Object.DestroyImmediate(old.gameObject);
            var g = new GameObject("Drips").transform; g.SetParent(root, false);
            // gota translúcida: só um brilho quando a luz pega
            var dropMat = Mat("Ship_Drip", Tex("ship_drip_dot", Dot), new Color(0.82f, 0.88f, 0.92f, 0.55f));
            var rnd = new System.Random(77);
            int i = 0;
            foreach (var s in Spots)
            {
                float top = s.ceil - 0.04f;
                float fall = Mathf.Sqrt(2f * (top - s.floor) / 9.81f);
                var go = new GameObject($"Drip_{i:00}"); go.transform.SetParent(g, false);
                go.transform.position = new Vector3(s.x, top, s.z);
                var drop = Particles(go.transform, "Drop", dropMat, stretch: true, velScale: 0.022f, lenScale: 1.15f, max: 6);
                var splash = Particles(go.transform, "Splash", dropMat, stretch: true, velScale: 0.03f, lenScale: 1.0f, max: 24);
                splash.transform.position = new Vector3(s.x, s.floor, s.z);
                var de = go.AddComponent<TheDelivery.Act5.DripEmitter>();
                float lo = 1.6f + (float)rnd.NextDouble() * 0.8f, hi = 3.4f + (float)rnd.NextDouble() * 1.4f;
                de.Setup(drop, splash, fall - 0.01f, s.floor, s.wet, new Vector2(lo, hi));
                i++;
            }
            log.AppendLine($"{i} goteiras no teto ({Spots.Count(s => s.wet)} sobre a água, com anel no shader da água).");
        }

        /// <summary>Sistema de partículas sem emissão própria (o DripEmitter emite), com gravidade, no mundo.</summary>
        private static ParticleSystem Particles(Transform parent, string name, Material mat, bool stretch, float velScale, float lenScale, int max)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.duration = 5f;
            main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.008f;
            main.gravityModifier = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.velocityScale = velScale; r.lengthScale = lenScale;
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------------ material e texturas
        private static Material Mat(string name, Texture2D tex, Color c)
        {
            string p = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); } else m.shader = sh;
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetFloat("_Smoothness", 0.9f);
            m.SetFloat("_Cull", 0f);   // dupla face: o anel horizontal na água estava virado para baixo e sumia
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static float Dot(float d) => Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
        private static float Ring(float d) => Mathf.Clamp01(1f - Mathf.Abs(d - 0.78f) / 0.2f);

        private static Texture2D Tex(string name, System.Func<float, float> alpha)
        {
            string p = $"{Dir}/{name}.png";
            if (!File.Exists(p))
            {
                const int N = 64;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(Mathf.Sqrt(dx * dx + dy * dy))));
                }
                File.WriteAllBytes(p, t.EncodeToPNG()); Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(p);
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
    }
}

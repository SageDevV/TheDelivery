using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Texturas SEM EMENDA (o ruído é periódico) para pisos e revestimentos do prédio (2026-11-24): azulejo, porcelanato,
    /// laminado, granito, cerâmica de área externa, pintura de rodameio, asfalto e piso de concreto. Cada uma sai como
    /// base_color + normal + metallic_smoothness (R metal, A suavidade) e vira um material URP Lit.
    /// Uma textura cobre <c>meters</c> metros (as malhas de revestimento usam UV em metros / meters).
    /// </summary>
    public static class SurfaceTextureGen
    {
        public const string Dir = "Assets/_Project/Materials/Surfaces";

        public enum Kind { SquareTile, Subway, Planks, Granite, Wainscot, Asphalt, Pavers }

        public sealed class Spec
        {
            public string name; public Kind kind; public int size = 1024; public float meters = 1f;
            public float tile = 0.2f, tileH = 0.2f;     // medidas da peça (m)
            public float grout = 0.003f;                // junta (m)
            public Color a, b, groutColor;              // tons da peça (sorteados entre a e b) e da junta
            public float smooth = 0.6f, groutSmooth = 0.15f, variation = 0.06f, speckle = 0f, bump = 1f;
            public int seed = 1;
        }

        // ------------------------------------------------------------------ ruído periódico
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }
        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);
            int Wrap(int v) => ((v % period) + period) % period;
            float h00 = Hash(Wrap(x0), Wrap(y0), seed), h10 = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float h01 = Hash(Wrap(x0), Wrap(y0 + 1), seed), h11 = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy);
        }
        /// <summary>fBm periódico: u,v em [0,1) da textura; baseCells = células na 1ª oitava (inteiro).</summary>
        public static float Fbm(float u, float v, int baseCells, int octaves, int seed, float stretchU = 1f, float stretchV = 1f)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cu = Mathf.Max(1, Mathf.RoundToInt(baseCells * stretchU)), cv = Mathf.Max(1, Mathf.RoundToInt(baseCells * stretchV));
            for (int o = 0; o < octaves; o++)
            {
                int pu = cu << o, pv = cv << o;
                // usa o período do eixo U para os dois (grade quadrada) quando iguais; senão, duas grades
                float n = pu == pv ? Value(u * pu, v * pv, pu, seed + o * 31) : Value2(u * pu, v * pv, pu, pv, seed + o * 31);
                sum += n * amp; norm += amp; amp *= 0.5f;
            }
            return sum / norm;
        }
        private static float Value2(float x, float y, int pu, int pv, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);
            int Wu(int v) => ((v % pu) + pu) % pu; int Wv(int v) => ((v % pv) + pv) % pv;
            float h00 = Hash(Wu(x0), Wv(y0), seed), h10 = Hash(Wu(x0 + 1), Wv(y0), seed);
            float h01 = Hash(Wu(x0), Wv(y0 + 1), seed), h11 = Hash(Wu(x0 + 1), Wv(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy);
        }

        // ------------------------------------------------------------------ geração
        public static Material Build(Spec s)
        {
            Directory.CreateDirectory(Dir);
            int N = s.size;
            var col = new Color[N * N]; var height = new float[N * N]; var gloss = new float[N * N];
            float m = s.meters;
            int cellsU = Mathf.Max(1, Mathf.RoundToInt(m / s.tile)), cellsV = Mathf.Max(1, Mathf.RoundToInt(m / s.tileH));
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;           // 0..1 na textura
                float pu = u * m, pv = v * m;                            // metros
                Color c; float h, g;
                switch (s.kind)
                {
                    case Kind.SquareTile:
                    case Kind.Subway:
                    case Kind.Planks:
                    {
                        // grade de peças; Subway/Planks: fileiras alternadas deslocadas
                        float tw = m / cellsU, th = m / cellsV;
                        int row = Mathf.FloorToInt(pv / th);
                        float off = s.kind == Kind.SquareTile ? 0f : (s.kind == Kind.Subway ? (row % 2) * 0.5f * tw : ((row * 37) % 5) / 5f * tw);
                        float px = pu + off; int colIx = Mathf.FloorToInt(px / tw);
                        float lx = px - colIx * tw, ly = pv - row * th;
                        int ci = ((colIx % cellsU) + cellsU) % cellsU;
                        float rnd = Hash(ci, row, s.seed), rnd2 = Hash(ci + 17, row + 5, s.seed);
                        float edge = Mathf.Min(Mathf.Min(lx, tw - lx), Mathf.Min(ly, th - ly));
                        bool groutPix = edge < s.grout * 0.5f;
                        float bevel = Mathf.Clamp01((edge - s.grout * 0.5f) / 0.004f);
                        Color baseC = Color.Lerp(s.a, s.b, rnd);
                        float fine = Fbm(u, v, Mathf.RoundToInt(m * 40), 4, s.seed + 3);
                        if (s.kind == Kind.Planks)
                        {
                            // veio do laminado: ruído esticado ao longo da tábua (u)
                            float grain = Fbm(u, v, Mathf.RoundToInt(m * 3), 5, s.seed + 7, 1f, 14f);
                            float streak = Fbm(u, v, Mathf.RoundToInt(m * 2), 4, s.seed + 9, 1f, 60f);
                            baseC = Color.Lerp(baseC * 0.82f, baseC * 1.12f, grain * 0.6f + streak * 0.4f);
                            baseC *= 1f + (rnd2 - 0.5f) * 0.12f;
                        }
                        else
                        {
                            baseC *= 1f + (fine - 0.5f) * s.variation * 2f;
                            if (s.speckle > 0f) { float sp = Hash(x * 3 + 1, y * 3 + 2, s.seed); if (sp > 1f - s.speckle) baseC *= 0.75f; }
                        }
                        c = groutPix ? s.groutColor * (0.9f + 0.2f * fine) : baseC;
                        h = groutPix ? 0f : 0.6f + 0.4f * bevel + (fine - 0.5f) * 0.05f;
                        g = groutPix ? s.groutSmooth : s.smooth * (0.92f + 0.16f * fine);
                        break;
                    }
                    case Kind.Granite:
                    {
                        float tw = m / cellsU; int cx = Mathf.FloorToInt(pu / tw), cy = Mathf.FloorToInt(pv / tw);
                        float lx = pu - cx * tw, ly = pv - cy * tw;
                        float edge = Mathf.Min(Mathf.Min(lx, tw - lx), Mathf.Min(ly, tw - ly));
                        bool groutPix = edge < s.grout * 0.5f;
                        float big = Fbm(u, v, Mathf.RoundToInt(m * 6), 5, s.seed);
                        float sp = Hash(x, y, s.seed + 11), sp2 = Hash(x / 2, y / 2, s.seed + 13);
                        Color cc = Color.Lerp(s.a, s.b, big);
                        if (sp > 0.93f) cc = Color.Lerp(cc, Color.black, 0.65f);
                        else if (sp2 > 0.90f) cc = Color.Lerp(cc, Color.white, 0.45f);
                        c = groutPix ? s.groutColor : cc;
                        h = groutPix ? 0f : 0.8f;
                        g = groutPix ? s.groutSmooth : s.smooth;
                        break;
                    }
                    case Kind.Wainscot:
                    {
                        // rodameio pintado: v 0..1 = 0..altura; friso de madeira no topo (7%), rodapé em baixo (8%)
                        float roller = Fbm(u, v, Mathf.RoundToInt(m * 8), 4, s.seed, 1f, 0.5f);
                        c = s.a * (0.94f + roller * 0.1f); h = 0.5f; g = s.smooth;
                        if (v > 0.93f) { float t = (v - 0.93f) / 0.07f; c = s.b * (0.85f + 0.25f * Mathf.Sin(t * Mathf.PI)); h = 0.6f + 0.4f * Mathf.Sin(t * Mathf.PI); g = 0.45f; }
                        else if (v < 0.08f) { c = s.groutColor; h = 0.9f; g = 0.35f; }
                        break;
                    }
                    case Kind.Asphalt:
                    {
                        float big = Fbm(u, v, Mathf.RoundToInt(m * 0.75f), 4, s.seed);
                        float mid = Fbm(u, v, Mathf.RoundToInt(m * 6), 4, s.seed + 5);
                        float agg = Hash(x, y, s.seed + 21);
                        c = Color.Lerp(s.a, s.b, big * 0.6f + mid * 0.4f);
                        if (agg > 0.86f) c *= 1.35f; else if (agg < 0.08f) c *= 0.7f;
                        h = mid * 0.6f + (agg > 0.86f ? 0.4f : 0f); g = s.smooth * (0.6f + 0.8f * (1f - big));   // poças: partes mais lisas
                        break;
                    }
                    default: // Pavers: blocos de concreto com junta de areia
                    {
                        float tw = m / cellsU; int cx = Mathf.FloorToInt(pu / tw), cy = Mathf.FloorToInt(pv / tw);
                        float lx = pu - cx * tw, ly = pv - cy * tw;
                        float edge = Mathf.Min(Mathf.Min(lx, tw - lx), Mathf.Min(ly, tw - ly));
                        bool joint = edge < s.grout * 0.5f;
                        int cix = ((cx % cellsU) + cellsU) % cellsU, ciy = ((cy % cellsU) + cellsU) % cellsU;
                        float rnd = Hash(cix, ciy, s.seed);
                        float grain = Fbm(u, v, Mathf.RoundToInt(m * 25), 4, s.seed + 2);
                        float stain = Fbm(u, v, Mathf.RoundToInt(m * 2), 4, s.seed + 4);
                        c = joint ? s.groutColor : Color.Lerp(s.a, s.b, rnd) * (0.9f + grain * 0.2f) * (0.85f + stain * 0.2f);
                        h = joint ? 0f : 0.6f + 0.4f * Mathf.Clamp01((edge - s.grout * 0.5f) / 0.01f) + grain * 0.1f;
                        g = joint ? s.groutSmooth : s.smooth;
                        break;
                    }
                }
                int i = y * N + x; col[i] = c; height[i] = h; gloss[i] = Mathf.Clamp01(g);
            }

            string td = $"{Dir}/{s.name}";
            Directory.CreateDirectory(td);
            var baseT = new Texture2D(N, N, TextureFormat.RGBA32, true);
            baseT.SetPixels(col); baseT.Apply();
            File.WriteAllBytes($"{td}/base_color.png", baseT.EncodeToPNG());
            var nrm = new Color[N * N]; var ms = new Color[N * N];
            float k = s.bump * N / (s.meters * 300f);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float hl = height[y * N + (x + N - 1) % N], hr = height[y * N + (x + 1) % N];
                float hd = height[((y + N - 1) % N) * N + x], hu = height[((y + 1) % N) * N + x];
                var n = new Vector3((hl - hr) * k, (hd - hu) * k, 1f).normalized;
                nrm[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                ms[y * N + x] = new Color(0f, 0f, 0f, gloss[y * N + x]);
            }
            var nT = new Texture2D(N, N, TextureFormat.RGBA32, false, true); nT.SetPixels(nrm); nT.Apply();
            File.WriteAllBytes($"{td}/normal.png", nT.EncodeToPNG());
            var mT = new Texture2D(N, N, TextureFormat.RGBA32, false, true); mT.SetPixels(ms); mT.Apply();
            File.WriteAllBytes($"{td}/metallic_smoothness.png", mT.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(baseT); UnityEngine.Object.DestroyImmediate(nT); UnityEngine.Object.DestroyImmediate(mT);

            AssetDatabase.ImportAsset($"{td}/base_color.png");
            AssetDatabase.ImportAsset($"{td}/normal.png");
            AssetDatabase.ImportAsset($"{td}/metallic_smoothness.png");
            Setup($"{td}/base_color.png", TextureImporterType.Default, true);
            Setup($"{td}/normal.png", TextureImporterType.NormalMap, false);
            Setup($"{td}/metallic_smoothness.png", TextureImporterType.Default, false);

            string mp = $"{Dir}/{s.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, mp); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{td}/base_color.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{td}/normal.png")); mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{td}/metallic_smoothness.png")); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 1f); mat.SetFloat("_Smoothness", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Setup(string path, TextureImporterType type, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = type; ti.sRGBTexture = srgb; ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }

        public static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
    }
}

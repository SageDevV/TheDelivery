using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.Narrative
{
    // Beat 5 - O MUNDO VERMELHO (2026-11-20): no quadro em que a mão fecha no pescoço dela, o cenário
    // inteiro some e o que sobra é a criatura, a Clear e um vazio vermelho imenso com nuvens negras
    // (acima, no horizonte e abaixo). Volta tudo na PISCADA da queda: no instante em que o olho dela
    // fecha (o mesmo em que HideFallObjects apaga o corredor), o mundo vermelho sai e ela abre os olhos
    // no cenário normal da queda. Parte do NightmareDirector (partial).
    //
    // O céu é uma textura equirretangular gerada aqui (gradiente + nuvens de ruído fractal) num
    // Skybox/Panoramic que gira devagar — as nuvens derivam. Gerada em fatias, alguns quadros por vez,
    // desde o começo do beat (PrepareRedWorld), para não engasgar no agarrão.
    public sealed partial class NightmareDirector
    {
        private const int RedSkyW = 1024, RedSkyH = 512;
        private Texture2D redSkyTex;
        private Material redSkyMat;
        private bool redSkyReady, redSkyBuilding;
        private bool redWorldActive;
        private readonly List<Renderer> redWorldHidden = new List<Renderer>();
        private Material savedSkybox;
        private bool savedFog;
        private Color savedFogColor, savedAmbient;
        private FogMode savedFogMode;
        private float savedFogDensity;
        private UnityEngine.Rendering.AmbientMode savedAmbientMode;
        private Camera redWorldCamera;
        private CameraClearFlags savedRedClear;
        private float redSkySpin;

        /// <summary>Começa a gerar o céu vermelho em segundo plano (chamado no início do beat).</summary>
        private void PrepareRedWorld()
        {
            if (redSkyReady || redSkyBuilding) return;
            StartCoroutine(BuildRedSky());
        }

        private IEnumerator BuildRedSky()
        {
            redSkyBuilding = true;
            redSkyTex = new Texture2D(RedSkyW, RedSkyH, TextureFormat.RGB24, false)
            {
                name = "PesadeloRedSky", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var px = new Color32[RedSkyW * RedSkyH];
            const int rowsPerFrame = 48;
            for (int y = 0; y < RedSkyH; y++)
            {
                float v = (y + 0.5f) / RedSkyH;                 // 0 = embaixo, 1 = em cima
                float fromHorizon = Mathf.Abs(v - 0.5f) * 2f;    // 0 no horizonte, 1 nos polos
                // GRADIENTE: brasa no horizonte, carmim escurecendo para cima, sangue escuro embaixo.
                Color sky = v >= 0.5f
                    ? Color.Lerp(new Color(1.0f, 0.24f, 0.08f), new Color(0.25f, 0.01f, 0.02f), Mathf.Pow(fromHorizon, 0.45f))
                    : Color.Lerp(new Color(0.9f, 0.16f, 0.06f), new Color(0.12f, 0.00f, 0.01f), Mathf.Pow(fromHorizon, 0.45f));
                // As nuvens se juntam numa faixa larga em volta do horizonte e num mar abaixo dele.
                float band = Mathf.Clamp01(1.25f - fromHorizon * 1.1f);
                for (int x = 0; x < RedSkyW; x++)
                {
                    float u = (x + 0.5f) / RedSkyW;
                    // costura horizontal sem emenda: mistura o ruído em u e em u-1
                    float n = Mathf.Lerp(Fbm(u * 22f, v * 7f), Fbm((u - 1f) * 22f, v * 7f), u);
                    // Ajustado no vídeo: em escala 9x4,5 saía UMA mancha borrada. Mais fino (22x7, esticado na
                    // horizontal) e com corte mais duro: muitas nuvens recortadas.
                    float big = Mathf.Lerp(Fbm(u * 5f + 40f, v * 2.5f), Fbm((u - 1f) * 5f + 40f, v * 2.5f), u);
                    float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.47f, 0.58f, n * 0.7f + big * 0.3f)) * band;
                    // borda das nuvens pega um pouco do brilho vermelho (contorno), o miolo é negro
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(cloud - 0.35f) * 4f) * 0.25f;
                    Color c = Color.Lerp(sky, new Color(0.02f, 0f, 0.005f), Mathf.Clamp01(cloud * 1.05f));
                    c += new Color(0.35f, 0.04f, 0.02f) * rim * band;
                    px[y * RedSkyW + x] = c;
                }
                if (y % rowsPerFrame == rowsPerFrame - 1) yield return null;
            }
            redSkyTex.SetPixels32(px);
            redSkyTex.Apply(false, true);

            Shader pano = Shader.Find("Skybox/Panoramic");
            if (pano == null)
            {
                Debug.LogWarning("[NightmareDirector] Shader Skybox/Panoramic não encontrado — o mundo vermelho fica sem céu (inclua o shader em Always Included Shaders para o build).", this);
            }
            else
            {
                redSkyMat = new Material(pano) { name = "PesadeloRedSky" };
                redSkyMat.SetTexture("_MainTex", redSkyTex);
                redSkyMat.SetFloat("_Mapping", 1f);
                redSkyMat.SetFloat("_ImageType", 0f);
                redSkyMat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
                redSkyMat.SetFloat("_Exposure", 1f);
            }
            redSkyReady = true;
            redSkyBuilding = false;
        }

        private static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 5; o++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq + 31.7f * o, y * freq + 17.3f * o);
                freq *= 2.03f; amp *= 0.5f;
            }
            return sum / 0.97f;
        }

        /// <summary>
        /// TROCA O MUNDO: esconde todo renderer da cena que não seja a criatura ou o jogador (o corpo
        /// dela em 1ª pessoa), põe o céu vermelho e tinge névoa e luz ambiente.
        /// </summary>
        private void BeginRedWorld()
        {
            if (redWorldActive) return;
            if (!redSkyReady)
            {
                // Não deu tempo de gerar em segundo plano: gera agora (engasga uma vez, mas não falha).
                var it = BuildRedSky();
                while (it.MoveNext()) { }
            }
            redWorldActive = true;
            redSkySpin = 0f;

            redWorldHidden.Clear();
            Transform creature = creatureGrabObject != null ? creatureGrabObject.transform : null;
            Transform player = playerController != null ? playerController.transform : null;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.enabled) continue;
                Transform t = r.transform;
                if (creature != null && t.IsChildOf(creature)) continue;
                if (player != null && t.IsChildOf(player)) continue;
                r.enabled = false;
                redWorldHidden.Add(r);
            }

            savedSkybox = RenderSettings.skybox;
            savedFog = RenderSettings.fog; savedFogColor = RenderSettings.fogColor;
            savedFogMode = RenderSettings.fogMode; savedFogDensity = RenderSettings.fogDensity;
            savedAmbientMode = RenderSettings.ambientMode; savedAmbient = RenderSettings.ambientLight;

            if (redSkyMat != null) RenderSettings.skybox = redSkyMat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.35f, 0.04f, 0.03f);
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // Quase neutro (pedido 2026-11-20): vermelho é o MUNDO ao fundo, não a criatura.
            RenderSettings.ambientLight = new Color(0.30f, 0.24f, 0.24f);

            // A câmera do sonho passa a limpar com o céu (o take em 3ª pessoa copia dela).
            redWorldCamera = ResolveDreamCamera();
            if (redWorldCamera != null)
            {
                savedRedClear = redWorldCamera.clearFlags;
                redWorldCamera.clearFlags = CameraClearFlags.Skybox;
            }
        }

        /// <summary>Troca o mundo na metade do giro dela (ver BeatTheGrab).</summary>
        private IEnumerator RedWorldMidTurn()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, grabTurnDuration) * 0.5f);
            BeginRedWorld();
        }

        /// <summary>As nuvens derivam: o céu gira devagar enquanto o mundo vermelho está no ar.</summary>
        private void UpdateRedWorld()
        {
            if (!redWorldActive || redSkyMat == null) return;
            redSkySpin += Time.deltaTime * 2.2f;
            redSkyMat.SetFloat("_Rotation", redSkySpin % 360f);
        }

        /// <summary>DEVOLVE O MUNDO (na piscada da queda, ou em qualquer saída do beat).</summary>
        private void EndRedWorld()
        {
            if (!redWorldActive) return;
            redWorldActive = false;
            foreach (var r in redWorldHidden) if (r != null) r.enabled = true;
            redWorldHidden.Clear();
            RenderSettings.skybox = savedSkybox;
            RenderSettings.fog = savedFog; RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogMode = savedFogMode; RenderSettings.fogDensity = savedFogDensity;
            RenderSettings.ambientMode = savedAmbientMode; RenderSettings.ambientLight = savedAmbient;
            if (redWorldCamera != null) redWorldCamera.clearFlags = savedRedClear;
            redWorldCamera = null;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TheDelivery.Narrative
{
    // Beat 5 - O FIM DO MUNDO (2026-11-20). Depois da queda, quando a Clear fecha os olhos no chão,
    // ela os abre de novo, rápido, e está outra vez no MUNDO VERMELHO, deitada numa planície sem fim.
    // No horizonte, uma LUA IMENSA vem na direção do planeta, acelerando, até tomar o céu e se chocar
    // com o chão: clarão, bola de fogo, destroços e uma parede de fogo e poeira que atravessa a planície
    // e engole a Clear num branco total. Daí o beat segue para o corte (TheCut), como antes.
    // Parte do NightmareDirector (partial).
    //
    // Tudo é montado em código, na hora: câmera própria (cópia da do sonho, com o pós-processo), chão,
    // lua (textura de crateras gerada), luz da lua, bola de fogo, parede de choque, destroços, sons.
    // A escala é de verdade (lua de 2,2 km de raio a 14 km, impacto a 4,5 km): a sensação de tamanho
    // vem da paralaxe e da velocidade, não de truque de lente.
    public sealed partial class NightmareDirector
    {
        [Header("Beat 5 - O fim do mundo (lua)")]
        [Tooltip("Liga a cena da lua depois que a Clear apaga no chão.")]
        [SerializeField] private bool moonEnding = true;
        [Tooltip("Ronco da aproximação (gerado no ElevenLabs).")]
        [SerializeField] private AudioClip moonRumbleSound;
        [Tooltip("Estrondo do impacto. Vazio: sintetizado em código.")]
        [SerializeField] private AudioClip moonImpactSound;
        [Tooltip("Respiração ofegante da Clear ao ver a lua (loop).")]
        [SerializeField] private AudioClip moonPantingSound;
        [Tooltip("Grito da Clear no choque.")]
        [SerializeField] private AudioClip moonScreamSound;
        [Tooltip("Vento de tempestade da chegada da lua (diferente do vento da queda).")]
        [SerializeField] private AudioClip moonWindSound;
        [Tooltip("Réquiem que toca enquanto a lua desce.")]
        [SerializeField] private AudioClip moonRequiem;

        // Ajustado no vídeo: com 2,2 km a 14 km ela parecia uma bola, não uma lua.
        private const float MoonRadius = 5200f;
        private const float MoonStartDistance = 34000f;
        // 6,8 km (era 9): a lua chega MAIS PERTO da Clear antes de bater (pedido 2026-11-20) — a frente
        // dela fica a ~2,4 km, tomando o céu inteiro.
        private const float MoonImpactDistance = 6800f;
        // O choque dura pelo menos isso antes do branco (pedido: "durar um pouco mais").
        private const float MoonImpactMinTime = 8f;
        private const float MoonApproachTime = 14f; // era 6,5: rápido demais (pedido 2026-11-20)

        private Texture2D moonTex, moonGroundTex, moonSoftDot;
        private bool moonTexReady;
        private readonly List<GameObject> moonSpawned = new List<GameObject>();
        private readonly List<Renderer> moonHiddenCreature = new List<Renderer>();
        private Camera moonCamera, moonHiddenDream;
        private Canvas moonFlashCanvas;
        private Image moonFlashImage;
        private AudioSource moonRumbleSource;
        private AudioSource moonPantSource;
        private AudioSource moonWindSource, moonMusicSource;
        private static AudioClip moonSynthImpact;

        // ---------------------------------------------------------------- texturas

        /// <summary>Gera as texturas da lua e do chão em segundo plano (chamado no Start).</summary>
        private void PrepareMoon()
        {
            if (moonTexReady) return;
            StartCoroutine(BuildMoonTextures());
        }

        private IEnumerator BuildMoonTextures()
        {
            // LUA: cinza com mares escuros (ruído de baixa frequência), relevo fino e CRATERAS
            // (miolo escuro, borda clara), em equirretangular.
            const int W = 1024, H = 512;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    float maria = Mathf.Lerp(Fbm(u * 4f + 7f, v * 2f + 3f), Fbm((u - 1f) * 4f + 7f, v * 2f + 3f), u);
                    float fine = Mathf.Lerp(Fbm(u * 40f, v * 20f), Fbm((u - 1f) * 40f, v * 20f), u);
                    float g = 0.62f + (fine - 0.5f) * 0.25f;
                    if (maria > 0.55f) g *= Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(0.55f, 0.62f, maria));
                    byte b = (byte)(Mathf.Clamp01(g) * 255f);
                    px[y * W + x] = new Color32(b, (byte)(b * 0.97f), (byte)(b * 0.93f), 255);
                }
                if (y % 64 == 63) yield return null;
            }
            var rng = new System.Random(77);
            for (int c = 0; c < 260; c++)
            {
                int cx = rng.Next(W), cy = rng.Next(H / 8, H - H / 8);
                float r = (float)(rng.NextDouble() < 0.85 ? 3 + rng.NextDouble() * 10 : 14 + rng.NextDouble() * 30);
                int ir = Mathf.CeilToInt(r * 1.3f);
                for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    if (d > 1.3f) continue;
                    int xx = ((cx + dx) % W + W) % W, yy = cy + dy;
                    if (yy < 0 || yy >= H) continue;
                    float k = d < 0.85f ? 0.78f + 0.18f * d : (d < 1.05f ? 1.12f : 1f + 0.12f * (1.3f - d));
                    Color32 p = px[yy * W + xx];
                    px[yy * W + xx] = new Color32((byte)Mathf.Min(255, p.r * k), (byte)Mathf.Min(255, p.g * k), (byte)Mathf.Min(255, p.b * k), 255);
                }
                if (c % 40 == 39) yield return null;
            }
            moonTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "PesadeloMoon", wrapMode = TextureWrapMode.Repeat };
            moonTex.SetPixels32(px); moonTex.Apply(true, true);

            // CHÃO: terra rachada escura, avermelhada, em ladrilho.
            const int G = 256;
            var gp = new Color32[G * G];
            for (int y = 0; y < G; y++)
            {
                for (int x = 0; x < G; x++)
                {
                    float u = x / (float)G, v = y / (float)G;
                    float n = Fbm(u * 6f, v * 6f);
                    float crack = Mathf.Abs(Mathf.PerlinNoise(u * 14f + 5f, v * 14f + 9f) - 0.5f) < 0.025f ? 0.45f : 1f;
                    float g = (0.18f + n * 0.22f) * crack;
                    gp[y * G + x] = new Color(g * 1.25f, g * 0.55f, g * 0.45f);
                }
                if (y % 64 == 63) yield return null;
            }
            moonGroundTex = new Texture2D(G, G, TextureFormat.RGBA32, true) { name = "PesadeloGround", wrapMode = TextureWrapMode.Repeat };
            moonGroundTex.SetPixels32(gp); moonGroundTex.Apply(true, true);

            // PONTO SUAVE para as partículas de poeira/brasa.
            const int D = 64;
            var dp = new Color32[D * D];
            for (int y = 0; y < D; y++)
            for (int x = 0; x < D; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(D / 2f, D / 2f)) / (D / 2f);
                byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255f);
                dp[y * D + x] = new Color32(255, 255, 255, a);
            }
            moonSoftDot = new Texture2D(D, D, TextureFormat.RGBA32, false) { name = "PesadeloDot" };
            moonSoftDot.SetPixels32(dp); moonSoftDot.Apply(false, true);
            moonTexReady = true;
            yield return PrewarmMoonShaders();
        }

        /// <summary>
        /// AQUECE OS SHADERS (ajustado no vídeo: o chão e a lua apareciam CIANO por 2 s — o Editor
        /// compila o shader na primeira vez que ele é desenhado). Cada material da cena é desenhado uma
        /// vez num quadradinho de 1 mm grudado na câmera do sonho, 1,5 s, muito antes de ser preciso.
        /// </summary>
        private IEnumerator PrewarmMoonShaders()
        {
            Camera cam = ResolveDreamCamera();
            if (cam == null) yield break;
            var root = new GameObject("MoonShaderPrewarm");
            root.transform.SetParent(cam.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.05f);
            var mats = new[] { NewSoftParticleMat(true), NewLit(Color.white, moonTex), NewLit(Color.white, moonGroundTex), NewUnlit(Color.white), NewSoftParticleMat(), NewSoftParticleMat(true), new Material(Shader.Find("Universal Render Pipeline/Particles/Lit")), NewAdditiveMat(moonSoftDot) };
            for (int i = 0; i < mats.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(root.transform, false);
                q.transform.localPosition = new Vector3(i * 0.0005f, 0f, 0f);
                q.transform.localScale = Vector3.one * 0.0004f;
                q.GetComponent<Renderer>().sharedMaterial = mats[i];
            }
            yield return new WaitForSeconds(1.5f);
            Destroy(root);
        }

        // ---------------------------------------------------------------- a cena

        private IEnumerator MoonApocalypse()
        {
            if (!moonEnding || playerController == null)
                yield break;
            if (!moonTexReady)
            {
                var it = BuildMoonTextures();
                while (it.MoveNext()) { }
            }

            // --- 0. Com o olho FECHADO: troca o mundo e monta o palco ---
            BeginRedWorld();
            foreach (var r in creatureGrabObject != null ? creatureGrabObject.GetComponentsInChildren<Renderer>() : new Renderer[0])
                if (r.enabled) { r.enabled = false; moonHiddenCreature.Add(r); }
            // névoa longa: o vermelho ainda tinge o horizonte, mas a lua a 14 km aparece
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 70000f;
            RenderSettings.ambientLight = new Color(0.16f, 0.05f, 0.04f); // o lado escuro da lua fica vermelho-sangue, não cinza

            Transform body = playerController.transform;
            Vector3 fwd = body.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float groundY = body.position.y;
            Vector3 eye = new Vector3(body.position.x, groundY + 0.55f, body.position.z); // cabeça erguida do chão

            Camera dream = ResolveDreamCamera();
            var camGo = new GameObject("MoonCamera");
            moonSpawned.Add(camGo);
            moonCamera = camGo.AddComponent<Camera>();
            if (dream != null)
            {
                moonCamera.CopyFrom(dream);
                var src = dream.GetUniversalAdditionalCameraData();
                var dst = moonCamera.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = camGo.transform;
                moonCamera.depth = dream.depth + 6f;
                dream.enabled = false;
                moonHiddenDream = dream;
            }
            moonCamera.clearFlags = CameraClearFlags.Skybox;
            moonCamera.fieldOfView = 62f;
            moonCamera.nearClipPlane = 0.3f;
            moonCamera.farClipPlane = 90000f;
            var bgGo = new GameObject("MoonBarsBackground"); moonSpawned.Add(bgGo);
            var bg = bgGo.AddComponent<Camera>();
            bg.clearFlags = CameraClearFlags.SolidColor; bg.backgroundColor = Color.black; bg.cullingMask = 0;
            bg.depth = moonCamera.depth - 1f;
            CinemaBegin(moonCamera, 0f, 0f);
            BeginMoonCinema(eye, fwd);
            BeginMoonVfx(eye, fwd);
            // A CLEAR DEITADA (só aparece no plano geral): o modelo inteiro de costas no chão, cabeça onde
            // fica o olho dela, pés para a lua.
            SpawnClearActor(eye, groundY, fwd);

            // CHÃO: planície de 60 km
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "MoonGround"; moonSpawned.Add(ground);
            Destroy(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(eye.x, groundY, eye.z);
            ground.transform.localScale = new Vector3(6000f, 1f, 6000f);
            var gMat = NewLit(new Color(0.9f, 0.85f, 0.85f), moonGroundTex);
            gMat.mainTextureScale = new Vector2(2400f, 2400f);
            ground.GetComponent<Renderer>().sharedMaterial = gMat;

            // LUA
            var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "MoonOfTheEnd"; moonSpawned.Add(moon);
            Destroy(moon.GetComponent<Collider>());
            moon.transform.localScale = Vector3.one * MoonRadius * 2f;
            moon.GetComponent<Renderer>().sharedMaterial = NewLit(new Color(0.72f, 0.68f, 0.64f), moonTex);
            moon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // LUZ DA LUA: um sol baixo vindo de trás-esquerda da Clear — a lua aparece quase cheia, com
            // a borda direita em sombra (o volume vem daí).
            var sunGo = new GameObject("MoonSun"); moonSpawned.Add(sunGo);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.84f, 0.72f); sun.intensity = 2.4f;
            sun.shadows = LightShadows.None;
            // Ajustado no vídeo: de trás da Clear a lua saía cheia e chapada. Luz de LADO: um terço
            // dela no escuro, e o volume aparece.
            sunGo.transform.rotation = Quaternion.LookRotation((right * 1f + fwd * 0.5f + Vector3.down * 0.15f).normalized);

            Vector3 impact = new Vector3(eye.x, groundY, eye.z) + fwd * MoonImpactDistance;
            // A LUA VEM DE CIMA (pedido 2026-11-20: a origem estava baixa demais): começa alta no céu, a
            // ~48° de elevação, e desce na diagonal até o chão.
            Vector3 moonFrom = eye + Quaternion.AngleAxis(-48f, right) * fwd * MoonStartDistance;
            Vector3 moonTo = impact + Vector3.up * (MoonRadius * 0.82f) + fwd * (MoonRadius * 0.15f);
            moon.transform.position = moonFrom;

            // RONCO
            moonRumbleSource = gameObject.AddComponent<AudioSource>();
            moonRumbleSource.spatialBlend = 0f; moonRumbleSource.loop = true; moonRumbleSource.volume = 0f; // em loop: o choque agora passa do fim do clipe
            if (moonRumbleSound != null) { moonRumbleSource.clip = moonRumbleSound; moonRumbleSource.Play(); }

            // --- 1. Ela abre os olhos, RÁPIDO, deitada de lado ---
            Quaternion lying = Quaternion.LookRotation(fwd) * Quaternion.Euler(-1f, 0f, 16f);
            Quaternion lookNow = lying;
            camGo.transform.SetPositionAndRotation(eye, lying);
            for (float t = 0f; t < 0.14f; t += Time.deltaTime) { SetBlink(1f - t / 0.14f); yield return null; }
            SetBlink(0f);
            // O VENTO DA LUA (tempestade, diferente do assobio da queda livre) e o RÉQUIEM: os dois entram
            // quando ela abre os olhos e crescem enquanto a lua desce. Pedido 2026-11-20.
            moonWindSource = gameObject.AddComponent<AudioSource>();
            moonWindSource.spatialBlend = 0f; moonWindSource.loop = true; moonWindSource.volume = 0f; moonWindSource.clip = moonWindSound;
            if (moonWindSound != null) moonWindSource.Play();
            moonMusicSource = gameObject.AddComponent<AudioSource>();
            moonMusicSource.spatialBlend = 0f; moonMusicSource.loop = false; moonMusicSource.volume = 0f; moonMusicSource.clip = moonRequiem;
            // o réquiem entra em 1,2 s (junto da respiração): o auge do coral (12–13 s do clipe) cai logo
            // antes do toque e o fim dele vira o silêncio antes do golpe

            // --- 2. A lua vem ---
            // MAIS DRAMÁTICA E MAIS LENTA (pedido 2026-11-20): 14 s em vez de 6,5. Ela sai da névoa do
            // horizonte quase parada, majestosa, e só acelera no fim. Conforme entra na atmosfera, a
            // barriga dela incandesce (luz laranja embaixo) e pedaços em chamas se soltam e caem à frente.
            // Meio segundo antes do toque o ronco CAI — o silêncio antes do golpe.
            var glowGo = new GameObject("MoonEntryGlow"); moonSpawned.Add(glowGo);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.45f, 0.12f);
            glow.range = 14000f; glow.shadows = LightShadows.None; glow.intensity = 0f;
            var meteors = SpawnMeteors();
            // A RESPIRAÇÃO OFEGANTE começa quando ela VÊ a lua (depois de erguer a cabeça) e cresce.
            var pant = gameObject.AddComponent<AudioSource>();
            pant.spatialBlend = 0f; pant.loop = true; pant.volume = 0f; pant.clip = moonPantingSound;
            moonPantSource = pant;
            float shakeBoost = 0f;
            for (float t = 0f; t < MoonApproachTime; t += Time.deltaTime)
            {
                float k = t / MoonApproachTime;
                float e = Mathf.Pow(k, 1.9f);
                Vector3 mp = Vector3.Lerp(moonFrom, moonTo, e);
                moon.transform.position = mp;
                moon.transform.rotation = Quaternion.AngleAxis(t * 0.8f, right) * Quaternion.AngleAxis(20f, Vector3.up);

                // incandescência embaixo: cresce no último terço
                float heat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, k));
                glowGo.transform.position = mp + Vector3.down * MoonRadius * 1.05f - fwd * MoonRadius * 0.55f;
                glow.intensity = heat * 3e7f * (0.85f + 0.15f * Mathf.PerlinNoise(t * 3f, 4f));
                // os pedaços saem da barriga dela, cada vez mais
                meteors.transform.position = mp + Vector3.down * MoonRadius * 0.9f - fwd * MoonRadius * 0.4f;
                var mem = meteors.emission; mem.rateOverTime = Mathf.Lerp(0f, 22f, heat);

                // ela ergue a cabeça (1,2 s) e o olhar sobe devagar atrás da lua conforme ela toma o céu
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.4f) / 1.2f));
                Vector3 horizon = eye + Quaternion.AngleAxis(-3f, right) * fwd * 1000f;
                // o olhar sobe até ela lá no alto e desce junto enquanto ela cai
                // (ajustado no vídeo: com 0,55 no fim o chão saía do quadro e a colisão perdia o planeta)
                // mistura de DIREÇÕES (misturar posições deixava a lua, muito mais longe, mandar no olhar)
                Vector3 lookDir = Vector3.Slerp((horizon - eye).normalized, (mp - eye).normalized, Mathf.Lerp(0.9f, 0.5f, Mathf.SmoothStep(0f, 1f, k)));
                Vector3 target = eye + lookDir * 1000f;
                Quaternion want = Quaternion.Slerp(lying, Quaternion.LookRotation(target - eye) * Quaternion.Euler(0f, 0f, 1.5f), lift);
                lookNow = Quaternion.Slerp(lookNow, want, 1f - Mathf.Exp(-3.5f * Time.deltaTime));

                shakeBoost = Mathf.Max(shakeBoost, heat * 0.4f);
                CinemaKick(shakeBoost);
                CinemaUpdate(moonCamera, mp, out Vector3 sp, out Quaternion sr);
                camGo.transform.SetPositionAndRotation(AboveGround(eye + sp * (1f + 6f * heat), groundY), lookNow * sr);
                // faixas de cinema entram quando ela vê a lua; a lente fecha devagar (teleobjetiva)
                SetMoonBars(Mathf.SmoothStep(0f, 1f, (t - 1f) / 2.2f));
                moonCamera.fieldOfView = Mathf.Lerp(62f, 46f, Mathf.SmoothStep(0f, 1f, k));
                // MONTAGEM (2026-11-20, "mais cinematográfico"): a aproximação deixa de ser um plano só.
                //   0–3,2 s   olhos dela (acima)
                //   3,2–6,8 s PLANO GERAL: longe e baixo, a Clear minúscula deitada e a lua colossal — escala
                //   6,8–10 s  RASANTE: rente ao chão, deslizando para a lua, teleobjetiva
                //   10–14 s   olhos dela de novo (acima)
                bool wide = t >= 3.2f && t < 6.8f, glide = t >= 6.8f && t < 10f;
                SetClearActorVisible(wide);
                if (wide)
                {
                    float st = t - 3.2f;
                    // perto o bastante para a Clear ser o primeiro plano (a 260 m ela sumia)
                    // atrás e de LADO, baixo: o corpo dela aparece deitado de comprido (de trás da cabeça ela
                    // encolhia num ponto) e a lua imensa à frente
                    Vector3 p = eye - fwd * 2.6f + right * 2.8f + Vector3.up * 0.75f - fwd * (st * 0.25f) + right * (st * 0.12f);
                    Vector3 toClear = (new Vector3(eye.x, groundY + 0.2f, eye.z) + fwd * 0.8f - p).normalized;
                    // (ajustado no vídeo: com 0,62 só se via céu — a Clear e o chão saíam do quadro)
                    Vector3 d = Vector3.Slerp(toClear, (mp - p).normalized, 0.46f); // Clear embaixo, lua no alto
                    camGo.transform.SetPositionAndRotation(p + sp * 0.6f, Quaternion.LookRotation(d) * sr);
                    moonCamera.fieldOfView = 70f;
                }
                else if (glide)
                {
                    float st = t - 6.8f;
                    Vector3 p = eye - right * 45f + fwd * (30f + st * 22f) + Vector3.up * 1.6f;
                    Vector3 ahead = Quaternion.AngleAxis(-2f, right) * fwd;
                    Vector3 d = Vector3.Slerp(ahead, (mp - p).normalized, 0.55f);
                    camGo.transform.SetPositionAndRotation(AboveGround(p + sp * 1.5f, groundY), Quaternion.LookRotation(d) * sr);
                    moonCamera.fieldOfView = 60f;
                }
                // PLANO HOLANDÊS: no último trecho pelos olhos dela a câmera vai inclinando — desequilíbrio
                if (!wide && !glide && t >= 10f) camGo.transform.rotation *= Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -7f, Mathf.SmoothStep(0f, 1f, (t - 10f) / 4f)));
                UpdateMoonHalo(mp, moonCamera, heat);
                float hush = Mathf.InverseLerp(MoonApproachTime - 0.6f, MoonApproachTime - 0.1f, t);
                moonWindSource.volume = Mathf.Lerp(0.08f, 0.6f, Mathf.SmoothStep(0f, 1f, k));
                if (moonRequiem != null && !moonMusicSource.isPlaying && t >= 1.2f && t < 2f) moonMusicSource.Play();
                moonMusicSource.volume = 0.9f; // a própria música cresce
                moonRumbleSource.volume = Mathf.Lerp(Mathf.Lerp(0.08f, 1f, Mathf.SmoothStep(0f, 1f, k)), 0.15f, hush);
                if (pant.clip != null)
                {
                    if (!pant.isPlaying && t >= 1.4f) pant.Play();
                    pant.volume = Mathf.Lerp(0f, 0.95f, Mathf.Clamp01((t - 1.4f) / 1.2f));
                }
                yield return null;
            }
            var stopMeteors = meteors.emission; stopMeteors.rateOverTime = 0f;

            // --- 3. O CHOQUE, em câmera lenta ---
            // Partículas a meia velocidade, clarão longo (1,4 s), a lua afundando em 8 s e a parede de
            // poeira levando ~4 s para chegar na Clear.
            EnsureMoonFlash();
            PlaySfx(moonImpactSound != null ? moonImpactSound : SynthMoonImpact(), 1f);
            // ELA GRITA no choque; a respiração para.
            if (moonPantSource != null) moonPantSource.Stop();
            PlaySfx(moonScreamSound, 1f);
            var lightGo = new GameObject("MoonImpactLight"); moonSpawned.Add(lightGo);
            // o fogo nasce em VOLTA da base da lua (o ponto de impacto fica dentro dela)
            Vector3 ring = new Vector3(moonTo.x, groundY, moonTo.z);
            lightGo.transform.position = ring - fwd * (MoonRadius * 1.15f) + Vector3.up * 900f;
            var fireLight = lightGo.AddComponent<Light>();
            fireLight.type = LightType.Point; fireLight.color = new Color(1f, 0.55f, 0.2f);
            // alcance realista (ajustado no vídeo: com 60 km o URP calculava a luz só num RETÂNGULO da tela)
            fireLight.range = 14000f; fireLight.shadows = LightShadows.None;
            SpawnFireCloud(ring);
            SpawnDustWall(ring);
            SpawnDebris(ring);
            SpawnRockFragments(ring, eye, fwd, right);
            SpawnImpactVfx(ring, eye, fwd, right, groundY);
            const float wallSpeed = 775f * ImpactSlowMo;
            float reach = Vector3.Distance(new Vector3(eye.x, groundY, eye.z), ring) - MoonRadius * 0.95f - 300f;
            Vector3 moonSink = moonTo + Vector3.down * MoonRadius * 0.35f;
            bool whiteStarted = false; float whiteT = 0f;
            for (float t = 0f; ; t += Time.deltaTime)
            {
                float flash = Mathf.Clamp01(1f - t / 1.4f);
                moon.transform.position = Vector3.Lerp(moonTo, moonSink, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 8f)));
                glow.intensity = Mathf.Lerp(3e7f, 0f, Mathf.Clamp01(t / 1.5f));
                fireLight.intensity = Mathf.Lerp(0f, 1.5e7f, Mathf.Clamp01(t / 0.3f)) * (0.85f + 0.15f * Mathf.PerlinNoise(t * 4f, 1f));

                // A TELA TREME forte o choque inteiro (só alivia um pouco no fim)
                CinemaKick(Mathf.Lerp(1f, 0.75f, Mathf.Clamp01(t / MoonImpactMinTime)));
                CinemaUpdate(moonCamera, impact, out Vector3 sp, out Quaternion sr);
                camGo.transform.SetPositionAndRotation(AboveGround(eye + sp * 18f, groundY), lookNow * sr);
                // no choque a lente ABRE de uma vez, a imagem racha em cor e a exposição estoura
                moonCamera.fieldOfView = t < 0.25f ? Mathf.Lerp(46f, 74f, t / 0.25f) : Mathf.Lerp(74f, 64f, Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 2f));
                if (moonChroma != null) moonChroma.intensity.value = Mathf.Lerp(1f, 0.18f, Mathf.Clamp01(t / 2.5f));
                if (moonGrade != null) moonGrade.postExposure.value = Mathf.Lerp(2.1f, 0.7f, Mathf.Clamp01(t / 1.2f));
                // O IMPACTO VISTO DE LONGE, DE LADO (primeiros 2,6 s): a lua batendo no horizonte, o anel de
                // poeira se abrindo e o fogo subindo; depois volta aos olhos dela para a poeira chegar.
                bool sideShot = t < 2.6f;
                if (sideShot)
                {
                    Vector3 p = ring + right * 15000f - fwd * 6000f + Vector3.up * 500f;
                    Vector3 look = ring + Vector3.up * (MoonRadius * 0.5f) - fwd * 1500f;
                    camGo.transform.SetPositionAndRotation(p + sp * 4f, Quaternion.LookRotation(look - p) * sr);
                    moonCamera.fieldOfView = 36f;
                    flash *= 0.35f; // o clarão não pode apagar o plano mais importante
                }
                // luzes de fogo só nos planos perto dela: com a câmera a 15 km o URP calculava a luz em blocos
                // (um retângulo aparecia no chão)
                fireLight.enabled = !sideShot; glow.enabled = !sideShot;
                UpdateShockRing(ring, groundY, t);
                UpdateImpactVfx(ring, groundY, fwd, moonCamera, t);
                UpdateStreak(t);
                UpdateMoonHalo(moon.transform.position, moonCamera, Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / 2f)));
                moonRumbleSource.volume = Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(t / 3f));
                moonMusicSource.volume = Mathf.Lerp(0.9f, 0f, Mathf.Clamp01(t / 2f));

                // a frente da parede de poeira chega nela: BRANCO total e fim
                if (!whiteStarted && t * wallSpeed >= reach && t >= MoonImpactMinTime) whiteStarted = true;
                if (whiteStarted) whiteT += Time.deltaTime;
                float white = whiteStarted ? Mathf.Clamp01(whiteT / 0.5f) : 0f;
                moonFlashImage.color = Color.Lerp(new Color(1f, 0.78f, 0.5f, flash * 0.8f), new Color(1f, 0.97f, 0.92f, 1f), white);
                moonFlashCanvas.gameObject.SetActive(flash > 0f || white > 0f);
                if (whiteStarted && whiteT > 1.0f) break;
                yield return null;
            }
            if (moonRumbleSource != null) moonRumbleSource.Stop();
            if (moonWindSource != null) moonWindSource.Stop();
            if (moonMusicSource != null) moonMusicSource.Stop();
        }

        /// <summary>Quanto o impacto anda em câmera lenta (velocidade de simulação das partículas).</summary>
        private const float ImpactSlowMo = 0.5f;

        /// <summary>
        /// OS PEDAÇOS EM CHAMAS que se soltam da lua na entrada: riscos esticados caindo à frente dela
        /// em direção ao chão. A taxa é controlada pela cena (começa em zero).
        /// </summary>
        private ParticleSystem SpawnMeteors()
        {
            var ps = NewBurst("MoonMeteors", Vector3.zero, Quaternion.LookRotation(Vector3.down));
            var main = ps.main;
            main.loop = true; main.duration = 5f; main.maxParticles = 400;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(900f, 1800f);
            main.startSize = new ParticleSystem.MinMaxCurve(120f, 320f);
            main.gravityModifier = 30f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = MoonRadius * 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.3f),
                              new GradientColorKey(new Color(0.3f, 0.06f, 0.03f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.lengthScale = 4f; pr.velocityScale = 0.15f;
            var em = ps.emission; em.rateOverTime = 0f;
            ps.Play();
            return ps;
        }


        // ---------------------------------------------------------------- acabamento de cinema
        // (2026-11-20, "mais cinematográfico"): faixas de tela larga, tratamento de cor de película,
        // brasas em primeiro plano e o pulso de cor/exposição no impacto.
        private Canvas moonBarsCanvas;
        private RectTransform moonBarTop, moonBarBottom;
        private ChromaticAberration moonChroma;
        private ColorAdjustments moonGrade;

        /// <summary>A câmera nunca desce abaixo da superfície (o tremor do choque a enterrava).</summary>
        private static Vector3 AboveGround(Vector3 p, float groundY) => new Vector3(p.x, Mathf.Max(p.y, groundY + 0.3f), p.z);

        private void BeginMoonCinema(Vector3 eye, Vector3 fwd)
        {
            // TRATAMENTO DE COR: contraste, bloom no fogo e na barriga incandescente, grão e vinheta.
            var gradeGo = new GameObject("MoonGrade"); moonSpawned.Add(gradeGo);
            var dream = FindAnyObjectByType<UnityEngine.Rendering.Volume>();
            if (dream != null) gradeGo.layer = dream.gameObject.layer;
            var vol = gradeGo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true; vol.priority = 1001f;
            var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(1.4f); bloom.threshold.Override(0.8f); bloom.scatter.Override(0.75f);
            var grain = prof.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Medium3); grain.intensity.Override(0.45f); grain.response.Override(0.7f);
            moonGrade = prof.Add<ColorAdjustments>(true); moonGrade.contrast.Override(24f); moonGrade.saturation.Override(12f); moonGrade.postExposure.Override(0f);
            moonChroma = prof.Add<ChromaticAberration>(true); moonChroma.intensity.Override(0.12f);
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.4f); vig.smoothness.Override(0.55f);
            // CURVA DE CINEMA e contraste de cor de filme: sombras frias (azul-petróleo), altas luzes âmbar;
            // leve distorção de lente anamórfica nas bordas.
            var tone = prof.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var split = prof.Add<SplitToning>(true); split.shadows.Override(new Color(0.3f, 0.45f, 0.55f)); split.highlights.Override(new Color(1f, 0.7f, 0.42f)); split.balance.Override(-20f);
            var lens = prof.Add<LensDistortion>(true); lens.intensity.Override(-0.14f);
            moonGrade.postExposure.Override(0.7f); // o ACES escurece: compensa
            vol.sharedProfile = prof;

            // FAIXAS DE CINEMA (2,39:1): entram devagar quando ela vê a lua.
            if (moonBarsCanvas == null)
            {
                var root = new GameObject("PesadeloMoonBars");
                root.transform.SetParent(transform, false);
                moonBarsCanvas = root.AddComponent<Canvas>();
                moonBarsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                moonBarsCanvas.sortingOrder = 489; // abaixo do clarão/branco (492)
                moonBarTop = NewBar(root.transform, true);
                moonBarBottom = NewBar(root.transform, false);
            }
            moonBarsCanvas.gameObject.SetActive(false); // as faixas agora são o recorte da câmera (SetMoonBars)
            SetMoonBars(0f);

            // BRASAS em primeiro plano: fagulhas subindo devagar perto dela — profundidade na imagem.
            var ps = NewBurst("MoonEmbers", eye + fwd * 14f + Vector3.up * 2f, Quaternion.identity);
            var main = ps.main;
            main.loop = true; main.duration = 5f; main.maxParticles = 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.8f, 0.4f));
            var em = ps.emission; em.rateOverTime = 28f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30f, 6f, 26f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.9f);
            vel.y = new ParticleSystem.MinMaxCurve(0.4f, 1.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
        }

        private static RectTransform NewBar(Transform parent, bool top)
        {
            var go = new GameObject(top ? "BarTop" : "BarBottom");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f); rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>(); img.color = Color.black; img.raycastTarget = false;
            return rt;
        }

        /// <summary>
        /// 0 = sem faixas, 1 = tela larga 2,39:1. Feito como no cinema: a CÂMERA filma só a faixa do meio
        /// (viewport) e uma câmera de fundo pinta o resto de preto. As faixas desenhadas por cima (canvas)
        /// se misturavam com a poeira no gravador e viravam um retângulo claro na tela.
        /// </summary>
        private void SetMoonBars(float k)
        {
            if (moonCamera != null)
            {
                float hh = Mathf.Lerp(0f, 0.12f, Mathf.Clamp01(k));
                moonCamera.rect = new Rect(0f, hh, 1f, 1f - 2f * hh);
                return;
            }
            if (moonBarTop == null) return;
            float h = Mathf.Lerp(0f, 0.12f, Mathf.Clamp01(k));
            moonBarTop.anchorMin = new Vector2(0f, 1f - h); moonBarTop.anchorMax = Vector2.one;
            moonBarBottom.anchorMin = Vector2.zero; moonBarBottom.anchorMax = new Vector2(1f, h);
            moonBarTop.offsetMin = moonBarTop.offsetMax = moonBarBottom.offsetMin = moonBarBottom.offsetMax = Vector2.zero;
        }


        // ---------------------------------------------------------------- fragmentos de rocha
        // (2026-11-20, "alguns fragmentos de pedra podem voar no momento da colisão"): rochas 3D
        // irregulares (esfera deformada por ruído, 3 formatos), girando, lançadas em arco do lado da
        // lua voltado para a Clear — as grandes passam lá no alto, algumas menores cruzam perto da lente.
        private Mesh[] moonRockMeshes;

        private Mesh[] RockMeshes()
        {
            if (moonRockMeshes != null) return moonRockMeshes;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            moonRockMeshes = new Mesh[3];
            for (int m = 0; m < 3; m++)
            {
                var v = src.vertices;
                Vector3 squash = new Vector3(1f + 0.35f * m, 0.6f + 0.15f * m, 0.9f - 0.1f * m);
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = v[i].normalized;
                    float n = Mathf.PerlinNoise(p.x * 2.1f + m * 7f, p.y * 2.1f + p.z * 1.7f) * 0.55f
                            + Mathf.PerlinNoise(p.z * 5.3f + m * 3f, p.x * 5.3f) * 0.2f;
                    // facetas: arredonda o raio em degraus, rocha quebrada e não pedra de rio
                    float r = Mathf.Round((0.65f + n) * 7f) / 7f;
                    v[i] = Vector3.Scale(p * r, squash) * 0.5f;
                }
                var mesh = new Mesh { name = "MoonRock" + m };
                mesh.vertices = v; mesh.triangles = src.triangles; mesh.uv = src.uv;
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                moonRockMeshes[m] = mesh;
            }
            return moonRockMeshes;
        }

        private void SpawnRockFragments(Vector3 ring, Vector3 eye, Vector3 fwd, Vector3 right)
        {
            var go = new GameObject("MoonRocks"); moonSpawned.Add(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.simulationSpeed = ImpactSlowMo;
            main.startRotation3D = true;
            const float gravity = 15f; // ×9,81: arco visível em quilômetros
            main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            rot.y = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh;
            pr.SetMeshes(RockMeshes());
            pr.alignment = ParticleSystemRenderSpace.World;
            pr.maxParticleSize = 20f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            mat.SetTexture("_BaseMap", moonGroundTex);
            mat.SetColor("_BaseColor", new Color(0.32f, 0.26f, 0.23f)); // rocha escura (ajustado: clara demais lia como pepita)
            // rocha ainda quente: brasa fraca na superfície
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.22f, 0.05f, 0.01f));
            pr.sharedMaterial = mat;
            ps.Play();

            Vector3 g = Physics.gravity * gravity;
            var rng = new System.Random(2026);
            float R() => (float)rng.NextDouble();
            for (int i = 0; i < 90; i++)
            {
                bool near = i < 14; // as poucas que cruzam perto da lente
                // sai do lado da lua voltado para a Clear (±75°), da base até ~1,5 km de altura
                float ang = Mathf.Lerp(-75f, 75f, R()) * Mathf.Deg2Rad;
                Vector3 outDir = -fwd * Mathf.Cos(ang) + right * Mathf.Sin(ang);
                Vector3 from = ring + outDir * MoonRadius * 0.95f + Vector3.up * Mathf.Lerp(150f, 1500f, R());
                Vector3 to = near
                    ? eye + right * Mathf.Lerp(-260f, 260f, R()) + Vector3.up * Mathf.Lerp(25f, 160f, R()) - fwd * Mathf.Lerp(50f, 400f, R())
                    : eye + right * Mathf.Lerp(-4500f, 4500f, R()) + Vector3.up * Mathf.Lerp(250f, 2200f, R()) + fwd * Mathf.Lerp(-2500f, 1200f, R());
                float T = near ? Mathf.Lerp(2.2f, 3.2f, R()) : Mathf.Lerp(2.5f, 4.5f, R()); // em tempo de simulação
                Vector3 vel = (to - from) / T - 0.5f * g * T;
                var ep = new ParticleSystem.EmitParams
                {
                    position = from,
                    velocity = vel,
                    startLifetime = T + 2.5f,
                    startSize3D = Vector3.one * (near ? Mathf.Lerp(3f, 12f, R()) : Mathf.Lerp(40f, 220f, R())),
                    rotation3D = new Vector3(R() * 360f, R() * 360f, R() * 360f),
                    startColor = Color.white,
                    applyShapeToPosition = false
                };
                ps.Emit(ep, 1);
            }
        }

        private GameObject moonClearActor;
        private Renderer[] moonClearRenderers;

        private void SpawnClearActor(Vector3 eye, float groundY, Vector3 fwd)
        {
            if (grabTakeModel == null) return;
            Vector3 feet = new Vector3(eye.x, groundY + 0.12f, eye.z) + fwd * 1.55f;
            moonClearActor = Instantiate(grabTakeModel, feet, Quaternion.LookRotation(Vector3.up, -fwd));
            moonClearActor.name = "MoonClearLying";
            moonSpawned.Add(moonClearActor);
            foreach (var c in moonClearActor.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var an in moonClearActor.GetComponentsInChildren<Animator>(true)) an.speed = 0f;
            moonClearRenderers = moonClearActor.GetComponentsInChildren<Renderer>(true);
            SetClearActorVisible(false);
        }

        private void SetClearActorVisible(bool on)
        {
            if (moonClearRenderers == null) return;
            foreach (var r in moonClearRenderers) if (r != null && r.enabled != on) r.enabled = on;
        }


        // ---------------------------------------------------------------- VFX de cinema (2026-11-20)
        private Transform moonHalo;
        private Material moonHaloMat;
        private Transform moonShockRing;
        private Material moonShockMat;
        private Image moonStreak;
        private static Texture2D moonRingTex, moonStreakTex;

        /// <summary>Material de partícula ADITIVO (brilho que soma luz) com a textura dada.</summary>
        private Material NewAdditiveMat(Texture tex)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = 3000;
            return m;
        }

        private static Texture2D RingTexture()
        {
            if (moonRingTex != null) return moonRingTex;
            const int S = 256;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                // anel fino e brilhante com um rastro macio para dentro
                float a = Mathf.Exp(-Mathf.Pow((d - 0.93f) / 0.03f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((d - 0.85f) / 0.1f, 2f));
                byte b = (byte)(Mathf.Clamp01(a) * 255f);
                px[y * S + x] = new Color32(255, 255, 255, b);
            }
            moonRingTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "MoonShockRing", wrapMode = TextureWrapMode.Clamp };
            moonRingTex.SetPixels32(px); moonRingTex.Apply(true, true);
            return moonRingTex;
        }

        private static Texture2D StreakTexture()
        {
            if (moonStreakTex != null) return moonStreakTex;
            const int W = 256, H = 32;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = Mathf.Abs(x / (W - 1f) - 0.5f) * 2f, v = Mathf.Abs(y / (H - 1f) - 0.5f) * 2f;
                float a = Mathf.Exp(-v * v * 9f) * Mathf.Pow(1f - u, 1.6f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            moonStreakTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "MoonStreak", wrapMode = TextureWrapMode.Clamp };
            moonStreakTex.SetPixels32(px); moonStreakTex.Apply(false, true);
            return moonStreakTex;
        }

        /// <summary>Monta os efeitos: cinza caindo, halo da lua, anel de choque (oculto) e o risco anamórfico.</summary>
        private void BeginMoonVfx(Vector3 eye, Vector3 fwd)
        {
            // CINZA CAINDO: flocos cinzentos flutuando pela planície (vale para todos os planos)
            var ash = NewBurst("MoonAsh", eye + fwd * 25f + Vector3.up * 12f, Quaternion.identity);
            var main = ash.main;
            main.loop = true; main.duration = 5f; main.maxParticles = 4000; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.42f); // (3–11 cm sumiam no escuro)
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.78f, 0.74f, 0.9f), new Color(0.55f, 0.5f, 0.48f, 0.9f));
            var em = ash.emission; em.rateOverTime = 420f;
            var sh = ash.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60f, 22f, 90f);
            var vel = ash.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.6f, -0.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var noise = ash.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.3f;
            ash.Play();

            // HALO DA LUA: brilho de atmosfera incandescente em volta dela (aditivo, de frente para a câmera)
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "MoonHalo"; moonSpawned.Add(halo);
            Destroy(halo.GetComponent<Collider>());
            moonHaloMat = NewAdditiveMat(moonSoftDot);
            moonHaloMat.SetColor("_BaseColor", new Color(1f, 0.4f, 0.12f, 0f));
            halo.GetComponent<Renderer>().sharedMaterial = moonHaloMat;
            halo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            moonHalo = halo.transform;

            // ANEL DE CHOQUE no chão (aparece no impacto)
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "MoonShockRing"; moonSpawned.Add(ring);
            Destroy(ring.GetComponent<Collider>());
            moonShockMat = NewAdditiveMat(RingTexture());
            moonShockMat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.18f, 0f));
            ring.GetComponent<Renderer>().sharedMaterial = moonShockMat;
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.SetActive(false);
            moonShockRing = ring.transform;

            // RISCO ANAMÓRFICO: linha de luz horizontal de lente, no canvas do clarão (abaixo do branco)
            EnsureMoonFlash();
            if (moonStreak == null)
            {
                var go = new GameObject("Streak");
                go.transform.SetParent(moonFlashCanvas.transform, false);
                go.transform.SetAsFirstSibling();
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(-0.1f, 0.5f); rt.anchorMax = new Vector2(1.1f, 0.5f);
                rt.sizeDelta = new Vector2(0f, 60f); rt.anchoredPosition = Vector2.zero;
                moonStreak = go.AddComponent<Image>();
                moonStreak.sprite = Sprite.Create(StreakTexture(), new Rect(0, 0, 256, 32), new Vector2(0.5f, 0.5f));
                moonStreak.raycastTarget = false;
                moonStreak.color = new Color(1f, 0.8f, 0.55f, 0f);
            }
        }

        /// <summary>Halo de frente para a câmera, do tamanho da lua ×1,5, ganhando força com o calor.</summary>
        private void UpdateMoonHalo(Vector3 moonPos, Camera cam, float heat)
        {
            if (moonHalo == null || cam == null) return;
            moonHalo.position = moonPos + (cam.transform.position - moonPos).normalized * MoonRadius * 1.05f;
            moonHalo.rotation = Quaternion.LookRotation(moonHalo.position - cam.transform.position);
            moonHalo.localScale = Vector3.one * MoonRadius * 3.2f;
            moonHaloMat.SetColor("_BaseColor", new Color(1f, 0.38f, 0.1f, Mathf.Lerp(0.12f, 0.75f, heat)));
        }

        /// <summary>O anel de choque corre pela planície: 0 → 28 km em 3 s (tempo real), apagando.</summary>
        private void UpdateShockRing(Vector3 ringCenter, float groundY, float t)
        {
            if (moonShockRing == null) return;
            float k = Mathf.Clamp01(t / 3f);
            if (k >= 1f) { moonShockRing.gameObject.SetActive(false); return; }
            moonShockRing.gameObject.SetActive(true);
            float r = Mathf.Lerp(MoonRadius * 0.9f, 28000f, 1f - Mathf.Pow(1f - k, 1.6f));
            moonShockRing.SetPositionAndRotation(new Vector3(ringCenter.x, groundY + 8f, ringCenter.z), Quaternion.Euler(90f, 0f, 0f));
            moonShockRing.localScale = new Vector3(r * 2f, r * 2f, 1f);
            moonShockMat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.18f, Mathf.Lerp(1f, 0f, k)));
        }

        /// <summary>O risco de lente: estoura no impacto e some em 1,4 s.</summary>
        private void UpdateStreak(float t)
        {
            if (moonStreak == null) return;
            float a = t < 0.08f ? t / 0.08f : Mathf.Clamp01(1f - (t - 0.08f) / 1.4f);
            moonStreak.color = new Color(1f, 0.8f, 0.55f, a * 0.95f);
            moonStreak.rectTransform.sizeDelta = new Vector2(0f, Mathf.Lerp(90f, 30f, Mathf.Clamp01(t / 1.4f)));
        }

        // ---------------------------------------------------------------- impacto avassalador (2026-11-20)
        // Camadas de partículas (Shuriken) por cima das que já existiam: clarão central, jatos de ejeção
        // incandescentes com rastro, explosões secundárias em volta da base, pedaços da própria lua e
        // rachaduras de lava correndo pelo chão.
        private Transform moonCoreFlash;
        private Material moonCoreMat;
        private ParticleSystem moonSecondary;
        private readonly List<Transform> moonCracks = new List<Transform>();
        private readonly List<float> moonCrackLen = new List<float>();
        private readonly List<Vector3> moonCrackDir = new List<Vector3>();
        private Material moonCrackMat;
        private float moonNextSecondary;

        private void SpawnImpactVfx(Vector3 ring, Vector3 eye, Vector3 fwd, Vector3 right, float groundY)
        {
            // CLARÃO CENTRAL: bola de luz aditiva que estoura e se expande a quilômetros
            var core = GameObject.CreatePrimitive(PrimitiveType.Quad);
            core.name = "MoonCoreFlash"; moonSpawned.Add(core);
            Destroy(core.GetComponent<Collider>());
            moonCoreMat = NewAdditiveMat(moonSoftDot);
            core.GetComponent<Renderer>().sharedMaterial = moonCoreMat;
            moonCoreFlash = core.transform;

            // JATOS DE EJEÇÃO: riscos incandescentes com rastro, em leque, para cima e para fora
            var ej = NewBurst("MoonEjecta", ring + Vector3.up * 400f, Quaternion.Euler(90f, 0f, 0f));
            var em = ej.main;
            em.duration = 1.5f; em.maxParticles = 900; em.simulationSpeed = ImpactSlowMo;
            em.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(600f, 1600f);
            em.startSize = new ParticleSystem.MinMaxCurve(90f, 260f);
            em.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.45f, 0.12f));
            em.gravityModifier = 15f;
            var eem = ej.emission;
            eem.SetBursts(new[] { new ParticleSystem.Burst(0f, 420), new ParticleSystem.Burst(0.3f, 260), new ParticleSystem.Burst(0.8f, 160) });
            var esh = ej.shape; esh.shapeType = ParticleSystemShapeType.Circle; esh.radius = MoonRadius * 0.95f; esh.radiusThickness = 0.1f;
            var evel = ej.velocityOverLifetime; evel.enabled = true; evel.space = ParticleSystemSimulationSpace.World;
            evel.x = new ParticleSystem.MinMaxCurve(0f, 0f); evel.y = new ParticleSystem.MinMaxCurve(1400f, 3200f); evel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var ecol = ej.colorOverLifetime; ecol.enabled = true;
            var eg = new Gradient();
            eg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.5f), new GradientColorKey(new Color(0.3f, 0.05f, 0.02f), 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ecol.color = eg;
            var tr = ej.trails; tr.enabled = true; tr.ratio = 0.7f; tr.lifetime = 0.35f; tr.minVertexDistance = 60f;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            tr.inheritParticleColor = true;
            var epr = ej.GetComponent<ParticleSystemRenderer>();
            var add = NewAdditiveMat(moonSoftDot);
            epr.sharedMaterial = add; epr.trailMaterial = add;
            epr.renderMode = ParticleSystemRenderMode.Stretch; epr.lengthScale = 3f; epr.velocityScale = 0.08f;
            ej.Play();

            // EXPLOSÕES SECUNDÁRIAS: o sistema fica parado e a cena dispara rajadas em pontos da base
            moonSecondary = NewBurst("MoonSecondary", ring, Quaternion.identity, true);
            var sm = moonSecondary.main;
            sm.loop = false; sm.maxParticles = 1200; sm.simulationSpeed = ImpactSlowMo;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(150f, 600f);
            sm.startSize = new ParticleSystem.MinMaxCurve(1500f, 4200f);
            var sem = moonSecondary.emission; sem.enabled = false;
            var ssh = moonSecondary.shape; ssh.enabled = false;
            var ssz = moonSecondary.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.3f));
            var scol = moonSecondary.colorOverLifetime; scol.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0f), new GradientColorKey(new Color(0.95f, 0.3f, 0.05f), 0.2f), new GradientColorKey(new Color(0.08f, 0.03f, 0.03f), 0.7f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.06f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
            scol.color = sg;
            moonSecondary.Play();
            moonNextSecondary = 0.25f;

            // PEDAÇOS DA PRÓPRIA LUA: blocos enormes e incandescentes se soltando do topo e voando
            var chunks = new GameObject("MoonChunks"); moonSpawned.Add(chunks);
            var cps = chunks.AddComponent<ParticleSystem>();
            cps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var cm = cps.main; cm.loop = false; cm.playOnAwake = false; cm.maxParticles = 60; cm.simulationSpace = ParticleSystemSimulationSpace.World;
            cm.simulationSpeed = ImpactSlowMo; cm.startRotation3D = true; cm.gravityModifier = 15f;
            var cem = cps.emission; cem.enabled = false; var csh = cps.shape; csh.enabled = false;
            var crot = cps.rotationOverLifetime; crot.enabled = true; crot.separateAxes = true;
            crot.x = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f); crot.y = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f); crot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var cpr = chunks.GetComponent<ParticleSystemRenderer>();
            cpr.renderMode = ParticleSystemRenderMode.Mesh; cpr.SetMeshes(RockMeshes()); cpr.alignment = ParticleSystemRenderSpace.World; cpr.maxParticleSize = 20f;
            var cmat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            cmat.SetTexture("_BaseMap", moonTex); cmat.SetColor("_BaseColor", new Color(0.6f, 0.56f, 0.52f));
            cmat.EnableKeyword("_EMISSION"); cmat.SetColor("_EmissionColor", new Color(0.8f, 0.22f, 0.04f));
            cpr.sharedMaterial = cmat;
            cps.Play();
            var rng = new System.Random(77);
            float R() => (float)rng.NextDouble();
            Vector3 moonTop = ring + Vector3.up * (MoonRadius * 1.4f);
            for (int i = 0; i < 26; i++)
            {
                Vector3 dir = (Vector3.up * Mathf.Lerp(0.6f, 1.2f, R()) + right * Mathf.Lerp(-1f, 1f, R()) - fwd * Mathf.Lerp(-0.2f, 0.8f, R())).normalized;
                var ep = new ParticleSystem.EmitParams
                {
                    position = moonTop + dir * MoonRadius * 0.4f,
                    velocity = dir * Mathf.Lerp(700f, 1700f, R()),
                    startLifetime = 9f,
                    startSize3D = Vector3.one * Mathf.Lerp(250f, 1100f, R()),
                    rotation3D = new Vector3(R() * 360f, R() * 360f, R() * 360f),
                    startColor = Color.white, applyShapeToPosition = false
                };
                cps.Emit(ep, 1);
            }

            // RACHADURAS DE LAVA: fendas brilhantes correndo pelo chão, do impacto para o lado da Clear
            moonCracks.Clear(); moonCrackLen.Clear(); moonCrackDir.Clear();
            moonCrackMat = NewAdditiveMat(StreakTexture());
            moonCrackMat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.1f, 1f));
            for (int i = 0; i < 16; i++)
            {
                float ang = Mathf.Lerp(-80f, 80f, i / 15f) + Mathf.Lerp(-6f, 6f, R());
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * -fwd;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "MoonCrack"; moonSpawned.Add(q);
                Destroy(q.GetComponent<Collider>());
                q.GetComponent<Renderer>().sharedMaterial = moonCrackMat;
                q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // deitado no chão (face para cima), comprido (eixo X do quad) na direção da rachadura
                q.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir) * Quaternion.Euler(90f, 0f, 0f);
                q.transform.position = new Vector3(ring.x, groundY + 3f, ring.z) + dir * MoonRadius;
                moonCrackDir.Add(dir);
                q.SetActive(false);
                moonCracks.Add(q.transform);
                moonCrackLen.Add(Mathf.Lerp(3500f, 9000f, R()));
            }
        }

        /// <summary>Um quadro das camadas do impacto (t em segundos de tempo real desde o toque).</summary>
        private void UpdateImpactVfx(Vector3 ring, float groundY, Vector3 fwd, Camera cam, float t)
        {
            if (moonCoreFlash != null && cam != null)
            {
                Vector3 c = ring + Vector3.up * (MoonRadius * 0.25f);
                moonCoreFlash.position = c + (cam.transform.position - c).normalized * MoonRadius * 1.2f;
                moonCoreFlash.rotation = Quaternion.LookRotation(moonCoreFlash.position - cam.transform.position);
                float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.7f), 3f);
                moonCoreFlash.localScale = Vector3.one * Mathf.Lerp(MoonRadius * 0.5f, MoonRadius * 7f, grow);
                moonCoreMat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.7f, Mathf.Clamp01(1f - t / 2.2f)));
            }
            // explosões secundárias: uma rajada a cada ~0,22 s nos primeiros 3,5 s
            if (moonSecondary != null && t >= moonNextSecondary && t < 3.5f)
            {
                moonNextSecondary += Random.Range(0.15f, 0.3f);
                float a = Random.Range(-100f, 100f) * Mathf.Deg2Rad;
                Vector3 dir = -fwd * Mathf.Cos(a) + Vector3.Cross(Vector3.up, fwd) * Mathf.Sin(a);
                Vector3 at = ring + dir * MoonRadius * Random.Range(0.95f, 1.35f) + Vector3.up * Random.Range(100f, 1200f);
                var ep = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false };
                for (int i = 0; i < 26; i++)
                {
                    ep.velocity = Random.onUnitSphere * Random.Range(150f, 600f) + Vector3.up * 300f;
                    ep.startSize = Random.Range(1200f, 3800f);
                    ep.startLifetime = Random.Range(3f, 5.5f);
                    moonSecondary.Emit(ep, 1);
                }
            }
            // rachaduras: crescem em 1,6 s e esfriam devagar
            float grow2 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.1f) / 1.6f));
            for (int i = 0; i < moonCracks.Count; i++)
            {
                var q = moonCracks[i];
                if (q == null) continue;
                if (!q.gameObject.activeSelf && t > 0.1f) q.gameObject.SetActive(true);
                float len = moonCrackLen[i] * grow2;
                q.localScale = new Vector3(Mathf.Max(1f, len), Mathf.Lerp(60f, 220f, grow2), 1f);
                q.position = new Vector3(ring.x, groundY + 3f, ring.z) + moonCrackDir[i] * (MoonRadius + len * 0.5f);
            }
            if (moonCrackMat != null) moonCrackMat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.1f, Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((t - 2f) / 6f))));
        }
        private Material NewLit(Color color, Texture tex)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh);
            m.SetColor("_BaseColor", color);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", 0.05f);
            return m;
        }

        private Material NewUnlit(Color hdr)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", hdr);
            return m;
        }

        /// <summary>Material de partícula transparente (ponto macio), alfa normal.</summary>
        private Material NewSoftParticleMat(bool cameraFade = false)
        {
            var pm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            pm.SetTexture("_BaseMap", moonSoftDot);
            pm.SetFloat("_Surface", 1f); pm.SetFloat("_Blend", 0f);
            pm.SetOverrideTag("RenderType", "Transparent");
            pm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            pm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            pm.SetInt("_ZWrite", 0);
            pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (cameraFade)
            {
                // SOME PERTO DA LENTE (ajustado no vídeo): as nuvens de centenas de metros passando pela
                // câmera eram cortadas pelo plano próximo e viravam um RETÂNGULO na tela. Somem de 1,2 km a 150 m.
                pm.EnableKeyword("_FADING_ON");
                pm.SetFloat("_CameraFadingEnabled", 1f);
                pm.SetFloat("_CameraNearFadeDistance", 150f);
                pm.SetFloat("_CameraFarFadeDistance", 1200f);
                pm.SetVector("_CameraFadeParams", new Vector4(150f, 1f / (1200f - 150f), 0f, 0f));
            }
            pm.renderQueue = 3000;
            return pm;
        }

        private ParticleSystem NewBurst(string name, Vector3 at, Quaternion rot, bool cameraFade = false)
        {
            var go = new GameObject(name); moonSpawned.Add(go);
            go.transform.SetPositionAndRotation(at, rot);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = NewSoftParticleMat(cameraFade);
            // Sem teto de tamanho na tela (padrão 0,5): as nuvens gigantes perto da lente eram ENCOLHIDAS
            // para meia tela e viravam um bloco retangular preso à imagem.
            var rend = go.GetComponent<ParticleSystemRenderer>(); rend.maxParticleSize = 20f; rend.sortMode = ParticleSystemSortMode.Distance;
            return ps;
        }

        /// <summary>
        /// A NUVEM DE FOGO: centenas de bolhas enormes subindo em cogumelo — branco-amarelo no miolo,
        /// laranja, vermelho escuro e por fim fumaça negra, crescendo enquanto sobem.
        /// </summary>
        private void SpawnFireCloud(Vector3 at)
        {
            var ps = NewBurst("MoonFireCloud", at + Vector3.up * 300f, Quaternion.Euler(90f, 0f, 0f), true);
            var main = ps.main;
            main.simulationSpeed = ImpactSlowMo;
            main.duration = 1.2f; main.maxParticles = 700;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(80f, 300f);
            main.startSize = new ParticleSystem.MinMaxCurve(4000f, 10000f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 260), new ParticleSystem.Burst(0.2f, 180), new ParticleSystem.Burst(0.5f, 140) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = MoonRadius * 0.95f; sh.radiusThickness = 0.15f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(900f, 2600f); // cogumelo mais alto
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 1800f; limit.dampen = 0.05f;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.72f, 0.3f), 0f), new GradientColorKey(new Color(0.95f, 0.32f, 0.06f), 0.14f),
                              new GradientColorKey(new Color(0.38f, 0.07f, 0.03f), 0.38f), new GradientColorKey(new Color(0.04f, 0.02f, 0.02f), 0.65f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.05f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
        }

        /// <summary>
        /// A PAREDE DE POEIRA: um anel de nuvens rasteiras disparado do impacto pela planície, rápido
        /// (frente a ~2,6 km/s), brilhando de fogo embaixo e escurecendo em poeira — é ela que chega na Clear.
        /// </summary>
        private void SpawnDustWall(Vector3 at)
        {
            var ps = NewBurst("MoonDustWall", at + Vector3.up * 350f, Quaternion.Euler(90f, 0f, 0f), true);
            var main = ps.main;
            main.simulationSpeed = ImpactSlowMo;
            main.duration = 0.8f; main.maxParticles = 900;
            // vida curta o bastante para PARAR antes da Clear (~2,3 km de percurso): nuvens gigantes
            // atravessando a lente geravam um retângulo na tela. O branco cobre a chegada.
            main.startLifetime = new ParticleSystem.MinMaxCurve(5.2f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(700f, 850f);
            main.startSize = new ParticleSystem.MinMaxCurve(900f, 2200f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 300), new ParticleSystem.Burst(0.15f, 260), new ParticleSystem.Burst(0.35f, 200) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = MoonRadius * 0.95f; sh.radiusThickness = 0.05f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(60f, 260f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0f), new GradientColorKey(new Color(0.4f, 0.12f, 0.05f), 0.3f),
                              new GradientColorKey(new Color(0.09f, 0.04f, 0.03f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.04f), new GradientAlphaKey(0.9f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
        }

        /// <summary>DESTROÇOS: brasas e rochas atiradas para o alto, caindo com gravidade.</summary>
        private void SpawnDebris(Vector3 at)
        {
            var go = new GameObject("MoonDebris"); moonSpawned.Add(go);
            go.transform.position = at + Vector3.up * 200f;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f; main.loop = false;
            main.simulationSpeed = ImpactSlowMo;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(900f, 2200f);
            main.startSize = new ParticleSystem.MinMaxCurve(120f, 420f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(3f, 1f, 0.3f), new Color(0.15f, 0.05f, 0.03f));
            main.gravityModifier = 25f;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 380), new ParticleSystem.Burst(0.25f, 160) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 22f; sh.radius = MoonRadius * 0.9f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0f), new GradientColorKey(new Color(0.1f, 0.03f, 0.02f), 0.6f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = NewSoftParticleMat(true); // some perto da lente, como a poeira
            pr.maxParticleSize = 20f;
            ps.Play();
        }

        private void EnsureMoonFlash()
        {
            if (moonFlashCanvas != null) return;
            var root = new GameObject("PesadeloMoonFlash");
            root.transform.SetParent(transform, false);
            moonFlashCanvas = root.AddComponent<Canvas>();
            moonFlashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            moonFlashCanvas.sortingOrder = 492;
            var img = new GameObject("Flash");
            img.transform.SetParent(root.transform, false);
            var rt = img.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            moonFlashImage = img.AddComponent<Image>();
            moonFlashImage.raycastTarget = false;
            moonFlashImage.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// ESTRONDO SINTETIZADO (enquanto não há um do ElevenLabs): estalo de ruído, um sub que despenca
        /// de 70 para 22 Hz e uma cauda longa de ruído grave rolando e se afastando. 5 s.
        /// </summary>
        private static AudioClip SynthMoonImpact()
        {
            if (moonSynthImpact != null) return moonSynthImpact;
            const int rate = 44100; int n = rate * 5;
            var d = new float[n]; var rnd = new System.Random(5);
            float lp = 0f, lp2 = 0f, ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float w = (float)(rnd.NextDouble() * 2 - 1);
                float cut = Mathf.Lerp(0.5f, 0.015f, Mathf.Clamp01(t / 1.5f));
                lp += (w - lp) * cut; lp2 += (lp - lp2) * 0.05f;
                float crack = w * Mathf.Exp(-t / 0.05f) * 0.9f;
                ph += 2f * Mathf.PI * Mathf.Lerp(70f, 22f, Mathf.Clamp01(t / 1.2f)) / rate;
                float sub = Mathf.Sin(ph) * Mathf.Exp(-t / 1.6f) * 1.1f;
                float roll = (lp * 0.8f + lp2 * 3f) * Mathf.Exp(-t / 2.2f) * (0.8f + 0.2f * Mathf.Sin(t * 9f));
                d[i] = (float)System.Math.Tanh((crack + sub + roll) * 1.4f);
            }
            moonSynthImpact = AudioClip.Create("MoonImpactSynth", n, 1, rate, false);
            moonSynthImpact.SetData(d, 0);
            return moonSynthImpact;
        }

        /// <summary>Desmonta a cena da lua (fim do beat / salto de debug).</summary>
        private void EndMoonScene()
        {
            foreach (var go in moonSpawned) if (go != null) Destroy(go);
            moonSpawned.Clear();
            foreach (var r in moonHiddenCreature) if (r != null) r.enabled = true;
            moonHiddenCreature.Clear();
            if (moonHiddenDream != null) { moonHiddenDream.enabled = true; moonHiddenDream = null; }
            moonCamera = null;
            if (moonFlashCanvas != null) moonFlashCanvas.gameObject.SetActive(false);
            if (moonBarsCanvas != null) moonBarsCanvas.gameObject.SetActive(false);
            if (moonRumbleSource != null) { moonRumbleSource.Stop(); Destroy(moonRumbleSource); moonRumbleSource = null; }
            if (moonPantSource != null) { moonPantSource.Stop(); Destroy(moonPantSource); moonPantSource = null; }
            if (moonWindSource != null) { moonWindSource.Stop(); Destroy(moonWindSource); moonWindSource = null; }
            if (moonMusicSource != null) { moonMusicSource.Stop(); Destroy(moonMusicSource); moonMusicSource = null; }
            CinemaEnd();
        }
    }
}

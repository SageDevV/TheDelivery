using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Player;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "trailer": MINI TRAILER da cidade (ato Road) — 11 planos curtos de ângulos diferentes,
    /// da tarde até a noite: grua sobre a rua A, o mendigo, a esquina A/B, o ônibus chegando ao
    /// ponto, câmera rente ao asfalto, jornais ao vento, a rua B do alto, os postes acendendo, o
    /// poste com mau contato, os faróis pela rua A e a rua C de noite. Abre do preto e fecha com
    /// a cartela "THE DELIVERY".
    ///
    /// Mesma câmera de cinema do <see cref="RoadCityFilm"/> (cópia da do jogo + pós URP, ouvido
    /// nela), Clear fora de cena, interface do jogo escondida. Carros/pedestres/ônibus agendados
    /// para chegar a cada plano. Play pela Road:
    /// <c>Temp/GameScreenshot.pending</c> = "300 Assets/Scenes/Road.unity trailer". 4 s de aquecimento.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadTrailerFilm
    {
        private const float Warmup = 4f;
        private const float Length = 55f;      // planos (0–50) + cartela (50–55)
        private const float TitleAt = 50f;

        private const float AIda = 249.2f, AVolta = 244.8f, BIda = 374.1f, BVolta = 369.7f;

        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo fS = typeof(StreetTrafficCar).GetField("s", F);
        private static readonly FieldInfo fSpeed = typeof(StreetTrafficCar).GetField("speed", F);
        private static readonly FieldInfo fCruise = typeof(StreetTrafficCar).GetField("cruise", F);
        private static readonly FieldInfo fWait = typeof(StreetTrafficCar).GetField("waitTimer", F);
        private static readonly FieldInfo fLane = typeof(StreetTrafficCar).GetField("lane", F);
        private static readonly MethodInfo mLaunch = typeof(StreetTrafficCar).GetMethod("Launch", F);

        static RoadTrailerFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "trailer")
                return;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsInactive.Exclude);
                var peds = GameObject.Find("Pedestrians");
                if (player == null || cars.Length == 0 || peds == null || Camera.main == null)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(Warmup + Length + 1f, 24, true);
                rec.StartCoroutine(Film(player, cars, peds));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player, StreetTrafficCar[] cars, GameObject peds)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadTrailerFilm] {Time.time - t0:0.0}s {text}"); }
            float T() => Time.time - t0 - Warmup;
            var byName = new Dictionary<string, StreetTrafficCar>();
            foreach (var c in cars) byName[c.name] = c;

            // --- Clear fora de cena.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.5f, 200f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            // --- Câmera de cinema.
            Camera gameCam = Camera.main;
            var camGo = new GameObject("TrailerCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(gameCam);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = Mathf.Max(gameCam.farClipPlane, 600f);
            var src = gameCam.GetUniversalAdditionalCameraData();
            var dst = cam.GetUniversalAdditionalCameraData();
            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = src.antialiasing;
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = camGo.transform;
            cam.depth = gameCam.depth + 10f;
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)) l.enabled = false;
            camGo.AddComponent<AudioListener>();
            gameCam.enabled = false;
            camGo.tag = "MainCamera";

            // --- Cartela (fade do preto + título): canvas próprio, poupado pelo Upkeep.
            var uiGo = new GameObject("TrailerTitleCanvas");
            var canvas = uiGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = uiGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var blackGo = new GameObject("Black", typeof(RectTransform));
            blackGo.transform.SetParent(uiGo.transform, false);
            var black = blackGo.AddComponent<Image>();
            black.color = Color.black;
            var br = (RectTransform)blackGo.transform;
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = br.offsetMax = Vector2.zero;
            TMP_Text MakeText(string name, string text, float size, float y, string fontPath, float spacing)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(uiGo.transform, false);
                var t = go.AddComponent<TextMeshProUGUI>();
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (font != null) t.font = font;
                t.text = text;
                t.fontSize = size;
                t.characterSpacing = spacing;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(0.92f, 0.88f, 0.80f, 0f);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(1f, 0.5f);
                r.sizeDelta = new Vector2(0f, size * 1.6f);
                r.anchoredPosition = new Vector2(0f, y);
                return t;
            }
            var title = MakeText("Title", "THE DELIVERY", 150f, 30f, "Assets/_Project/Fonts/RubikDirt-Regular SDF.asset", 12f);
            var tagline = MakeText("Tagline", "a cidade não dorme. ela espera.", 42f, -95f, "Assets/_Project/Fonts/SpecialElite-Regular SDF.asset", 4f);

            // --- Luz: tarde → anoitecer (postes acendem) → noite. O tráfego some a partir de 0,7,
            // então até o plano 10 fica abaixo disso; o plano 11 (rua C vazia) já é noite fechada.
            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            if (nightfall != null) nightfall.Stop();
            float Light(float t) => t < 27f ? Mathf.Lerp(0.36f, 0.56f, t / 27f)
                                 : t < 36f ? Mathf.Lerp(0.56f, 0.655f, (t - 27f) / 9f)
                                 : t < 45f ? Mathf.Lerp(0.655f, 0.69f, (t - 36f) / 9f)
                                 : Mathf.Lerp(0.70f, 0.76f, Mathf.Clamp01((t - 45f) / 5f));

            yield return null;
            while (System.Array.Exists(cars, c => fLane.GetValue(c) == null)) yield return null;
            foreach (var c in cars)
                if (!c.IsDriving) fWait.SetValue(c, 9999f);

            var host = Object.FindAnyObjectByType<DemoFrameRecorder>();

            bool filming = true;
            IEnumerator Upkeep()
            {
                while (filming)
                {
                    foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                        if (cv != canvas && cv.renderMode == RenderMode.ScreenSpaceOverlay && cv.enabled) cv.enabled = false;
                    if (nightfall != null)
                    {
                        if (nightfall.IsRunning) nightfall.Stop();
                        apply?.Invoke(nightfall, new object[] { Light(Mathf.Max(0f, T())) });
                    }
                    // Fade: abre do preto (0–1,2 s), fecha no fim do último plano e mostra a cartela.
                    float t = T();
                    float a = t < 0f ? 1f
                            : t < 1.2f ? 1f - t / 1.2f
                            : t < TitleAt - 1.5f ? 0f
                            : Mathf.Clamp01((t - (TitleAt - 1.5f)) / 1.5f);
                    black.color = new Color(0f, 0f, 0f, a);
                    float ta = Mathf.Clamp01((t - TitleAt - 0.4f) / 1.4f) * (1f - Mathf.Clamp01((t - (Length - 0.8f)) / 0.8f));
                    float ga = Mathf.Clamp01((t - TitleAt - 1.6f) / 1.2f) * (1f - Mathf.Clamp01((t - (Length - 0.8f)) / 0.8f));
                    title.color = new Color(title.color.r, title.color.g, title.color.b, ta);
                    tagline.color = new Color(tagline.color.r, tagline.color.g, tagline.color.b, ga);
                    title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.06f, Mathf.Clamp01((t - TitleAt) / 5f));
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            // ---------------------------------------------------------------- utilidades
            IEnumerator LaunchLater(StreetTrafficCar c, float launchAt, float cruise)
            {
                while (T() < launchAt) yield return null;
                mLaunch.Invoke(c, null);
                fCruise.SetValue(c, cruise);
                fSpeed.SetValue(c, cruise);
            }

            void CarAt(string carName, Vector3 p, float at)
            {
                if (!byName.TryGetValue(carName, out var c)) { Ev($"carro {carName} não existe"); return; }
                if (!c.IsDriving) mLaunch.Invoke(c, null);
                var lane = (StreetRoute.LanePath)fLane.GetValue(c);
                float best = 0f, bestD = float.MaxValue;
                for (float d = 0f; d < lane.Length; d += 0.5f)
                {
                    lane.Evaluate(d, out Vector3 q, out _);
                    float dd = (new Vector2(q.x, q.z) - new Vector2(p.x, p.z)).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; best = d; }
                }
                float cruise = (float)fCruise.GetValue(c);
                float s0 = best - cruise * (at - T());
                if (s0 < 0f)
                {
                    float launchAt = at - best / cruise;
                    fWait.SetValue(c, 9999f);
                    typeof(StreetTrafficCar).GetMethod("BeginWait", F)?.Invoke(c, new object[] { 9999f });
                    host.StartCoroutine(LaunchLater(c, launchAt, cruise));
                    Ev($"{c.name} → ({p.x:0},{p.z:0}) em t={at:0.0}: larga em t={launchAt:0.0}");
                    return;
                }
                fS.SetValue(c, s0);
                fSpeed.SetValue(c, cruise);
                Ev($"{c.name} → ({p.x:0},{p.z:0}) em t={at:0.0}");
            }

            void PedAt(string npcName, Vector3 meet, float at)
            {
                Transform npc = peds.transform.Find(npcName);
                bool ok = RoadAudioDemo.PutOnLine(peds, npc, meet, Mathf.Max(0.5f, at - T()), out Vector3 m);
                Ev(ok ? $"{npcName} → ({m.x:0.0},{m.z:0.0}) em t={at:0.0}" : $"{npcName}: indisponível");
            }
            Transform Npc(string n) => peds.transform.Find(n);

            bool cut = true;
            void Pose(Vector3 pos, Vector3 lookAt, float smooth = 0f)
            {
                Quaternion rot = Quaternion.LookRotation(lookAt - pos, Vector3.up);
                if (cut || smooth <= 0f)
                    camGo.transform.SetPositionAndRotation(pos, rot);
                else
                {
                    float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
                    camGo.transform.SetPositionAndRotation(Vector3.Lerp(camGo.transform.position, pos, k),
                        Quaternion.Slerp(camGo.transform.rotation, rot, k));
                }
                cut = false;
            }
            float Ease(float x) => x * x * (3f - 2f * x);
            IEnumerator Shot(string name, float from, float to, System.Action<float> frame, float fov = 50f)
            {
                while (T() < from) yield return null;
                Ev($"PLANO {name}");
                cut = true;
                cam.fieldOfView = fov;
                while (T() < to)
                {
                    frame(Mathf.Clamp01((T() - from) / (to - from)));
                    yield return null;
                }
            }

            // Ônibus (plano 4) e jornais (plano 6).
            var busGo = GameObject.Find("Bus_Linha");
            var bus = busGo != null ? busGo.GetComponent<StreetTrafficCar>() : null;
            FieldInfo fStopS = typeof(StreetTrafficCar).GetField("stopS", F);
            FieldInfo fNextStop = typeof(StreetTrafficCar).GetField("nextStop", F);
            void BusBefore(int stop, float meters)
            {
                if (bus == null || fLane.GetValue(bus) == null) { Ev("sem ônibus"); return; }
                if (!bus.IsDriving) mLaunch.Invoke(bus, null);
                var stops = (float[])fStopS.GetValue(bus);
                if (stops == null || stop >= stops.Length) { Ev("pontos não calculados"); return; }
                fNextStop.SetValue(bus, stop);
                fS.SetValue(bus, Mathf.Max(0f, stops[stop] - meters));
                fSpeed.SetValue(bus, (float)fCruise.GetValue(bus));
                Ev($"ônibus a {meters:0} m do ponto {stop}");
            }

            var papers = System.Array.FindAll(Object.FindObjectsByType<WindblownPaper>(FindObjectsSortMode.None),
                w => w != null && w.name.Contains("RuaB"));
            var cityWind = CityWind.Instance;
            void PutPaper(int i, Vector3 at, float yaw)
            {
                if (i >= papers.Length) return;
                var rb = papers[i].GetComponent<Rigidbody>();
                if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                    at.y = hit.point.y + 0.03f;
                rb.position = at;
                rb.rotation = Quaternion.Euler(0f, yaw, 0f);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            IEnumerator GustAt(float at, float seconds, float strength)
            {
                while (T() < at) yield return null;
                cityWind?.ForceGust(seconds, strength);
                Ev($"rajada {strength:0.0} por {seconds:0}s");
            }

            FieldInfo fFaulty = typeof(StreetLightPole).GetField("faulty", F);
            StreetLightPole faulty = null;
            foreach (var p in Object.FindObjectsByType<StreetLightPole>(FindObjectsSortMode.None))
                if ((bool)fFaulty.GetValue(p) && (faulty == null || p.name.StartsWith("StreetLight_B")))
                    faulty = p;

            // ================================================================= PLANOS
            Pose(new Vector3(244f, 16f, 258f), new Vector3(247f, 0f, 320f));
            CarAt("Car_Ida_2", new Vector3(AIda, 0f, 284f), 3.0f);
            CarAt("Car_Volta_6", new Vector3(AVolta, 0f, 298f), 4.0f);
            PedAt("NPC1", new Vector3(258.5f, 0f, 290f), 3f);
            PedAt("NPC6", new Vector3(258.5f, 0f, 302f), 4f);
            PedAt("NPC7", new Vector3(235.13f, 0f, 330f), 8f);

            // 1. grua descendo sobre a rua A
            yield return Shot("1 grua A", 0f, 5f, u =>
            {
                float e = Ease(u);
                Pose(Vector3.Lerp(new Vector3(244f, 16f, 258f), new Vector3(244.5f, 9f, 280f), e),
                     Vector3.Lerp(new Vector3(247f, 0f, 320f), new Vector3(248f, 0f, 330f), e));
            });

            // 2. o mendigo, câmera recuando à frente dele, sedã passando rente
            CarAt("Car_Volta_7", new Vector3(AVolta, 0f, 330f), 7.6f);
            yield return Shot("2 mendigo", 5f, 9.5f, u =>
            {
                Transform n = Npc("NPC7");
                if (n == null) return;
                Vector3 fwd = n.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 pos = n.position + fwd * (5.5f - 2f * u) + Vector3.right * 1.6f + Vector3.up * 0.9f;
                Pose(pos, n.position + Vector3.up * 1.2f, 4f);
            }, 42f);

            // 3. esquina A/B: panorâmica acompanhando a curva
            CarAt("Car_Ida_3", new Vector3(AIda, 0f, 362f), 11.2f);
            CarAt("Car_Volta_5", new Vector3(225f, 0f, BVolta), 12.8f);
            IEnumerator BusLater() { while (T() < 10.5f) yield return null; BusBefore(0, 34f); }
            host.StartCoroutine(BusLater());   // ônibus já a caminho (fora do quadro da esquina)
            yield return Shot("3 esquina", 9.5f, 13.5f, u =>
            {
                float yaw = Mathf.Lerp(-15f, -85f, Ease(u));
                Vector3 pos = new Vector3(259.5f, 3.2f, 358f);
                Pose(pos, pos + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 20f + Vector3.down * 3f);
            });

            // 4. ônibus encostando no ponto da B
            Vector3 shelterB = new Vector3(13f, 0f, 378.85f);
            yield return Shot("4 ponto de ônibus", 13.5f, 18f, u =>
            {
                Pose(new Vector3(Mathf.Lerp(3f, 5f, u), 1.6f, 364.6f),
                     Vector3.Lerp(shelterB + new Vector3(10f, 1.5f, 0f), shelterB + new Vector3(2f, 1.3f, 0f), Ease(u)));
            });

            // 5. rente ao asfalto: carro vem de frente e outro passa ao lado
            CarAt("Car_Volta_8", new Vector3(110f, 0f, BVolta), 20.3f);
            CarAt("Car_Ida_4", new Vector3(110f, 0f, BIda), 21.6f);
            yield return Shot("5 rente ao asfalto", 18f, 22.5f, u =>
            {
                Vector3 pos = new Vector3(110f, 0.45f, 371.9f);
                Pose(pos, pos + new Vector3(-20f, 0.6f, -0.8f));
            }, 45f);

            // 6. jornais ao vento: câmera baixa contra o vento, folhas vindo
            PutPaper(0, new Vector3(151f, 0f, 371.0f), 20f);
            PutPaper(1, new Vector3(153.5f, 0f, 373.4f), 110f);
            PutPaper(2, new Vector3(156f, 0f, 369.6f), 200f);
            PutPaper(3, new Vector3(158.5f, 0f, 372.2f), 300f);
            host.StartCoroutine(GustAt(22.7f, 5f, 1.2f));
            yield return Shot("6 jornais ao vento", 22.5f, 27f, u =>
            {
                Vector3 pos = new Vector3(Mathf.Lerp(147f, 145.5f, u), 0.5f, 371.6f);
                Pose(pos, pos + new Vector3(12f, 0.7f, 0.3f));
            });

            // 7. carro-câmera: acompanhando um carro pela rua B, rente à lateral
            PedAt("NPC2", new Vector3(160f, 0f, 378.8f), 30f);
            PedAt("NPC4", new Vector3(150f, 0f, 378.8f), 29.5f);
            CarAt("Car_Volta_7", new Vector3(150f, 0f, BVolta), 30.2f);
            CarAt("Car_Ida_1", new Vector3(150f, 0f, BIda), 29.5f);
            StreetTrafficCar follow = byName.TryGetValue("Car_Ida_1", out var fc) ? fc : null;
            yield return Shot("7 acompanhando carro", 27f, 31.5f, u =>
            {
                if (follow == null) return;
                Transform c = follow.transform;
                Vector3 fwd = c.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                Vector3 pos = c.position + side * 3.4f + Vector3.up * 1.0f + fwd * Mathf.Lerp(3.5f, -1.5f, Ease(u));
                Pose(pos, c.position + Vector3.up * 0.8f + fwd * 0.6f, u < 0.06f ? 0f : 6f); // grudada no corte
            }, 40f);

            // 8. a fileira de postes acendendo, carro de faróis acesos atravessando
            CarAt("Car_Volta_5", new Vector3(200f, 0f, BVolta), 34.3f);
            CarAt("Car_Ida_3", new Vector3(196f, 0f, BIda), 35.5f);
            yield return Shot("8 postes acendendo", 31.5f, 36f, u =>
            {
                Pose(new Vector3(Mathf.Lerp(214f, 210.5f, u), 1.6f, 366.6f), new Vector3(150f, 2.5f, 372f));
            });

            // 9. o poste com mau contato, de baixo para cima
            yield return Shot("9 mau contato", 36f, 40f, u =>
            {
                if (faulty == null) return;
                Vector3 b = faulty.transform.position;
                Vector3 away = faulty.transform.right; away.y = 0f;
                Pose(b + away.normalized * Mathf.Lerp(4.5f, 3.6f, u) + Vector3.up * 1.4f, b + Vector3.up * 6.6f);
            });

            // 10. recuo pela rua A, faróis vindo de frente
            CarAt("Car_Ida_2", new Vector3(AIda, 0f, 300f), 41.2f);
            CarAt("Car_Volta_6", new Vector3(AVolta, 0f, 296f), 43.6f);
            PedAt("NPC6", new Vector3(258.5f, 0f, 304f), 42.5f);
            yield return Shot("10 faróis na A", 40f, 45f, u =>
            {
                float e = Ease(u);
                Vector3 pos = new Vector3(247f, Mathf.Lerp(1.7f, 2.6f, e), Mathf.Lerp(296f, 285f, e));
                Pose(pos, pos + new Vector3(0f, -0.25f, 10f));
            });

            // 11. a rua C de noite: câmera sobe devagar do asfalto, postes acesos, fecha no preto
            yield return Shot("11 rua C noite", 45f, TitleAt, u =>
            {
                float e = Ease(u);
                Pose(new Vector3(-27.1f, Mathf.Lerp(1.6f, 6.5f, e), Mathf.Lerp(150f, 142f, e)),
                     new Vector3(-27.1f, Mathf.Lerp(1.8f, 0.5f, e), 260f));
            });

            // Cartela.
            while (T() < Length) yield return null;
            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

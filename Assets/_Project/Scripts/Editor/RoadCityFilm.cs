using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "cityfilm": take cinematográfico do ato Road — o cotidiano da rua (pedestres nas calçadas,
    /// o mendigo, o tráfego nas duas mãos) em 8 planos de ângulos diferentes, enquanto a tarde cai.
    ///
    /// Uma câmera de cinema separada (cópia da do jogo, com o pós-processamento do URP) assume a
    /// tela e o ouvido; a Clear fica fora de cena (calçada leste, antes do início da rota) e a
    /// interface (legendas) some durante a gravação. Carros e pedestres são POSICIONADOS para
    /// chegar a cada plano no instante certo (reflexão no StreetTrafficCar: Launch + s + velocidade;
    /// pedestres via <see cref="RoadAudioDemo.PutOnLine"/>). Play pela Road:
    /// <c>Temp/GameScreenshot.pending</c> = "300 Assets/Scenes/Road.unity cityfilm". 4 s de aquecimento.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadCityFilm
    {
        private const float Warmup = 4f;
        private const float FilmLength = 66f;
        private const float LightsLength = 40f;
        private const float BusLength = 42f;

        // Centro das faixas (pista ± laneOffset 2,2): A (x) e B (z).
        private const float AIda = 249.2f, AVolta = 244.8f, BIda = 374.1f, BVolta = 369.7f;

        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo fS = typeof(StreetTrafficCar).GetField("s", F);
        private static readonly FieldInfo fSpeed = typeof(StreetTrafficCar).GetField("speed", F);
        private static readonly FieldInfo fCruise = typeof(StreetTrafficCar).GetField("cruise", F);
        private static readonly FieldInfo fWait = typeof(StreetTrafficCar).GetField("waitTimer", F);
        private static readonly FieldInfo fLane = typeof(StreetTrafficCar).GetField("lane", F);
        private static readonly MethodInfo mLaunch = typeof(StreetTrafficCar).GetMethod("Launch", F);

        static RoadCityFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "cityfilm" && prep != "lightsfilm" && prep != "busfilm")
                return;
            bool lightsOnly = prep == "lightsfilm";
            bool busOnly = prep == "busfilm";
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
                rec.Begin(Warmup + (busOnly ? BusLength : lightsOnly ? LightsLength : FilmLength) + 1f, 24, true);
                rec.StartCoroutine(busOnly ? BusFilm(player) : lightsOnly ? LightsFilm(player, cars) : Film(player, cars, peds));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player, StreetTrafficCar[] cars, GameObject peds)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadCityFilm] {Time.time - t0:0.0}s {text}"); }
            var byName = new Dictionary<string, StreetTrafficCar>();
            foreach (var c in cars) byName[c.name] = c;

            // --- Clear fora de cena: calçada leste, antes da origem da rota (fora do spawnClearance).
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.5f, 200f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            // --- Câmera de cinema: cópia da do jogo (mesmo URP/pós), assume tela e ouvido.
            Camera gameCam = Camera.main;
            var camGo = new GameObject("CityFilmCamera");
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

            // --- Anoitecer conduzido pelo roteiro (0,35 → 0,66; o tráfego some a partir de 0,7).
            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            if (nightfall != null) nightfall.Stop();

            // Ninguém larga sozinho: o roteiro decide quem entra em cena e quando.
            yield return null;
            while (System.Array.Exists(cars, c => fLane.GetValue(c) == null)) yield return null;
            foreach (var c in cars)
                if (!c.IsDriving) fWait.SetValue(c, 9999f);

            // ---------------------------------------------------------------- utilidades
            float T() => Time.time - t0 - Warmup; // tempo do filme (0 = corte inicial)
            var host = Object.FindAnyObjectByType<DemoFrameRecorder>();

            IEnumerator LaunchLater(StreetTrafficCar c, float launchAt, float cruise)
            {
                while (T() < launchAt) yield return null;
                mLaunch.Invoke(c, null);
                fCruise.SetValue(c, cruise);
                fSpeed.SetValue(c, cruise);
            }

            // Faz o carro passar pelo ponto P (na pista dele) no instante 'at' do filme.
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
                    // Ponto perto demais da origem para chegar só em 'at': espera escondido e larga na hora.
                    float launchAt = at - best / cruise;
                    fWait.SetValue(c, 9999f);
                    typeof(StreetTrafficCar).GetMethod("BeginWait", F)?.Invoke(c, new object[] { 9999f });
                    host.StartCoroutine(LaunchLater(c, launchAt, cruise));
                    Ev($"{c.name} → ({p.x:0},{p.z:0}) em t={at:0.0}: larga da origem em t={launchAt:0.0}");
                    return;
                }
                fS.SetValue(c, s0);
                fSpeed.SetValue(c, cruise);
                Ev($"{c.name} ({PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject)?.name}) → ({p.x:0},{p.z:0}) em t={at:0.0} (s={s0:0}, {cruise:0.0} m/s)");
            }

            void PedAt(string npcName, Vector3 meet, float at)
            {
                Transform npc = peds.transform.Find(npcName);
                bool ok = RoadAudioDemo.PutOnLine(peds, npc, meet, Mathf.Max(0.5f, at - T()), out Vector3 m);
                Ev(ok ? $"{npcName} → ({m.x:0.0},{m.z:0.0}) em t={at:0.0}" : $"{npcName}: indisponível");
            }

            Transform Npc(string n) => peds.transform.Find(n);

            // Pose da câmera: corte seco no 1º quadro do plano, depois suave (sem tremer em rastreio).
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

            // Roda um plano de 'from' a 'to' chamando 'frame(u)' (u = 0..1) a cada quadro.
            IEnumerator Shot(string title, float from, float to, System.Action<float> frame)
            {
                while (T() < from) yield return null;
                Ev($"PLANO {title}");
                cut = true;
                while (T() < to)
                {
                    frame(Mathf.Clamp01((T() - from) / (to - from)));
                    yield return null;
                }
            }

            // Interface fora da tela (legendas/HUD) e anoitecer, todo quadro.
            bool filming = true;
            IEnumerator Upkeep()
            {
                while (filming)
                {
                    foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                        if (cv.renderMode == RenderMode.ScreenSpaceOverlay && cv.enabled) cv.enabled = false;
                    if (nightfall != null && nightfall.IsRunning) nightfall.Stop(); // o roteiro manda na luz
                    if (nightfall != null)
                        apply?.Invoke(nightfall, new object[] { Mathf.Lerp(0.35f, 0.66f, Mathf.Clamp01(T() / FilmLength)) });
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            // Enquanto o aquecimento roda, a câmera já espera na posição do plano 1.
            Pose(new Vector3(244f, 14f, 262f), new Vector3(247f, 0f, 320f));

            // Elenco do plano 1 (posto durante o aquecimento).
            CarAt("Car_Ida_2", new Vector3(AIda, 0f, 285f), 3.5f);     // hatch passa por baixo da grua
            CarAt("Car_Volta_6", new Vector3(AVolta, 0f, 300f), 6.5f);  // picape vem de frente
            PedAt("NPC6", new Vector3(258.5f, 0f, 300f), 5f);
            PedAt("NPC1", new Vector3(258.5f, 0f, 290f), 4f);
            PedAt("NPC7", new Vector3(235.13f, 0f, 296f), 6f);

            // ---------------------------------------------------------------- 1. grua sobre a rua A
            yield return Shot("1 grua A", 0f, 9f, u =>
            {
                float e = Ease(u);
                Pose(Vector3.Lerp(new Vector3(244f, 14f, 262f), new Vector3(244.5f, 9f, 284f), e),
                     Vector3.Lerp(new Vector3(247f, 0f, 320f), new Vector3(248f, 0f, 330f), e));
            });

            // ---------------------------------------------------------------- 2. acompanhando NPC1
            PedAt("NPC1", new Vector3(258.5f, 0f, 320f), 13.5f);
            CarAt("Car_Ida_4", new Vector3(AIda, 0f, 320f), 13.5f);     // perua passa em primeiro plano
            yield return Shot("2 travelling calçada", 9f, 17f, u =>
            {
                Transform n = Npc("NPC1");
                if (n == null) return;
                Vector3 fwd = n.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 pos = n.position + Vector3.left * 3.2f + Vector3.up * 1.35f - fwd * 2.5f;
                Pose(pos, n.position + Vector3.up * 1.1f + fwd * 2.5f, 5f);
            });

            // ---------------------------------------------------------------- 3. o mendigo
            PedAt("NPC7", new Vector3(235.13f, 0f, 330f), 22f);
            CarAt("Car_Volta_7", new Vector3(AVolta, 0f, 330f), 21f);   // sedã passa rente à calçada dele
            CarAt("Car_Ida_1", new Vector3(AIda, 0f, 330f), 23.5f);
            yield return Shot("3 mendigo", 17f, 25f, u =>
            {
                Transform n = Npc("NPC7");
                if (n == null) return;
                Vector3 fwd = n.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 pos = n.position + fwd * (6.5f - 2.5f * u) + Vector3.right * 1.6f + Vector3.up * 0.9f;
                Pose(pos, n.position + Vector3.up * 1.2f, 4f);
            });

            // ---------------------------------------------------------------- 4. esquina A/B
            CarAt("Car_Ida_3", new Vector3(AIda, 0f, 362f), 27.5f);     // sobe A e vira para B
            CarAt("Car_Volta_5", new Vector3(225f, 0f, BVolta), 29.5f); // desce B e vira para A
            yield return Shot("4 esquina", 25f, 33f, u =>
            {
                float yaw = Mathf.Lerp(-10f, -95f, Ease(u));
                Vector3 pos = new Vector3(259.5f, 3.2f, 358f);
                Pose(pos, pos + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 20f + Vector3.down * 3f);
            });

            // ---------------------------------------------------------------- 5. calçada da B
            PedAt("NPC3", new Vector3(140f, 0f, 364.5f), 37.5f);
            PedAt("NPC5", new Vector3(140f, 0f, 364.5f), 37.5f);
            CarAt("Car_Volta_8", new Vector3(140f, 0f, BVolta), 38f);
            CarAt("Car_Ida_2", new Vector3(140f, 0f, BIda), 35.5f);
            yield return Shot("5 cruzamento B", 33f, 41f, u =>
            {
                Pose(new Vector3(Mathf.Lerp(133.5f, 135f, u), 1.3f, 367.2f), new Vector3(141f, 1.0f, 364.5f), 3f);
            });

            // ---------------------------------------------------------------- 6. rente ao asfalto
            CarAt("Car_Volta_6", new Vector3(110f, 0f, BVolta), 45.5f); // vem de frente, farol na lente
            CarAt("Car_Ida_4", new Vector3(110f, 0f, BIda), 47.5f);     // passa ao lado, indo embora
            yield return Shot("6 nível do para-choque", 41f, 49f, u =>
            {
                Vector3 pos = new Vector3(110f, 0.45f, 371.9f);
                Pose(pos, pos + new Vector3(-20f, 0.6f, -0.8f));
            });

            // ---------------------------------------------------------------- 7. alto sobre a B
            PedAt("NPC2", new Vector3(160f, 0f, 378.8f), 54f);
            PedAt("NPC4", new Vector3(150f, 0f, 378.8f), 53f);
            CarAt("Car_Volta_7", new Vector3(150f, 0f, BVolta), 53f);
            CarAt("Car_Ida_1", new Vector3(150f, 0f, BIda), 55f);
            yield return Shot("7 alto B", 49f, 58f, u =>
            {
                float e = Ease(u);
                // Sobre o eixo da rua B (fora dos prédios), olhando a rua para oeste.
                Pose(new Vector3(Mathf.Lerp(188f, 178f, e), Mathf.Lerp(15f, 12f, e), 371.9f),
                     new Vector3(Mathf.Lerp(150f, 135f, e), 0f, 371.9f));
            });

            // ---------------------------------------------------------------- 8. recuo pela A
            PedAt("NPC6", new Vector3(258.5f, 0f, 312f), 61f);
            CarAt("Car_Ida_3", new Vector3(AIda, 0f, 300f), 59.5f);      // passa ao lado e vai embora
            CarAt("Car_Volta_5", new Vector3(AVolta, 0f, 296f), 63.5f);  // vem de frente com os faróis
            yield return Shot("8 recuo A", 58f, FilmLength, u =>
            {
                float e = Ease(u);
                Vector3 pos = new Vector3(247f, Mathf.Lerp(1.7f, 2.6f, e), Mathf.Lerp(296f, 284f, e));
                Pose(pos, pos + new Vector3(0f, -0.25f, 10f));
            });

            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "lightsfilm": a rua ACENDENDO. Anoitecer acelerado (0,48 → 0,86 em 40 s) e 4 planos: a
        /// rua A do alto enquanto os postes pegam um a um, a fileira de postes da B com um carro, um
        /// poste com mau contato de baixo para cima e a rua C já de noite. Mesma câmera de cinema do
        /// "cityfilm". Play pela Road: "300 Assets/Scenes/Road.unity lightsfilm".
        /// </summary>
        private static IEnumerator LightsFilm(PlayerController player, StreetTrafficCar[] cars)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadCityFilm] {Time.time - t0:0.0}s {text}"); }
            float T() => Time.time - t0 - Warmup;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.5f, 200f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            Camera gameCam = Camera.main;
            var camGo = new GameObject("LightsFilmCamera");
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

            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            var net = Object.FindAnyObjectByType<StreetLightNetwork>();
            var host = Object.FindAnyObjectByType<DemoFrameRecorder>();

            float Progress(float t) => t < 14f ? Mathf.Lerp(0.48f, 0.63f, t / 14f)
                                   : t < 22f ? Mathf.Lerp(0.63f, 0.67f, (t - 14f) / 8f)
                                   : t < 30f ? Mathf.Lerp(0.67f, 0.70f, (t - 22f) / 8f)
                                   : Mathf.Lerp(0.70f, 0.86f, Mathf.Clamp01((t - 30f) / 10f));
            bool filming = true;
            int lastLit = -1;
            IEnumerator Upkeep()
            {
                while (filming)
                {
                    foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                        if (cv.renderMode == RenderMode.ScreenSpaceOverlay && cv.enabled) cv.enabled = false;
                    if (nightfall != null)
                    {
                        if (nightfall.IsRunning) nightfall.Stop();
                        apply?.Invoke(nightfall, new object[] { Progress(Mathf.Max(0f, T())) });
                    }
                    if (net != null && net.LitCount != lastLit)
                    {
                        lastLit = net.LitCount;
                        Ev($"postes acesos {lastLit}/{net.PoleCount} (anoitecer {nightfall?.Progress:0.000})");
                    }
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            void Pose(Vector3 pos, Vector3 lookAt) =>
                camGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up));
            float Ease(float x) => x * x * (3f - 2f * x);
            IEnumerator Shot(string title, float from, float to, System.Action<float> frame)
            {
                while (T() < from) yield return null;
                Ev($"PLANO {title}");
                while (T() < to)
                {
                    frame(Mathf.Clamp01((T() - from) / (to - from)));
                    yield return null;
                }
            }

            // Um carro de faróis acesos atravessando a fileira de postes da B (plano 2).
            void CarAt(string carName, Vector3 p, float at)
            {
                var c = System.Array.Find(cars, x => x.name == carName);
                if (c == null || fLane.GetValue(c) == null) return;
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
                fS.SetValue(c, Mathf.Max(0f, best - cruise * (at - T())));
                fSpeed.SetValue(c, cruise);
                Ev($"{c.name} → ({p.x:0},{p.z:0}) em t={at:0.0}");
            }

            // Poste com mau contato para o close (de preferência na rua B).
            FieldInfo fFaulty = typeof(StreetLightPole).GetField("faulty", F);
            StreetLightPole faulty = null;
            foreach (var p in Object.FindObjectsByType<StreetLightPole>(FindObjectsSortMode.None))
                if ((bool)fFaulty.GetValue(p) && (faulty == null || p.name.StartsWith("StreetLight_B")))
                    faulty = p;
            Ev($"poste com mau contato: {(faulty != null ? faulty.name : "nenhum")}");

            Pose(new Vector3(247f, 12f, 250f), new Vector3(247f, 0f, 330f));

            yield return Shot("1 rua A acendendo", 0f, 14f, u =>
            {
                float e = Ease(u);
                Pose(Vector3.Lerp(new Vector3(247f, 12f, 250f), new Vector3(247f, 9f, 268f), e), new Vector3(247f, 0f, 340f));
            });

            CarAt("Car_Volta_8", new Vector3(200f, 0f, BVolta), 18.5f);
            CarAt("Car_Ida_3", new Vector3(196f, 0f, BIda), 20f);
            yield return Shot("2 fileira da B", 14f, 22f, u =>
            {
                Pose(new Vector3(Mathf.Lerp(214f, 210f, u), 1.6f, 366.6f), new Vector3(150f, 2.5f, 372f));
            });

            yield return Shot("3 mau contato", 22f, 30f, u =>
            {
                if (faulty == null) return;
                Vector3 b = faulty.transform.position;
                Vector3 away = faulty.transform.right; away.y = 0f; // ao longo da calçada
                Pose(b + away.normalized * Mathf.Lerp(4.5f, 3.5f, u) + Vector3.up * 1.4f, b + Vector3.up * 6.6f);
            });

            yield return Shot("4 rua C de noite", 30f, LightsLength, u =>
            {
                float e = Ease(u);
                Pose(new Vector3(-27.1f, Mathf.Lerp(14f, 10f, e), Mathf.Lerp(105f, 125f, e)), new Vector3(-27.1f, 0f, 230f));
            });

            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "busfilm": a linha de ônibus. O ônibus é adiantado na pista para chegar ao ponto da rua B
        /// logo após o corte (2 passageiros embarcam) e, depois, ao ponto da rua C (3 passageiros).
        /// 5 planos; anoitecer 0,50 → 0,64 (os postes acendem durante o vídeo).
        /// Play pela Road: "300 Assets/Scenes/Road.unity busfilm".
        /// </summary>
        private static IEnumerator BusFilm(PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadCityFilm] {Time.time - t0:0.0}s {text}"); }
            float T() => Time.time - t0 - Warmup;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.5f, 200f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            Camera gameCam = Camera.main;
            var camGo = new GameObject("BusFilmCamera");
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

            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            var host = Object.FindAnyObjectByType<DemoFrameRecorder>();
            var busGo = GameObject.Find("Bus_Linha");
            var bus = busGo != null ? busGo.GetComponent<StreetTrafficCar>() : null;
            if (bus == null) { Ev("SEM ONIBUS"); yield break; }
            while (fLane.GetValue(bus) == null) yield return null;
            FieldInfo fStopS = typeof(StreetTrafficCar).GetField("stopS", F);
            FieldInfo fNextStop = typeof(StreetTrafficCar).GetField("nextStop", F);

            bool filming = true;
            int lastDwell = -2;
            IEnumerator Upkeep()
            {
                while (filming)
                {
                    foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                        if (cv.renderMode == RenderMode.ScreenSpaceOverlay && cv.enabled) cv.enabled = false;
                    if (nightfall != null)
                    {
                        if (nightfall.IsRunning) nightfall.Stop();
                        apply?.Invoke(nightfall, new object[] { Mathf.Lerp(0.50f, 0.64f, Mathf.Clamp01(T() / BusLength)) });
                    }
                    if (bus.DwellingAt != lastDwell)
                    {
                        lastDwell = bus.DwellingAt;
                        Ev(lastDwell >= 0 ? $"onibus PARADO no ponto {lastDwell}" : $"onibus andando (proximo ponto {bus.NextStop})");
                    }
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            // Põe o ônibus 'meters' antes do ponto 'stop' (larga se estiver esperando).
            void BusBefore(int stop, float meters)
            {
                if (!bus.IsDriving) mLaunch.Invoke(bus, null);
                var stops = (float[])fStopS.GetValue(bus);
                if (stops == null || stop >= stops.Length) { Ev("pontos nao calculados"); return; }
                fNextStop.SetValue(bus, stop);
                fS.SetValue(bus, Mathf.Max(0f, stops[stop] - meters));
                fSpeed.SetValue(bus, (float)fCruise.GetValue(bus));
                Ev($"onibus a {meters:0} m do ponto {stop}");
            }

            void Pose(Vector3 pos, Vector3 lookAt) =>
                camGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up));
            float Ease(float x) => x * x * (3f - 2f * x);
            IEnumerator Shot(string title, float from, float to, System.Action<float> frame)
            {
                while (T() < from) yield return null;
                Ev($"PLANO {title}");
                while (T() < to)
                {
                    frame(Mathf.Clamp01((T() - from) / (to - from)));
                    yield return null;
                }
            }

            // Ainda no aquecimento: ônibus a caminho do ponto da B.
            while (T() < -1.5f) yield return null;
            BusBefore(0, 62f);
            Vector3 shelterB = new Vector3(13f, 0f, 378.85f), doorB = bus.StopDoorWorld(0);
            Pose(new Vector3(4f, 1.7f, 364.6f), shelterB + Vector3.up * 1.2f);

            yield return Shot("1 ponto B chegada", 0f, 10f, u =>
            {
                Pose(new Vector3(Mathf.Lerp(4f, 6f, u), 1.7f, 364.6f),
                     Vector3.Lerp(shelterB + new Vector3(14f, 1.4f, 0f), shelterB + new Vector3(2f, 1.2f, 0f), Ease(u)));
            });
            yield return Shot("2 porta embarque", 10f, 17f, u =>
            {
                Pose(doorB + new Vector3(6f, 1.6f, 2.0f), doorB + new Vector3(-0.5f, 1.2f, -0.2f)); // calçada a leste da porta, ao longo da lateral do ônibus
            });
            yield return Shot("3 saindo do ponto", 17f, 22f, u =>
            {
                Vector3 b = bus.transform.position;
                Pose(new Vector3(Mathf.Lerp(18f, 8f, u), 3.2f, 366f), b + Vector3.up * 1.6f);
            });

            BusBefore(1, 60f);
            Vector3 shelterC = new Vector3(-34.85f, 0f, 302f), doorC = bus.StopDoorWorld(1);
            yield return Shot("4 ponto C embarque", 22f, 34f, u =>
            {
                Vector3 aim = Vector3.Lerp(shelterC + new Vector3(1.5f, 1.4f, 12f), doorC + new Vector3(0f, 1.3f, 0f), Ease(Mathf.Clamp01(u * 1.6f)));
                Pose(new Vector3(-21.2f, 1.7f, Mathf.Lerp(292f, 294f, u)), aim);
            });
            yield return Shot("5 seguindo pela C", 34f, BusLength, u =>
            {
                Vector3 b = bus.transform.position;
                Pose(new Vector3(-27.1f, Mathf.Lerp(11f, 13f, u), 318f), b + Vector3.up * 1.2f);
            });

            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

using System.IO;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "roadvideo" — o INTERLÚDIO inteiro, andado: Play isolado na Road
    /// (marcador <c>Temp/GameScreenshot.pending</c> = "900 Assets/Scenes/Road.unity roadvideo"),
    /// 4 s de aquecimento do gravador (cortados na montagem), e a Clear ANDA pela rua com um gamepad
    /// virtual seguindo a NavMesh: até o gatilho do assovio, até o portão do encontro (o diretor
    /// assume, ela vira, vai até o estranho, a conversa), e depois até o prédio — porta e corte para
    /// a Recepção. Trilha em Temp/DemoFrames/log.txt, com o progresso do anoitecer em cada marco.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadAudioDemo
    {
        private const float Duration = 300f;
        private static bool noSkips;

        static RoadAudioDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        /// <summary>
        /// "trafficvideo": teste do tráfego. A Clear é posta NA PISTA do primeiro carro da ida, de
        /// frente para ele: o carro freia, para antes dela e buzina; depois de alguns segundos ela
        /// sai da frente (anda para a calçada) e ele segue. Registra cada carro a cada 2 s.
        /// </summary>
        private static void TrafficVideo()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var director = Object.FindAnyObjectByType<JourneyDirector>();
                if (player == null || director == null || director.CurrentBeat == JourneyBeat.None)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(44f, 24, true);
                rec.StartCoroutine(TrafficScript(player));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator TrafficScript(PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadAudioDemo] {Time.time - t0:0.0}s {text}"); }
            var cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsInactive.Exclude);
            Gamepad pad = InputSystem.AddDevice<Gamepad>("RoadTrafficGamepad");

            // Na pista da ida (x ≈ 249,2), 45 m à frente da origem dos carros, olhando para eles (sul).
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(249.2f, 0.5f, 300f), Quaternion.Euler(0f, 180f, 0f));
            if (cc != null) cc.enabled = true;
            Ev("Clear na pista da ida, de frente para o trânsito (aquecimento 4 s)");

            float stepAsideAt = 22f, logAt = 0f;
            bool aside = false;
            while (Time.time - t0 < 42f)
            {
                if (Time.time - t0 >= logAt)
                {
                    logAt += 2f;
                    var sb = new StringBuilder();
                    foreach (var c in cars)
                        sb.Append($"{c.name}:{(c.IsDriving ? "anda" : "espera")}{(c.IsYieldingToPlayer ? "/PARADO-P-ELA" : "")} buz={c.HornsPlayed} pos=({c.transform.position.x:0},{c.transform.position.z:0}) | ");
                    Ev(sb.ToString());
                }
                if (!aside && Time.time - t0 >= stepAsideAt)
                {
                    aside = true;
                    Ev("ela sai da frente (anda para a calçada, oeste)");
                }
                if (aside && player.transform.position.x > 239f)
                {
                    player.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
                    InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
                }
                else
                {
                    if (aside) player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.Euler(0f, 120f, 0f), 120f * Time.deltaTime); // vira para a rua (leste-sul): vê o carro passar de lado
                    InputSystem.QueueStateEvent(pad, new GamepadState());
                }
                yield return null;
            }
            InputSystem.RemoveDevice(pad);
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        private static void OnPlayReady(string prep)
        {
            if (prep == "trafficvideo")
            {
                TrafficVideo();
                return;
            }
            // "roadvideofull": o mesmo percurso SEM os saltos de corte — a jornada inteira, para validar.
            noSkips = prep == "roadvideofull";
            if (prep != "roadvideo" && prep != "roadvideofull")
                return;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var director = Object.FindAnyObjectByType<JourneyDirector>();
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (director == null || player == null || director.CurrentBeat == JourneyBeat.None)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(noSkips ? 420f : Duration, 24, true);
                rec.StartCoroutine(Script(director, player));
            }
            EditorApplication.update += Tick;
        }

        /// <summary>Registra cada evento do tráfego: largada, sumiço, passagem com som, parada por ela, buzinada.</summary>
        private static System.Collections.IEnumerator WatchCars(System.Action<string> ev)
        {
            var cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsInactive.Exclude);
            var driving = new bool[cars.Length];
            var yielding = new bool[cars.Length];
            var horns = new int[cars.Length];
            var passes = new int[cars.Length];
            while (true)
            {
                for (int i = 0; i < cars.Length; i++)
                {
                    var c = cars[i];
                    if (c == null) continue;
                    if (c.IsDriving != driving[i]) { driving[i] = c.IsDriving; ev($"TRÁFEGO {c.name} {(c.IsDriving ? "larga" : "some")}"); }
                    if (c.IsYieldingToPlayer != yielding[i]) { yielding[i] = c.IsYieldingToPlayer; ev($"TRÁFEGO {c.name} {(c.IsYieldingToPlayer ? "PARA por causa dela" : "segue")}"); }
                    if (c.HornsPlayed != horns[i]) { horns[i] = c.HornsPlayed; ev($"TRÁFEGO {c.name} BUZINA ({c.HornsPlayed})"); }
                    if (c.PassesPlayed != passes[i]) { passes[i] = c.PassesPlayed; ev($"TRÁFEGO {c.name} passa (som)"); }
                }
                yield return null;
            }
        }

        private static System.Collections.IEnumerator Script(JourneyDirector director, PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            var scape = Object.FindAnyObjectByType<NightfallSoundscape>();
            void Ev(string text)
            {
                string p = scape != null ? $" [anoitecer {scape.CurrentProgress:0.00}]" : "";
                log.AppendLine($"{Time.time - t0:0.0}  {text}{p}");
                Debug.Log($"[RoadAudioDemo] {Time.time - t0:0.0}s {text}{p}");
            }
            void Save()
            {
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
            }
            player.StartCoroutine(WatchCars(Ev));

            Gamepad pad = InputSystem.AddDevice<Gamepad>("RoadDemoGamepad");
            var trigger = GetPrivate<Transform>(director, "leaningTriggerPoint");
            var notice = GetPrivate<Transform>(director, "leaningNoticePoint");
            var destination = GetPrivate<Transform>(director, "destinationPoint");

            Ev("gravando; aquecimento de 4 s (cortado na montagem)");
            yield return new WaitForSeconds(4f);
            Ev("CORTE_INICIO — caminhada");

            // A ROTA: a NavMesh da Road não cobre a rua inteira, mas os gatilhos de pensamento
            // ("esquerda", "direita") ficam exatamente nas curvas — são os pontos de passagem. Corre
            // nos trechos longos (a rua tem ~750 m) e anda no último trecho antes do estranho.
            Transform Named(string n) { var go = GameObject.Find(n); return go != null ? go.transform : null; }
            (Transform point, bool sprint, string label)[] route =
            {
                (Named("Trigger_Thought_ActJourney-2"), true, "primeira curva (pensamento 2)"),
                (Named("Trigger_Thought_ActJourney-3"), true, "segunda curva (pensamento 3)"),
                (Named("Trigger_Thought_ActJourney-4"), true, "terceira curva (pensamento 4: 'muito silêncio')"),
                (trigger, false, "gatilho do assovio"),
                (notice, false, "portão do encontro"),
            };
            foreach (var (point, sprint, label) in route)
            {
                if (point == null || director.CurrentBeat != JourneyBeat.Walk)
                    continue;
                if (!noSkips && label.StartsWith("terceira"))
                {
                    // O TRECHO MORTO: ~260 m de rua já de noite, sem som novo. Anda 20 s nele e
                    // depois CORTA (salto) para 25 m antes da curva — só para o vídeo caber.
                    yield return WalkTo(pad, player, point.position, 1.2f, 20f, sprint, () => director.CurrentBeat != JourneyBeat.Walk);
                    Vector3 dir = point.position - player.transform.position; dir.y = 0f;
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.transform.position = point.position - dir.normalized * 25f + Vector3.up * 0.5f;
                    if (cc != null) cc.enabled = true;
                    Ev("CORTE_SALTO — pula o trecho noturno sem som novo");
                }
                yield return WalkTo(pad, player, point.position, point == notice ? 0.15f : 1.2f, 140f, sprint, () => director.CurrentBeat != JourneyBeat.Walk);
                Ev(label);
            }
            Queue(pad, Vector2.zero);
            Ev($"encontro: beat {director.CurrentBeat}");

            // 2. O encontro roda sozinho; espera o controle voltar.
            yield return new WaitUntil(() => director.CurrentBeat == JourneyBeat.Walk || Time.time - t0 > (noSkips ? 420f : Duration) - 40f);
            Ev("controle de volta (fim do encontro)");
            yield return new WaitForSeconds(1.5f);

            // 3. Última curva e o prédio.
            // O encontro termina com ela DENTRO do recuo do estranho: primeiro volta ao meio da rua
            // (o portão do encontro), senão a linha reta até a curva bate na parede do recuo.
            if (notice != null)
                yield return WalkTo(pad, player, notice.position, 1.2f, 20f, false, () => director.CurrentBeat == JourneyBeat.Arrival);
            var t5 = Named("Trigger_Thought_ActJourney-5");
            if (t5 != null)
            {
                // Anda 8 s (a tensão descendo, o assovio que não volta) e corta para 12 m da curva.
                yield return WalkTo(pad, player, t5.position, 1.2f, 8f, false, () => director.CurrentBeat == JourneyBeat.Arrival);
                Vector3 dir = t5.position - player.transform.position; dir.y = 0f;
                if (!noSkips && dir.magnitude > 14f)
                {
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.transform.position = t5.position - dir.normalized * 12f + Vector3.up * 0.5f;
                    if (cc != null) cc.enabled = true;
                    Ev("CORTE_SALTO — até perto da última curva");
                }
                yield return WalkTo(pad, player, t5.position, 1.2f, 60f, true, () => director.CurrentBeat == JourneyBeat.Arrival);
                Ev("última curva (pensamento 5: 'não tem ninguém na rua')");
            }
            if (destination != null)
                yield return WalkTo(pad, player, destination.position, 0.6f, 60f, false, () => director.CurrentBeat == JourneyBeat.Arrival);
            Queue(pad, Vector2.zero);
            Ev($"chegada: beat {director.CurrentBeat}");
            InputSystem.RemoveDevice(pad);

            yield return new WaitUntil(() => SceneManager.GetActiveScene().name != "Road" || Time.time - t0 > (noSkips ? 420f : Duration) - 6f);
            Ev($"cena ativa: {SceneManager.GetActiveScene().name}");
            yield return new WaitForSeconds(4f);
            Ev("CORTE_FIM");
            Save();
        }

        /// <summary>Anda (ou corre) em linha reta até o alvo, virando o corpo e empurrando o stick.</summary>
        private static System.Collections.IEnumerator WalkTo(Gamepad pad, PlayerController player, Vector3 target,
                                                               float stopDistance, float timeout, bool sprint, System.Func<bool> abort)
        {
            float start = Time.time;
            float nextLog = Time.time;
            // Desvio: parada (< 0,3 m em 1,5 s) = obstáculo na frente. Anda de lado por 1,2 s,
            // alternando o lado a cada tentativa, e volta a mirar no alvo.
            Vector3 lastPos = player.transform.position;
            float lastMoveCheck = Time.time;
            float sidestepUntil = -1f;
            float side = 1f;
            while (Time.time - start < timeout && !abort())
            {
                Vector3 to = target - player.transform.position;
                to.y = 0f;
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 10f;
                    Debug.Log($"[RoadAudioDemo]   pos {player.transform.position} -> {target} dist {to.magnitude:0.0}");
                }
                if (to.magnitude <= stopDistance)
                    break;
                if (Time.time - lastMoveCheck >= 1.5f)
                {
                    if (Vector3.Distance(player.transform.position, lastPos) < 0.3f && Time.time > sidestepUntil)
                    {
                        sidestepUntil = Time.time + 1.2f;
                        side = -side;
                        Debug.Log($"[RoadAudioDemo]   travada em {player.transform.position}: desviando para {(side > 0 ? "a direita" : "a esquerda")}");
                    }
                    lastPos = player.transform.position;
                    lastMoveCheck = Time.time;
                }
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 200f * Time.deltaTime);
                var state = new GamepadState { leftStick = Time.time < sidestepUntil ? new Vector2(side, 0.2f) : new Vector2(0f, 1f) };
                if (sprint)
                    state = state.WithButton(GamepadButton.LeftStick);
                if (pad != null && pad.added)
                    InputSystem.QueueStateEvent(pad, state);
                yield return null;
            }
            Queue(pad, Vector2.zero);
        }

        /// <summary>Anda pela NavMesh até o alvo, de quina em quina, virando o corpo e empurrando o stick.</summary>
        private static System.Collections.IEnumerator WalkPath(Gamepad pad, PlayerController player, Vector3 target,
                                                                 float stopDistance, float timeout, System.Func<bool> abort)
        {
            float start = Time.time;
            var path = new NavMeshPath();
            Vector3[] corners = { target };
            if (NavMesh.SamplePosition(player.transform.position, out var a, 3f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(target, out var b, 3f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 1)
                corners = path.corners;

            int i = corners.Length > 1 ? 1 : 0;
            Debug.Log($"[RoadAudioDemo] caminho: {corners.Length} quinas, status {path.status}, de {player.transform.position} até {target}");
            float nextLog = Time.time;
            while (Time.time - start < timeout && !abort())
            {
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 3f;
                    Debug.Log($"[RoadAudioDemo]   pos {player.transform.position} quina {i}/{corners.Length - 1} {corners[i]} " +
                              $"dist {Vector3.Distance(player.transform.position, corners[i]):0.0} podeMover={player.CanMove}");
                }
                Vector3 to = corners[i] - player.transform.position;
                to.y = 0f;
                float reach = i == corners.Length - 1 ? stopDistance : 1.2f;
                if (to.magnitude <= reach)
                {
                    if (i == corners.Length - 1)
                        break;
                    i++;
                    continue;
                }
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 200f * Time.deltaTime);
                Queue(pad, new Vector2(0f, 1f));
                yield return null;
            }
            Queue(pad, Vector2.zero);
        }

        private static void Queue(Gamepad pad, Vector2 move)
        {
            if (pad == null || !pad.added)
                return;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = move });
        }

        private static T GetPrivate<T>(object owner, string field) where T : class =>
            owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                 ?.GetValue(owner) as T;
    }
}

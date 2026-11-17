using System.IO;
using System.Text;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "cafeaudiovideo" — evidência da sonorização do Ato 1. Play isolado na
    /// Cafeteria (marcador <c>Temp/GameScreenshot.pending</c> = "4 Assets/Scenes/Cafeteria.unity
    /// cafeaudiovideo"):
    /// <list type="number">
    /// <item>reinicia o Awakening já gravando — o arquejo de acordar (A1-02);</item>
    /// <item>quando os pensamentos acabam, aperta Espaço — a cadeira arrasta ao levantar (A1-08);</item>
    /// <item>pula para o MarinaArrives — o sino da porta (A1-07) e a cadeira dela ao sentar;</item>
    /// <item>por baixo de tudo, os carros passando lá fora (A1-05).</item>
    /// </list>
    /// Grava com o <see cref="DemoFrameRecorder"/> (imagem + áudio) e escreve a trilha em
    /// Temp/DemoFrames/log.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class CafeteriaAudioDemo
    {
        private const float Duration = 30f;

        static CafeteriaAudioDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep == "wakeprobe")
            {
                WakeProbe();
                return;
            }
            if (prep == "wakevideo")
            {
                WakeVideo();
                return;
            }
            if (prep != "cafeaudiovideo" && prep != "cafefullvideo" && prep != "cafemixvideo")
                return;

            double at = EditorApplication.timeSinceStartup + 1.0;
            void Wait()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Wait; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Wait;
                if (prep == "cafefullvideo")
                    RunFull();
                else if (prep == "cafemixvideo")
                    RunMix();
                else
                    Run();
            }
            EditorApplication.update += Wait;
        }

        // =====================================================================
        // "cafefullvideo" — o ATO 1 INTEIRO, jogado: acorda, levanta, passeia pelo salão parando
        // em cada fonte de som (balcão, fliperama, vitrine, o senhor com o café), volta e senta,
        // conversa com a Marina, ela vai embora, a Clear sai pela porta e a cena vira a rua.
        // Anda com um gamepad VIRTUAL (passos e colisão de verdade), virando o corpo para o alvo.
        // =====================================================================
        private const float FullDuration = 125f;

        // =====================================================================
        // "cafemixvideo" — evidência da MIXAGEM do salão: aquecimento de 4 s (cortado na
        // montagem), acorda, levanta, anda até o balcão (liquidificador e espresso disparados) e
        // depois até o fliperama, ficando alguns segundos em cada um.
        // =====================================================================
        private const float MixDuration = 95f;

        private static void RunMix()
        {
            var director = Object.FindFirstObjectByType<Act1Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (director == null || player == null)
            {
                Debug.LogError("[CafeteriaAudioDemo] Act1Director ou PlayerController não encontrado.");
                return;
            }
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(MixDuration, 24, true);
            rec.StartCoroutine(MixScript(director, player));
        }

        private static System.Collections.IEnumerator MixScript(Act1Director director, PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[CafeteriaAudioDemo] {Time.time - t0:0.0}s {text}"); }

            Gamepad pad = InputSystem.AddDevice<Gamepad>("CafeMixGamepad");
            yield return new WaitForSeconds(4f); // aquecimento do gravador
            ThoughtSystem.Instance?.Interrupt();
            director.AdvanceToBeat(Act1Beat.Awakening);
            Ev("CORTE_INICIO — Awakening");
            yield return new WaitUntil(() => StandPromptVisible(director) || Time.time - t0 > 34f);
            yield return new WaitForSeconds(0.8f);
            PressSpace();
            yield return new WaitForSeconds(0.2f);
            ReleaseKeys();
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 44f);
            yield return new WaitForSeconds(0.5f);

            var blender = FindEmitter("BlenderSound");
            var counter = FindEmitter("CounterSound");
            if (blender != null)
            {
                Ev("anda até o balcão");
                yield return WalkTo(pad, player, blender.transform.position, 1.6f, 6f);
                yield return FaceTo(player, blender.transform.position, 0.6f);
                blender.PlayOneShotNow();
                Ev("LIQUIDIFICADOR");
                yield return new WaitForSeconds(6f);
                if (counter != null) { counter.PlayOneShotNow(); Ev("espresso (referência de nível)"); }
                yield return new WaitForSeconds(5f);
            }
            var arcade = FindEmitter("ArcadeSound");
            if (arcade != null)
            {
                Ev("anda até o fliperama");
                yield return WalkTo(pad, player, arcade.transform.position, 1.6f, 6f);
                yield return FaceTo(player, arcade.transform.position, 0.6f);
                Ev("FLIPERAMA");
                yield return new WaitForSeconds(6f);
            }
            var cup = FindEmitter("CupSound");
            if (cup != null)
            {
                Ev("anda até o senhor do café");
                yield return WalkTo(pad, player, cup.transform.position, 1.6f, 6f);
                yield return FaceTo(player, cup.transform.position, 0.6f);
                cup.PlayOneShotNow();
                Ev("XÍCARA");
                yield return new WaitForSeconds(5f);
            }
            QueueStick(pad, Vector2.zero);
            InputSystem.RemoveDevice(pad);
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        private static void RunFull()
        {
            var director = Object.FindFirstObjectByType<Act1Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (director == null || player == null)
            {
                Debug.LogError("[CafeteriaAudioDemo] Act1Director ou PlayerController não encontrado.");
                return;
            }

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(FullDuration, 24, true);
            rec.StartCoroutine(FullScript(rec, director, player));
        }

        private static System.Collections.IEnumerator FullScript(DemoFrameRecorder rec, Act1Director director, PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[CafeteriaAudioDemo] {Time.time - t0:0.0}s {text}"); }

            Gamepad pad = InputSystem.AddDevice<Gamepad>("CafeDemoGamepad");
            var chair = GetPrivate<TheDelivery.Interaction.SittableChair>(director, "clearChair");
            var exitZone = GetPrivate<Transform>(director, "exitZone");
            var interaction = player.GetComponentInChildren<TheDelivery.Interaction.PlayerInteraction>();

            // Vigia do tráfego: registra cada passagem de carro que tocou som (tempo + distância).
            rec.StartCoroutine(WatchTraffic(Ev));

            // 1. Acorda (arquejo) e levanta quando o jogo pede o Espaço (cadeira).
            ThoughtSystem.Instance?.Interrupt();
            director.AdvanceToBeat(Act1Beat.Awakening);
            Ev("Awakening (arquejo)");
            yield return new WaitUntil(() => StandPromptVisible(director) || Time.time - t0 > 30f);
            yield return new WaitForSeconds(0.8f);
            PressSpace();
            yield return new WaitForSeconds(0.2f);
            ReleaseKeys();
            Ev("Espaço -> levanta (cadeira)");
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 40f);
            yield return new WaitForSeconds(0.5f);

            // 2. Passeio pelas fontes de som. A Marina chega no meio (sino) — de propósito.
            string[] tour = { "CounterSound", "ArcadeSound", "DisplayHumSound", "CupSound" };
            foreach (string name in tour)
            {
                var emitter = FindEmitter(name);
                if (emitter == null) { Ev($"{name} não encontrado; pulado"); continue; }
                Ev($"anda até {name} ({emitter.transform.parent.name})");
                yield return WalkTo(pad, player, emitter.transform.position, 1.6f, 6f);
                yield return FaceTo(player, emitter.transform.position, 0.6f);
                emitter.PlayOneShotNow();
                yield return new WaitForSeconds(3.2f);
            }

            // 3. Volta para a cadeira e senta (a conversa só começa com as duas sentadas).
            if (chair != null)
            {
                Ev("volta para a cadeira");
                yield return WalkTo(pad, player, chair.transform.position, 1.1f, 8f);
                yield return FaceTo(player, chair.transform.position, 0.4f);
                if (interaction != null)
                    chair.Interact(interaction);
                Ev("senta (cadeira)");
            }

            // 4. Conversa -> Marina sai (sino) -> pensamento -> ClearLeaves (levanta).
            yield return new WaitUntil(() => director.CurrentBeat == Act1Beat.Conversation || Time.time - t0 > 75f);
            Ev($"beat {director.CurrentBeat}: conversa");
            yield return new WaitUntil(() => director.CurrentBeat == Act1Beat.MarinaLeaves || Time.time - t0 > 100f);
            Ev("Marina vai embora (sino ao sair)");
            yield return new WaitUntil(() => director.CurrentBeat == Act1Beat.ClearLeaves || Time.time - t0 > 110f);
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 112f);
            Ev("Clear levanta e vai até a porta");

            // 5. Sai pela porta (sino) -> a cena vira a rua.
            if (exitZone != null)
                yield return WalkTo(pad, player, exitZone.position, 0.4f, 10f);
            Ev("chegou na porta (sino) -> Road");

            QueueStick(pad, Vector2.zero);
            InputSystem.RemoveDevice(pad);
            yield return new WaitForSeconds(4f);
            Ev($"cena ativa: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// SONDA "wakeprobe" (sem gravar): mede, no relógio real, quando o arquejo de acordar
        /// começa a soar de verdade — o AudioSource do diretor avançando timeSamples — contra o
        /// início do Play e o relógio de áudio (dspTime). Separa atraso do JOGO de atraso do
        /// gravador. Resultado em Temp/WakeProbe.txt.
        /// </summary>
        private static void WakeProbe()
        {
            var director = Object.FindFirstObjectByType<Act1Director>();
            var source = director != null ? director.GetComponent<AudioSource>() : null;
            var log = new StringBuilder();
            double t0 = EditorApplication.timeSinceStartup;
            double dsp0 = AudioSettings.dspTime;
            log.AppendLine($"inicio: frame={Time.frameCount} time={Time.time:0.000} realtime={Time.realtimeSinceStartup:0.000} dsp={dsp0:0.000} source={(source != null)}");
            double nextLog = 0;
            void Tick()
            {
                double el = EditorApplication.timeSinceStartup - t0;
                if (!EditorApplication.isPlaying || el > 5.0)
                {
                    EditorApplication.update -= Tick;
                    File.WriteAllText("Temp/WakeProbe.txt", log.ToString());
                    Debug.Log("[CafeteriaAudioDemo] WakeProbe:\n" + log);
                    return;
                }
                if (el < nextLog) return;
                nextLog = el + 0.25;
                var ambient = Object.FindFirstObjectByType<TheDelivery.FX.AmbientMusic>();
                var amb = ambient != null ? ambient.GetComponent<AudioSource>() : null;
                log.AppendLine($"+{el:0.00}s real | frame={Time.frameCount} time={Time.time:0.00} | dsp+{AudioSettings.dspTime - dsp0:0.00} | ambiente playing={amb?.isPlaying} t={amb?.time:0.00}");
            }
            EditorApplication.update += Tick;
        }

        /// <summary>
        /// "wakevideo": grava, espera <see cref="WarmupSeconds"/> de jogo (o AudioRenderer leva
        /// alguns segundos para estabilizar logo após o Play) e SÓ ENTÃO reinicia o Awakening.
        /// O instante do arquejo fica no log; os segundos de aquecimento são cortados na montagem.
        /// </summary>
        public const float WarmupSeconds = 4f;

        private static void WakeVideo()
        {
            var director = Object.FindFirstObjectByType<Act1Director>();
            if (director == null) return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(WarmupSeconds + 12f, 24, true);
            rec.StartCoroutine(WakeScript(director));
        }

        private static System.Collections.IEnumerator WakeScript(Act1Director director)
        {
            float t0 = Time.time;
            yield return new WaitForSeconds(WarmupSeconds);
            ThoughtSystem.Instance?.Interrupt();
            director.AdvanceToBeat(Act1Beat.Awakening);
            string line = $"Awakening (arquejo) em {Time.time - t0:0.000}s de gravação";
            Debug.Log($"[CafeteriaAudioDemo] {line}");
            yield return new WaitForSeconds(13f);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), line);
        }

        private static System.Collections.IEnumerator WatchTraffic(System.Action<string> ev)
        {
            var seen = new System.Collections.Generic.Dictionary<TheDelivery.FX.TrafficCar, int>();
            while (true)
            {
                foreach (var car in Object.FindObjectsByType<TheDelivery.FX.TrafficCar>(FindObjectsSortMode.None))
                {
                    seen.TryGetValue(car, out int n);
                    if (car.PassesPlayed > n)
                        ev($"carro '{car.name}' passou com som a {car.LastPassDistance:0.0} m");
                    seen[car] = car.PassesPlayed;
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        /// <summary>Anda (stick para a frente) virando o corpo para o alvo, até chegar ou estourar o tempo.</summary>
        private static System.Collections.IEnumerator WalkTo(Gamepad pad, PlayerController player, Vector3 target, float stopDistance, float timeout)
        {
            float start = Time.time;
            while (Time.time - start < timeout && player != null)
            {
                Vector3 to = target - player.transform.position;
                to.y = 0f;
                if (to.magnitude <= stopDistance)
                    break;
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 240f * Time.deltaTime);
                float facing = Vector3.Dot(player.transform.forward, to.normalized);
                QueueStick(pad, new Vector2(0f, facing > 0.7f ? 1f : 0.2f));
                yield return null;
            }
            QueueStick(pad, Vector2.zero);
        }

        /// <summary>Vira o corpo suavemente para o alvo (sem andar).</summary>
        private static System.Collections.IEnumerator FaceTo(PlayerController player, Vector3 target, float duration)
        {
            Vector3 to = target - player.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f)
                yield break;
            Quaternion from = player.transform.rotation;
            Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                player.transform.rotation = Quaternion.Slerp(from, want, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            player.transform.rotation = want;
        }

        private static void QueueStick(Gamepad pad, Vector2 move)
        {
            if (pad != null && pad.added)
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = move });
        }

        private static TheDelivery.FX.AmbientSoundEmitter FindEmitter(string name)
        {
            foreach (var e in Object.FindObjectsByType<TheDelivery.FX.AmbientSoundEmitter>(FindObjectsSortMode.None))
                if (e.name == name)
                    return e;
            return null;
        }

        private static T GetPrivate<T>(object owner, string field) where T : class
        {
            var f = owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f?.GetValue(owner) as T;
        }

        private static void Run()
        {
            var log = new StringBuilder();
            void Ev(string text) { log.AppendLine($"{Time.time:0.0}  {text}"); Debug.Log($"[CafeteriaAudioDemo] {text}"); }

            var director = Object.FindFirstObjectByType<Act1Director>();
            if (director == null)
            {
                Debug.LogError("[CafeteriaAudioDemo] Act1Director não encontrado.");
                return;
            }

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(Duration, 24, true);
            float t0 = Time.time;

            ThoughtSystem.Instance?.Interrupt();
            director.AdvanceToBeat(Act1Beat.Awakening);
            Ev("Awakening reiniciado (arquejo)");

            int step = 0;
            float stepAt = Time.time;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                float t = Time.time - t0;
                switch (step)
                {
                    case 0: // o jogo pediu o Espaço (prompt "levantar" visível) -> aperta
                        if (StandPromptVisible(director) && Time.time - stepAt > 0.8f)
                        {
                            PressSpace();
                            Ev("Espaço -> levanta (cadeira)");
                            step++; stepAt = Time.time;
                        }
                        else if (t > 22f)
                        {
                            Ev("prompt de levantar não apareceu até 22 s; segue sem levantar");
                            step = 2; stepAt = Time.time;
                        }
                        else if (!StandPromptVisible(director))
                        {
                            stepAt = Time.time; // conta a folga só a partir de o prompt aparecer
                        }
                        break;
                    case 1: // solta o Espaço alguns quadros depois (o jogo precisa VER o aperto)
                        if (Time.time - stepAt > 0.2f)
                        {
                            ReleaseKeys();
                            step++;
                        }
                        break;
                    case 2: // em pé -> a Marina chega
                        if (Time.time - stepAt > 2.5f)
                        {
                            director.AdvanceToBeat(Act1Beat.MarinaArrives);
                            Ev("MarinaArrives (sino da porta)");
                            step++; stepAt = Time.time;
                        }
                        break;
                    case 3:
                        if (director.CurrentBeat == Act1Beat.Conversation)
                        {
                            Ev("Marina sentou (cadeira)");
                            step++;
                        }
                        else if (t > Duration)
                        {
                            Ev($"fim da gravação no beat {director.CurrentBeat}");
                            step++;
                        }
                        break;
                    case 4:
                        if (t > Duration + 1f)
                        {
                            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                            EditorApplication.update -= Tick;
                        }
                        break;
                }
            }
            EditorApplication.update += Tick;
        }

        /// <summary>O prompt "Pressione Espaço" do diretor está na tela (campo privado, lido por reflexão).</summary>
        private static bool StandPromptVisible(Act1Director director)
        {
            var field = typeof(Act1Director).GetField("standUpPrompt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(director) is GameObject prompt && prompt != null && prompt.activeInHierarchy;
        }

        private static void PressSpace()
        {
            if (Keyboard.current != null)
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Space));
        }

        private static void ReleaseKeys()
        {
            if (Keyboard.current != null)
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }
    }
}

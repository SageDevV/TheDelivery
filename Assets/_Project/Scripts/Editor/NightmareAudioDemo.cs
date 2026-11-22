using System.IO;
using System.Text;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "nightmarevideo" — o PRELÚDIO inteiro, jogado, para a evidência da
    /// sonorização (N-02 corredor, N-07 perseguição, N-14 corte). Play pela Boot (marcador
    /// <c>Temp/GameScreenshot.pending</c> = "900 Assets/Scenes/Boot.unity nightmarevideo"):
    /// menu → Seleção de Atos → Nightmare → <see cref="WarmupSeconds"/> parada (aquecimento do
    /// gravador, cortado na montagem) → anda até o rosnado → CORRE pelo corredor até o fim →
    /// a pegada, a queda e o corte → alguns segundos da cafeteria (o arquejo). Gamepad VIRTUAL:
    /// passos, corrida e colisão de verdade. Trilha em Temp/DemoFrames/log.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareAudioDemo
    {
        private const float WarmupSeconds = 4f;
        private const float Duration = 110f;
        private const float ChaseSeconds = 9f;

        private const string ExitPlayMarker = "Temp/ExitPlay.pending";

        static NightmareAudioDemo()
        {
            GameScreenshotTool.PlayReady += OnPlayReady;
            EditorApplication.update += WatchExitMarker;
        }

        /// <summary>
        /// <c>Temp/ExitPlay.pending</c> encerra o Play em andamento — para abortar uma gravação
        /// que travou sem esperar o tempo do marcador de captura.
        /// </summary>
        private static void WatchExitMarker()
        {
            if (!EditorApplication.isPlaying || !File.Exists(ExitPlayMarker))
                return;
            File.Delete(ExitPlayMarker);
            Debug.Log("[NightmareAudioDemo] ExitPlay.pending -> saindo do Play.");
            EditorApplication.ExitPlaymode();
        }

        // "nightmarefallvideo": o mesmo caminho, mas salta direto para a pegada depois do
        // aquecimento — só o fim do prelúdio (pegada, queda, baque, tontura, corte, cafeteria).
        private static bool fallOnly;
        // "nightmarecaughtvideo" (2026-11-22): anda até o rosnado e NÃO foge — fica parada esperando a
        // criatura chegar perto, para gravar a caminhada dela e o ataque por proximidade.
        private static bool caught;

        private static void OnPlayReady(string prep)
        {
            if (prep != "nightmarevideo" && prep != "nightmarefallvideo" && prep != "nightmarecaughtvideo")
                return;
            fallOnly = prep == "nightmarefallvideo";
            caught = prep == "nightmarecaughtvideo";

            // Gravação muda já aconteceu com o "Mute Audio" da Game view ligado: registra o
            // estado e, se estiver mudo, libera só durante a gravação (restaurado ao sair do Play).
            bool wasMuted = EditorUtility.audioMasterMute;
            Debug.Log($"[NightmareAudioDemo] Áudio do Editor: Mute Audio={wasMuted}, AudioListener.volume={AudioListener.volume}");
            if (wasMuted)
            {
                EditorUtility.audioMasterMute = false;
                void Restore(PlayModeStateChange s)
                {
                    if (s != PlayModeStateChange.EnteredEditMode) return;
                    EditorUtility.audioMasterMute = true;
                    EditorApplication.playModeStateChanged -= Restore;
                }
                EditorApplication.playModeStateChanged += Restore;
            }

            double start = EditorApplication.timeSinceStartup;
            double nextTry = 0;
            void WaitMenu()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= WaitMenu; return; }
                if (Object.FindFirstObjectByType<NightmareDirector>() != null)
                {
                    EditorApplication.update -= WaitMenu;
                    WaitDirector();
                    return;
                }
                if (Object.FindFirstObjectByType<MainMenuController>() == null || EditorApplication.timeSinceStartup - start < 3 ||
                    EditorApplication.timeSinceStartup < nextTry)
                    return;
                // "Novo Jogo" é o caminho real até o prelúdio (a Seleção de Atos exige o ato liberado).
                // O GameManager IGNORA o pedido enquanto a transição para o menu ainda roda, então
                // o roteiro insiste a cada 2 s até a cena do pesadelo existir.
                nextTry = EditorApplication.timeSinceStartup + 2.0;
                GameManager.Instance.StartNewGame();
                Debug.Log("[NightmareAudioDemo] Novo Jogo -> Nightmare");
            }
            EditorApplication.update += WaitMenu;
        }

        private static void WaitDirector()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var director = Object.FindFirstObjectByType<NightmareDirector>();
                var player = Object.FindFirstObjectByType<PlayerController>();
                if (director == null || player == null || director.CurrentBeat == NightmareBeat.None)
                    return;
                EditorApplication.update -= Tick;

                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.RenderActiveCamera = true; // 1080p pela câmera ativa (segue o take em 3ª pessoa), com a UI
                rec.Begin(caught ? 75f : fallOnly ? 85f : Duration, 24, true); // 85: cabe a cena da lua (lenta) no fim (2026-11-20)
                rec.StartCoroutine(Script(director, player));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator Script(NightmareDirector director, PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[NightmareAudioDemo] {text}"); }

            Gamepad pad = InputSystem.AddDevice<Gamepad>("NightmareDemoGamepad");
            var growl = GetPrivate<Transform>(director, "growlPoint");
            var abyss = GetPrivate<Transform>(director, "abyssPoint");

            Ev($"gravando; beat {director.CurrentBeat}. Aquecimento de {WarmupSeconds:0} s (cortado na montagem)");
            yield return new WaitForSeconds(WarmupSeconds);
            if (fallOnly)
            {
                Queue(pad, Vector2.zero, false);
                InputSystem.RemoveDevice(pad);
                director.AdvanceToBeat(NightmareBeat.TheGrab);
                Ev("CORTE_INICIO — salto direto para TheGrab (pegada, queda, baque, tontura)");
                yield return new WaitUntil(() => SceneManager.GetActiveScene().name == GameScene.Cafeteria.ToString() || Time.time - t0 > Duration - 6f);
                Ev($"cena ativa: {SceneManager.GetActiveScene().name} (corte -> arquejo)");
                yield return new WaitForSeconds(5f);
                Ev("CORTE_FIM");
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                yield break;
            }

            Ev("CORTE_INICIO — anda pelo corredor (leito do corredor)");

            // 1. Anda até o rosnado.
            while (director.CurrentBeat == NightmareBeat.Corridor && Time.time - t0 < 40f && growl != null)
            {
                Steer(pad, player, growl.position, sprint: false);
                yield return null;
            }
            Ev($"beat {director.CurrentBeat}: o rosnado");
            Queue(pad, Vector2.zero, false);

            // 2. Espera a corrida ser liberada e FOGE pelo corredor até o fim.
            yield return new WaitUntil(() => director.CurrentBeat != NightmareBeat.TheGrowl || Time.time - t0 > 50f);
            if (caught)
            {
                Ev($"beat {director.CurrentBeat}: parada, esperando a criatura chegar perto");
                float waitStart = Time.time;
                var cr = GetPrivate<GameObject>(director, "creatureObject");
                while (director.CurrentBeat == NightmareBeat.TheChase && Time.time - waitStart < 40f)
                {
                    // vira o corpo para a criatura (o take mostra ela vindo), sem andar
                    if (cr != null) { Vector3 to = cr.transform.position - player.transform.position; to.y = 0f; if (to.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.LookRotation(to), 180f * Time.deltaTime); }
                    yield return null;
                }
                Ev($"beat {director.CurrentBeat}: alcançada");
                InputSystem.RemoveDevice(pad);
                yield return new WaitUntil(() => director.CurrentBeat == NightmareBeat.TheCut || Time.time - t0 > 68f);
                Ev($"beat {director.CurrentBeat}");
                yield return new WaitForSeconds(4f);
                Ev("CORTE_FIM");
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                yield break;
            }
            Ev($"beat {director.CurrentBeat}: corre (camada da perseguição)");
            float chaseStart = Time.time;
            while ((director.CurrentBeat == NightmareBeat.TheChase) && Time.time - chaseStart < ChaseSeconds)
            {
                Vector3 target = abyss != null ? abyss.position : player.transform.position + player.transform.forward * 10f;
                Steer(pad, player, target, sprint: true);
                yield return null;
            }
            Queue(pad, Vector2.zero, false);
            InputSystem.RemoveDevice(pad);

            // A fuga guiada às cegas não acha o fim do corredor escuro; a pegada é roteirizada,
            // então o roteiro salta para ela (como a tecla de debug) depois de alguns segundos
            // de perseguição — o bastante para ouvir a camada da criatura chegando.
            if (director.CurrentBeat == NightmareBeat.TheChase)
            {
                director.AdvanceToBeat(NightmareBeat.TheGrab);
                Ev("salto de roteiro -> TheGrab (pegada, queda)");
            }
            Ev($"beat {director.CurrentBeat}: fim da fuga");

            // 3. Pegada/queda/corte -> cafeteria.
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == GameScene.Cafeteria.ToString() || Time.time - t0 > Duration - 6f);
            Ev($"cena ativa: {SceneManager.GetActiveScene().name} (corte -> arquejo)");
            yield return new WaitForSeconds(5f);
            Ev("CORTE_FIM");

            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>Vira o corpo para o alvo e empurra o stick para a frente (corrida opcional).</summary>
        private static void Steer(Gamepad pad, PlayerController player, Vector3 target, bool sprint)
        {
            Vector3 to = target - player.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
            {
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 180f * Time.deltaTime);
            }
            Queue(pad, new Vector2(0f, 1f), sprint);
        }

        private static void Queue(Gamepad pad, Vector2 move, bool sprint)
        {
            if (pad == null || !pad.added)
                return;
            var state = new GamepadState { leftStick = move };
            if (sprint)
                state = state.WithButton(GamepadButton.LeftStick);
            InputSystem.QueueStateEvent(pad, state);
        }

        private static T GetPrivate<T>(object owner, string field) where T : class
        {
            var f = owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f?.GetValue(owner) as T;
        }
    }
}

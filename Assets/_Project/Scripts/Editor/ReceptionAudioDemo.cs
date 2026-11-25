using System.IO;
using System.Text;
using TheDelivery.Interaction;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "receptionaudiovideo" — evidência da sonorização do Ato 2 (2026-11-23). Play isolado
    /// na Recepção (marcador <c>Temp/GameScreenshot.pending</c> = "130 Assets/Scenes/Reception.unity
    /// receptionaudiovideo"): a Clear entra, vai até o balcão (o telefone toca uma vez; o rádio),
    /// atravessa o saguão até a sala de produtos (o zelador trabalhando atrás da porta), abre a porta (rangido →
    /// o esfregão para → o golpe e o balde), conversa com o Jef (stings), vai até o elevador (botão, sino, portas,
    /// motor) e a cena troca. Anda com um gamepad VIRTUAL (passos e colisão de verdade). Trilha em Temp/DemoFrames/log.txt.
    ///
    /// "receptionscarevideo" (2026-11-23): o mesmo roteiro cortado em <see cref="ScareDuration"/> s — balcão,
    /// porta e susto. Em 1080p a gravação roda a ~0,57× do tempo real e o GameScreenshotTool sai do Play pelo
    /// relógio REAL (o 1º número do marcador): use "120 Assets/Scenes/Reception.unity receptionscarevideo".
    /// </summary>
    [InitializeOnLoad]
    public static class ReceptionAudioDemo
    {
        private const float Duration = 118f;
        private const float ScareDuration = 32f;
        private const string CandidateFile = "Temp/JumpscareCandidate.txt";

        static ReceptionAudioDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "receptionaudiovideo" && prep != "receptionscarevideo") return;
            float duration = prep == "receptionscarevideo" ? ScareDuration : Duration;
            double at = EditorApplication.timeSinceStartup + 1.0;
            void Wait()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Wait; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Wait;
                Run(duration);
            }
            EditorApplication.update += Wait;
        }

        private static void Run(float duration)
        {
            var director = Object.FindFirstObjectByType<Act2Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (director == null || player == null)
            {
                Debug.LogError("[ReceptionAudioDemo] Act2Director ou PlayerController não encontrado.");
                return;
            }
            // comparação de candidatos do susto (2026-11-25): Temp/JumpscareCandidate.txt = caminho do clipe a testar
            if (File.Exists(CandidateFile))
            {
                string path = File.ReadAllText(CandidateFile).Trim();
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var field = typeof(Act2Director).GetField("scareHitSound", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (clip != null && field != null)
                {
                    clip.LoadAudioData();
                    field.SetValue(director, clip);
                    Debug.Log($"[ReceptionAudioDemo] susto de teste: {path}");
                }
                else Debug.LogWarning($"[ReceptionAudioDemo] candidato do susto não encontrado: {path}");
            }
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true; // 1080p pela câmera ativa, com a UI (a Game View do Editor encolhida dava 875×237)
            rec.Begin(duration, 24, true);
            rec.StartCoroutine(Script(director, player));
            rec.StartCoroutine(HitchWatch());
        }

        /// <summary>
        /// Vigia de travamentos: loga todo quadro que levou mais de 250 ms REAIS. Numa gravação (captureFramerate) o
        /// jogo não pula tempo, mas o áudio segue o relógio real — um travamento descola o som da imagem (o golpe do
        /// susto saiu ~1,5 s atrasado quando o zelador aparecia pela 1ª vez, 2026-11-23).
        /// </summary>
        private static System.Collections.IEnumerator HitchWatch()
        {
            float t0 = Time.time, last = Time.realtimeSinceStartup;
            while (true)
            {
                yield return null;
                float now = Time.realtimeSinceStartup, gap = now - last; last = now;
                if (gap > 0.25f)
                    Debug.Log($"[ReceptionAudioDemo] QUADRO LENTO em {Time.time - t0:0.00}s de jogo: {gap * 1000f:0} ms reais (quadro {Time.frameCount})");
            }
        }

        private static System.Collections.IEnumerator Script(Act2Director director, PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[ReceptionAudioDemo] {Time.time - t0:0.0}s {text}"); }
            Gamepad pad = InputSystem.AddDevice<Gamepad>("ReceptionDemoGamepad");
            var interaction = player.GetComponentInChildren<PlayerInteraction>();
            var deskZone = GetPrivate<Transform>(director, "receptionistAbsentZone");
            var door = GetPrivate<ProductsRoomDoor>(director, "productsRoomDoor");
            var elevatorZone = GetPrivate<Transform>(director, "elevatorZone");
            var elevator = GetPrivate<Transform>(director, "elevatorSoundPoint");

            // 1. Chegada: o saguão (room tone, fluorescentes) e o pensamento de chegada.
            Ev("chegada (room tone, fluorescentes)");
            yield return new WaitForSeconds(4f);

            // 2. Até o balcão: o telefone toca uma vez; relógio e rádio de perto.
            if (deskZone != null)
            {
                Ev("anda até o balcão");
                yield return WalkTo(pad, player, deskZone.position, 0.8f, 10f);
                Ev("no balcão: telefone toca (ninguém atende), rádio");
                yield return FaceTo(player, deskZone.position + new Vector3(0.1f, 0f, 1.3f), 0.8f);
                yield return new WaitForSeconds(6f);
            }

            // 3. Até a porta da sala de produtos: o zelador trabalhando lá dentro.
            if (door != null)
            {
                Ev("atravessa o saguão até a sala de produtos");
                Vector3 front = door.transform.position + (player.transform.position - door.transform.position).normalized * 1.3f;
                yield return WalkTo(pad, player, new Vector3(front.x, player.transform.position.y, front.z), 0.4f, 12f);
                yield return FaceTo(player, door.transform.position, 0.6f);
                Ev("na porta: esfregão e balde atrás dela");
                yield return new WaitForSeconds(4.5f);
                yield return new WaitUntil(() => door.CanInteract || Time.time - t0 > 45f);
                if (interaction != null) door.Interact(interaction);
                Ev("abre a porta: rangido, o esfregão para -> golpe + balde, o zelador desliza");
            }

            // 4. A conversa com o Jef (stings) até o elevador ser liberado.
            yield return new WaitUntil(() => director.CurrentBeat == Act2Beat.GoToElevator || Time.time - t0 > 95f);
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 97f);
            Ev($"beat {director.CurrentBeat}: vai até o elevador");

            // 5. Elevador: contorna o sofá e vai até a zona.
            if (elevatorZone != null)
            {
                yield return WalkTo(pad, player, new Vector3(0.3f, player.transform.position.y, 0.4f), 0.6f, 8f);
                yield return WalkTo(pad, player, elevatorZone.position, 0.9f, 10f);
                QueueStick(pad, Vector2.zero);
                if (elevator != null) yield return FaceTo(player, elevator.position, 0.5f);
                Ev("no elevador: botão, sino, portas, motor (atravessa a troca)");
            }
            QueueStick(pad, Vector2.zero);
            yield return new WaitForSeconds(9f);
            Ev($"cena ativa: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            InputSystem.RemoveDevice(pad);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        private static System.Collections.IEnumerator WalkTo(Gamepad pad, PlayerController player, Vector3 target, float stopDistance, float timeout)
        {
            float start = Time.time;
            while (Time.time - start < timeout && player != null)
            {
                Vector3 to = target - player.transform.position; to.y = 0f;
                if (to.magnitude <= stopDistance) break;
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 240f * Time.deltaTime);
                float facing = Vector3.Dot(player.transform.forward, to.normalized);
                QueueStick(pad, new Vector2(0f, facing > 0.7f ? 1f : 0.2f));
                yield return null;
            }
            QueueStick(pad, Vector2.zero);
        }

        private static System.Collections.IEnumerator FaceTo(PlayerController player, Vector3 target, float duration)
        {
            Vector3 to = target - player.transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) yield break;
            Quaternion from = player.transform.rotation, want = Quaternion.LookRotation(to.normalized, Vector3.up);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                player.transform.rotation = Quaternion.Slerp(from, want, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            player.transform.rotation = want;
        }

        private static void QueueStick(Gamepad pad, Vector2 move)
        {
            if (pad != null && pad.added) InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = move });
        }

        private static T GetPrivate<T>(object owner, string field) where T : class
        {
            var f = owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f?.GetValue(owner) as T;
        }
    }
}

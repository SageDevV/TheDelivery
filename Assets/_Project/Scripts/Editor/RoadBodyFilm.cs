using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "fpbody": prova do corpo da Clear em primeira pessoa (FirstPersonBody). Jogo de verdade, pela
    /// câmera do jogador, dirigido por um gamepad virtual: olha para os pés parada, anda, corre e
    /// agacha olhando para baixo; no fim olha para a sombra no chão. De dia, calçada da rua A.
    /// Play pela Road: "300 Assets/Scenes/Road.unity fpbody". 4 s de aquecimento.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadBodyFilm
    {
        private const float Warmup = 4f;
        private const float Length = 26f;
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        static RoadBodyFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "fpbody") return;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (player == null || Camera.main == null) return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.RenderCamera = Camera.main;
                rec.Begin(Warmup + Length + 1f, 24, true);
                rec.StartCoroutine(Film(player));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[RoadBodyFilm] {Time.time - t0:0.0}s {s}"); }
            float T() => Time.time - t0 - Warmup;

            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            var body = Object.FindAnyObjectByType<FirstPersonBody>();
            Ev(body != null ? "FirstPersonBody presente no Player" : "SEM FirstPersonBody no Player!");

            Gamepad pad = InputSystem.AddDevice<Gamepad>("BodyFilmGamepad");
            try
            {
                while (T() < Length)
                {
                    float t = T();
                    if (nightfall != null)
                    {
                        if (nightfall.IsRunning) nightfall.Stop();
                        apply?.Invoke(nightfall, new object[] { 0.15f });
                    }
                    // Ela é a jogadora: o controle é dela o tempo todo (sem cutscene da jornada).
                    player.CanMove = true;

                    if (t < 0f)
                    {
                        var cc = player.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.2f, 212f), Quaternion.identity);
                        if (cc != null) cc.enabled = true;
                    }

                    var s = new GamepadState();
                    float pitch;
                    if (t < 4f) { pitch = Mathf.Lerp(10f, 62f, Mathf.SmoothStep(0f, 1f, t / 2.5f)); }                    // parada, olha os pés
                    else if (t < 10f) { s.leftStick = new Vector2(0f, 1f); pitch = 64f; }                                   // anda
                    else if (t < 15f) { s.leftStick = new Vector2(0f, 1f); s = s.WithButton(GamepadButton.LeftStick); pitch = 60f; } // corre
                    else if (t < 20f) { s = s.WithButton(GamepadButton.East); if (t < 18.5f) s.leftStick = new Vector2(0f, 0.8f); pitch = 55f; } // agacha
                    else
                    {
                        // olha a sombra: vira de costas para o sol (a sombra cai para trás-esquerda dela)
                        player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.Euler(0f, 200f, 0f), 120f * Time.deltaTime);
                        s.leftStick = t > 21.5f && t < 24f ? new Vector2(0f, 0.6f) : Vector2.zero;
                        pitch = 38f;
                    }
                    InputSystem.QueueStateEvent(pad, s);
                    player.SyncCameraState(pitch, player.EyeHeight);

                    if (Mathf.Abs(t - 0f) < 0.02f) Ev("PARADA olhando para baixo");
                    if (Mathf.Abs(t - 4f) < 0.02f) Ev("ANDA");
                    if (Mathf.Abs(t - 10f) < 0.02f) Ev("CORRE");
                    if (Mathf.Abs(t - 15f) < 0.02f) Ev("AGACHA");
                    if (Mathf.Abs(t - 20f) < 0.02f) Ev("SOMBRA");
                    yield return null;
                }
            }
            finally
            {
                InputSystem.RemoveDevice(pad);
            }
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

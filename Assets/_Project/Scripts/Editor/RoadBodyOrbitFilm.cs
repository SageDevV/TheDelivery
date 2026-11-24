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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "bodyorbit" (2026-11-24): o corpo da Clear do jogo (FirstPersonBody do Player) visto DE FORA, por uma
    /// câmera que gira em volta dela na calçada da rua A, de dia. Parte 1: o corpo como o jogo o usa (cabeça
    /// encolhida, porque a câmera do jogador fica no lugar dela), parada, andando e agachada. Parte 2: o mesmo
    /// modelo completo (o da sombra, com cabeça, tornado visível só para o vídeo).
    /// Play pela Road: "300 Assets/Scenes/Road.unity bodyorbit".
    /// </summary>
    [InitializeOnLoad]
    public static class RoadBodyOrbitFilm
    {
        private const float Warmup = 3f;
        private const float Length = 30f;
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        static RoadBodyOrbitFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "bodyorbit") return;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (player == null || Camera.main == null) return;
                EditorApplication.update -= Tick;
                var cam = new GameObject("OrbitCam").AddComponent<Camera>();
                cam.CopyFrom(Camera.main);
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.05f;
                cam.depth = Camera.main.depth - 1;
                var src = Camera.main.GetUniversalAdditionalCameraData();
                var dst = cam.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = src.volumeTrigger != null ? src.volumeTrigger : Camera.main.transform;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.RenderCamera = cam;
                rec.Begin(Warmup + Length + 1f, 24, true);
                rec.StartCoroutine(Film(player, cam));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player, Camera cam)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[BodyOrbit] {Time.time - t0:0.0}s {s}"); }
            float T() => Time.time - t0 - Warmup;

            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            var body = Object.FindAnyObjectByType<FirstPersonBody>();
            Ev(body != null ? "FirstPersonBody presente no Player" : "SEM FirstPersonBody no Player!");
            Renderer[] bodyR = body != null ? (Renderer[])typeof(FirstPersonBody).GetField("bodyRenderers", F)?.GetValue(body) : null;
            Renderer[] shadowR = body != null ? (Renderer[])typeof(FirstPersonBody).GetField("shadowRenderers", F)?.GetValue(body) : null;

            Gamepad pad = InputSystem.AddDevice<Gamepad>("BodyOrbitGamepad");
            bool full = false;
            float orbit = 200f;
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
                    player.CanMove = true;
                    if (t < 0f)
                    {
                        var cc = player.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.2f, 212f), Quaternion.identity);
                        if (cc != null) cc.enabled = true;
                    }

                    // roteiro: 0-5 parada | 5-10 anda | 10-14 agacha | 14 corte: modelo completo | 14-19 parada | 19-25 anda | 25-30 parada
                    var s = new GamepadState();
                    if ((t >= 5f && t < 10f) || (t >= 19f && t < 25f)) s.leftStick = new Vector2(0f, 0.7f);
                    if (t >= 10f && t < 14f) { s = s.WithButton(GamepadButton.East); if (t < 12f) s.leftStick = new Vector2(0f, 0.6f); }
                    if (t >= 19f && t < 25f) player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.Euler(0f, 180f, 0f), 25f * Time.deltaTime);
                    InputSystem.QueueStateEvent(pad, s);
                    player.SyncCameraState(8f, player.EyeHeight);

                    if (!full && t >= 14f && shadowR != null)
                    {
                        full = true;
                        // corte: o modelo completo (o da sombra) aparece e o corpo sem cabeça some
                        foreach (var r in shadowR) r.shadowCastingMode = ShadowCastingMode.On;
                        Ev("MODELO COMPLETO (o da sombra, com cabeça)");
                    }
                    if (full && bodyR != null) foreach (var r in bodyR) r.enabled = false;

                    // câmera girando em volta dela, um pouco acima do ombro
                    orbit += 14f * Time.deltaTime;
                    float radius = t < 14f ? 2.6f : 2.9f;
                    float height = t >= 10f && t < 14f ? 1.1f : 1.45f;
                    Vector3 c = player.transform.position;
                    Vector3 off = Quaternion.Euler(0f, orbit, 0f) * new Vector3(0f, 0f, radius);
                    cam.transform.position = c + off + Vector3.up * height;
                    cam.transform.LookAt(c + Vector3.up * (t >= 10f && t < 14f ? 0.6f : 1.0f));

                    if (Mathf.Abs(t - 0f) < 0.02f) Ev("PARADA (corpo do jogo)");
                    if (Mathf.Abs(t - 5f) < 0.02f) Ev("ANDA");
                    if (Mathf.Abs(t - 10f) < 0.02f) Ev("AGACHA");
                    if (Mathf.Abs(t - 19f) < 0.02f) Ev("ANDA (modelo completo)");
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

using System.Collections;
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
    /// "clearshow": apresenta o modelo da protagonista (Prefabs/Clear.prefab) na calçada leste da rua A,
    /// de dia. 0–9 s parada (Idle) com a câmera dando a volta nela; 9–20 s andando pela calçada
    /// (Walk, 1,25 m/s) com a câmera acompanhando de lado e depois de frente.
    /// Play pela Road: "300 Assets/Scenes/Road.unity clearshow". 4 s de aquecimento.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadClearFilm
    {
        private const float Warmup = 4f;
        private const float Length = 20f;
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        static RoadClearFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "clearshow") return;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (player == null || Camera.main == null) return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(Warmup + Length + 1f, 24, true);
                rec.StartCoroutine(Film(player));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[RoadClearFilm] {Time.time - t0:0.0}s {s}"); }
            float T() => Time.time - t0 - Warmup;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(240f, 0.5f, 300f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            Camera gameCam = Camera.main;
            var camGo = new GameObject("ClearFilmCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(gameCam);
            cam.nearClipPlane = 0.05f;
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
            // Grava direto desta câmera em 1920×1080 (a Game View do Editor pode estar encolhida).
            Object.FindAnyObjectByType<DemoFrameRecorder>().RenderCamera = cam;

            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            MethodInfo apply = typeof(NightfallController).GetMethod("Apply", F);
            var host = Object.FindAnyObjectByType<DemoFrameRecorder>();
            bool filming = true;
            IEnumerator Upkeep()
            {
                while (filming)
                {
                    foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                        if (cv.renderMode == RenderMode.ScreenSpaceOverlay && cv.enabled) cv.enabled = false;
                    if (nightfall != null)
                    {
                        if (nightfall.IsRunning) nightfall.Stop();
                        apply?.Invoke(nightfall, new object[] { 0.20f });
                    }
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Clear.prefab");
            Vector3 start = new Vector3(258.5f, 0f, 226f);
            if (Physics.Raycast(start + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                start.y = hit.point.y;
            var clear = Object.Instantiate(prefab, start, Quaternion.LookRotation(Vector3.forward));
            clear.name = "Clear (show)";
            var anim = clear.GetComponent<Animator>();
            Ev($"Clear em ({start.x:0.0},{start.y:0.00},{start.z:0.0})");

            void Pose(Vector3 pos, Vector3 look) => camGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            float Ease(float x) => x * x * (3f - 2f * x);
            Vector3 Head() => clear.transform.position + Vector3.up * 0.95f;

            cam.fieldOfView = 38f;
            while (T() < 9f)
            {
                float u = Mathf.Clamp01(T() / 9f);
                float a = Mathf.Lerp(-40f, 220f, Ease(u)) * Mathf.Deg2Rad;
                Vector3 off = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Lerp(3.4f, 2.9f, u);
                Pose(Head() + off + Vector3.up * Mathf.Lerp(0.35f, 0.15f, u), Head());
                yield return null;
            }

            Ev("ANDA");
            anim.SetBool("Walking", true);
            while (T() < Length)
            {
                float u = Mathf.Clamp01((T() - 9f) / (Length - 9f));
                clear.transform.position += Vector3.forward * 1.25f * Time.deltaTime;
                Vector3 side = new Vector3(-3.2f, 0.25f, Mathf.Lerp(0.5f, 3.6f, Ease(u)));
                Pose(Head() + side, Head() + Vector3.forward * 0.4f);
                yield return null;
            }

            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

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
    /// "fightfilm": a briga no beco da rua B (RoadAlleyFightSetup). 5 planos em 30 s, já anoitecendo
    /// (0,70 → 0,80): da calçada olhando o beco, médio de perfil, por cima do ombro do agressor (vítima
    /// implorando), contra-plongée do agressor (ponto de vista da vítima) e afastando até a boca do beco.
    /// Play pela Road: "300 Assets/Scenes/Road.unity fightfilm". 4 s de aquecimento.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadAlleyFightFilm
    {
        private const float Warmup = 4f;
        private const float Length = 30f;
        private const float LoopLength = 20f; // "fightloop": plano fixo cobrindo 2 emendas do loop (8 s e 16 s)
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        static RoadAlleyFightFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "fightfilm" && prep != "fightloop") return;
            bool loop = prep == "fightloop";
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (player == null || Camera.main == null || GameObject.Find("AlleyFight") == null) return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(Warmup + (loop ? LoopLength : Length) + 1f, 24, true);
                rec.StartCoroutine(Film(player, loop));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player, bool loop)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[RoadAlleyFightFilm] {Time.time - t0:0.0}s {s}"); }
            float T() => Time.time - t0 - Warmup;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(120f, 0.5f, 371f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            Camera gameCam = Camera.main;
            var camGo = new GameObject("FightFilmCamera");
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
                        apply?.Invoke(nightfall, new object[] { Mathf.Lerp(0.70f, 0.80f, Mathf.Clamp01(T() / Length)) });
                    }
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            Transform victim = GameObject.Find("AlleyFight/FightVictim").transform;
            Transform thug = GameObject.Find("AlleyFight/FightThug").transform;
            Vector3 vp = victim.position, tp = thug.position, mid = (vp + tp) * 0.5f;
            Ev($"vítima ({vp.x:0.0},{vp.z:0.0}) agressor ({tp.x:0.0},{tp.z:0.0})");

            void Pose(Vector3 pos, Vector3 look) => camGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            float Ease(float x) => x * x * (3f - 2f * x);
            Vector3 Shake(float amt) => new Vector3(Mathf.PerlinNoise(T() * 0.7f, 1.3f) - 0.5f, (Mathf.PerlinNoise(4.1f, T() * 0.9f) - 0.5f) * 0.6f, Mathf.PerlinNoise(T() * 0.6f, 8.8f) - 0.5f) * amt;
            IEnumerator Shot(string name, float from, float to, System.Action<float> frame, float fov)
            {
                while (T() < from) yield return null;
                Ev($"PLANO {name}");
                cam.fieldOfView = fov;
                while (T() < to) { frame(Mathf.Clamp01((T() - from) / (to - from))); yield return null; }
            }

            if (loop)
            {
                Vector3 fixedPos = mid + new Vector3(0f, 1.35f, 3.4f);
                Pose(fixedPos, mid + Vector3.up * 0.9f);
                yield return Shot("LOOP plano fixo", 0f, LoopLength, u => Pose(fixedPos, mid + Vector3.up * 0.9f), 40f);
                filming = false;
                Ev("CORTE_FIM");
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                yield break;
            }
            Pose(new Vector3(mid.x - 0.5f, 1.65f, mid.z + 8.5f), mid + Vector3.up * 0.9f);
            yield return Shot("1 da calçada", 0f, 7f, u =>
                Pose(new Vector3(mid.x - 0.5f, 1.65f, Mathf.Lerp(mid.z + 8.5f, mid.z + 5.5f, Ease(u))) + Shake(0.04f), mid + Vector3.up * 0.9f), 42f);

            yield return Shot("2 médio de perfil", 7f, 13f, u =>
            {
                float a = Mathf.Lerp(-12f, 12f, u) * Mathf.Deg2Rad;
                Vector3 off = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.2f;
                Pose(mid + off + Vector3.up * 1.35f + Shake(0.03f), mid + Vector3.up * 0.95f);
            }, 40f);

            yield return Shot("3 ombro do agressor", 13f, 19f, u =>
            {
                Vector3 behind = tp + (tp - vp).normalized * 0.75f + new Vector3(0f, 0f, 0.45f);
                Pose(behind + Vector3.up * Mathf.Lerp(1.75f, 1.65f, u) + Shake(0.03f), vp + Vector3.up * Mathf.Lerp(0.85f, 0.95f, u));
            }, 38f);

            yield return Shot("4 agressor visto pela vítima", 19f, 24f, u =>
            {
                Vector3 eye = vp + (tp - vp).normalized * 0.15f + new Vector3(0f, 0f, -0.5f);
                Pose(eye + Vector3.up * 0.95f + Shake(0.05f), tp + Vector3.up * Mathf.Lerp(1.55f, 1.65f, u));
            }, 45f);

            yield return Shot("5 afastando para a boca do beco", 24f, Length, u =>
            {
                float k = Ease(u);
                Pose(new Vector3(Mathf.Lerp(mid.x + 1.2f, mid.x - 1.0f, k), Mathf.Lerp(1.3f, 2.2f, k), Mathf.Lerp(mid.z + 3.0f, mid.z + 9.5f, k)), mid + Vector3.up * 0.8f);
            }, 44f);

            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

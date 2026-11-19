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
    /// "firefilm": os barris de fogo nos becos. 5 planos: close orbitando um barril (chamas,
    /// faíscas e fumaça), a boca do beco da B vista da calçada, travelling entrando no beco, o beco
    /// da rua A e o beco da D já de noite. Anoitecer 0,62 → 0,8 (o fogo ilumina as paredes).
    /// Play pela Road: "300 Assets/Scenes/Road.unity firefilm". 4 s de aquecimento.
        /// "firetake": take único de 22 s na mão, entrando no beco da rua A até baixo do fogo.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadFireFilm
    {
        private const float Warmup = 4f;
        private const float Length = 30f;
        private const float TakeLength = 22f;
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        static RoadFireFilm() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "firefilm" && prep != "firetake") return;
            bool take = prep == "firetake";
            float len = take ? TakeLength : Length;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (player == null || Camera.main == null || GameObject.Find("BurnBarrels") == null) return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(Warmup + len + 1f, 24, true);
                rec.StartCoroutine(Film(player, take));
            }
            EditorApplication.update += Tick;
        }

        private static IEnumerator Film(PlayerController player, bool take)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[RoadFireFilm] {Time.time - t0:0.0}s {s}"); }
            float T() => Time.time - t0 - Warmup;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(258.5f, 0.5f, 240f), Quaternion.identity);
            if (cc != null) cc.enabled = true;

            Camera gameCam = Camera.main;
            var camGo = new GameObject("FireFilmCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(gameCam);
            cam.fieldOfView = 50f;
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
                        apply?.Invoke(nightfall, new object[] { take ? Mathf.Lerp(0.74f, 0.80f, Mathf.Clamp01(T() / TakeLength)) : Mathf.Lerp(0.62f, 0.80f, Mathf.Clamp01(T() / Length)) });
                    }
                    yield return null;
                }
            }
            host.StartCoroutine(Upkeep());

            Transform root = GameObject.Find("BurnBarrels").transform;
            Transform Barrel(int i) => root.childCount > i ? root.GetChild(i) : root.GetChild(0);
            foreach (Transform b in root) Ev($"{b.name} em ({b.position.x:0.0},{b.position.z:0.0})");

            void Pose(Vector3 pos, Vector3 look) => camGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            float Ease(float x) => x * x * (3f - 2f * x);
            IEnumerator Shot(string name, float from, float to, System.Action<float> frame, float fov = 50f)
            {
                while (T() < from) { yield return null; }
                Ev($"PLANO {name}");
                cam.fieldOfView = fov;
                while (T() < to) { frame(Mathf.Clamp01((T() - from) / (to - from))); yield return null; }
            }

            // Barris por beco (ordem de criação no RoadBurnBarrelSetup): 0,1 = B x145; 2 = D x-89; 3 = D x-16; 4,5 = A z201.
            Transform b0 = Barrel(0), b1 = Barrel(1), b2 = Barrel(2), b4 = Barrel(4);
            if (take)
            {
                // TAKE ÚNICO (câmera na mão): da calçada da rua A entra no beco (z≈201) andando até os
                // dois barris, abaixa e contorna o B_05 até terminar baixo, olhando a chama de baixo.
                Vector3 bA = b4.position, bB = Barrel(5).position, mid = (bA + bB) * 0.5f;
                Vector3 p0 = new Vector3(254.8f, 1.70f, 195.6f), p1 = new Vector3(259.4f, 1.15f, 200.9f);
                Vector3 rel = p1 - bA; rel.y = 0f;
                float r0 = rel.magnitude, a0 = Mathf.Atan2(rel.z, rel.x);
                Vector3 Shake(float t, float amt) => new Vector3(
                    (Mathf.PerlinNoise(t * 0.7f, 1.3f) - 0.5f), (Mathf.PerlinNoise(4.1f, t * 0.9f) - 0.5f) * 0.7f,
                    (Mathf.PerlinNoise(t * 0.6f, 8.8f) - 0.5f)) * amt;
                Pose(p0, mid + Vector3.up * 1.2f);
                yield return Shot("TAKE beco da A", 0f, TakeLength, u =>
                {
                    float t = T();
                    Vector3 pos, look;
                    if (u < 0.55f)
                    {
                        float k = Ease(u / 0.55f);
                        pos = Vector3.Lerp(p0, p1, k);
                        pos.y += Mathf.Abs(Mathf.Sin(t * 3.6f)) * 0.035f * (1f - k); // passos
                        look = Vector3.Lerp(mid + Vector3.up * 1.2f, bA + Vector3.up * 1.05f, k);
                    }
                    else
                    {
                        float k = Ease((u - 0.55f) / 0.45f);
                        float a = a0 + Mathf.Lerp(0f, 38f, k) * Mathf.Deg2Rad;
                        float r = Mathf.Lerp(r0, 1.75f, k);
                        pos = bA + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                        pos.y = Mathf.Lerp(1.15f, 0.82f, k);
                        look = bA + Vector3.up * Mathf.Lerp(1.05f, 1.22f, k);
                    }
                    pos += Shake(t, 0.035f);
                    Pose(pos, look + Shake(t + 20f, 0.05f));
                }, 46f);
                filming = false;
                Ev("CORTE_FIM");
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                yield break;
            }
            Pose(b0.position + new Vector3(1.6f, 1.3f, 1.2f), b0.position + Vector3.up * 0.9f);

            yield return Shot("1 close orbitando", 0f, 7f, u =>
            {
                float a = Mathf.Lerp(20f, 110f, Ease(u)) * Mathf.Deg2Rad;
                Vector3 c = b0.position;
                Pose(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Lerp(1.7f, 1.3f, u) + Vector3.up * Mathf.Lerp(1.5f, 1.15f, u),
                     c + Vector3.up * 0.95f);
            }, 45f);

            yield return Shot("2 boca do beco B (calçada)", 7f, 13f, u =>
            {
                // do outro lado da rua B, olhando para o beco (sul)
                Vector3 mouth = new Vector3(145f, 0f, 364.8f);
                Pose(new Vector3(Mathf.Lerp(141.5f, 143.5f, u), 1.6f, 371.5f), mouth + new Vector3(0f, 1.0f, -5f));
            });

            yield return Shot("3 entrando no beco B", 13f, 19f, u =>
            {
                Vector3 p = new Vector3(145f, 1.55f, Mathf.Lerp(366f, 361.5f, Ease(u)));
                Pose(p, Vector3.Lerp(b0.position, b1.position, u) + Vector3.up * 0.9f);
            });

            yield return Shot("4 beco da rua A", 19f, 25f, u =>
            {
                Vector3 c = (b4.position + Barrel(5).position) * 0.5f;
                Pose(new Vector3(Mathf.Lerp(256.5f, 258.5f, Ease(u)), 1.6f, 200.8f + Mathf.Lerp(-2.5f, -1.5f, u)), c + Vector3.up * 0.9f);
            });

            yield return Shot("5 beco da D, noite", 25f, Length, u =>
            {
                Vector3 c = b2.position;
                Pose(c + new Vector3(Mathf.Lerp(-2.4f, -1.6f, u), Mathf.Lerp(0.7f, 1.1f, u), Mathf.Lerp(3.2f, 2.4f, u)), c + Vector3.up * 1.0f);
            }, 42f);

            filming = false;
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

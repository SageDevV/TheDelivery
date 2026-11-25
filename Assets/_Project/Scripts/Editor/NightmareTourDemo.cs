using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "nightmaretourvideo" (2026-11-25): passeio por pontos fixos do corredor do pesadelo (spawn, parede do fundo,
    /// rosnado, meio da fuga, beira do abismo) para avaliar e conferir o acabamento. O diretor é parado (sem rosnado
    /// nem criatura). Marcador: "200 Assets/Scenes/Nightmare.unity nightmaretourvideo".
    /// Mundo: corredor em x 106,7, piso y 180,8, de z 835 (spawn, parede do fundo) a z 316 (beira, aberta).
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareTourDemo
    {
        static NightmareTourDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "nightmaretourvideo" && prep != "nightmarechasevideo") return;
            bool chase = prep == "nightmarechasevideo";
            double at = EditorApplication.timeSinceStartup + 1.0;
            void Wait()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Wait; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Wait;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.RenderActiveCamera = true;
                rec.StartCoroutine(chase ? Chase(rec) : Script(rec));
            }
            EditorApplication.update += Wait;
        }

        // (rótulo, z, olhar para z, desvio lateral do olhar em x, pitch, segundos)
        private static readonly (string label, float z, float lz, float lx, float pitch, float secs)[] Shots =
        {
            ("Spawn: o corredor à frente", 833f, 700f, 0f, 2f, 3.5f),
            ("O elevador atrás dela (andar 3)", 829f, 900f, 0f, -8f, 3f),
            ("Zona 1: portas 301, 302, 303", 826f, 790f, 0.6f, 4f, 3f),
            ("Zona 2: 302, 302, 302", 735f, 650f, 0f, 2f, 3f),
            ("Zona 2: a parede de cartazes", 577f, 573f, 1.6f, 4f, 4f),
            ("Zona 3: portas altas e tortas", 470f, 380f, 0f, -4f, 3f),
            ("Zona 3: garras e rachaduras", 509.5f, 506f, 1.6f, -4f, 3f),
            ("Zona 3: as folhas e o caderno", 409f, 405.9f, 0.5f, 45f, 3f),
            ("A beira", 323f, 300f, 0f, 18f, 3.5f),
            ("O abismo lá embaixo", 317.5f, 250f, 0f, 55f, 3f),
        };

        private static IEnumerator Script(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            var director = Object.FindFirstObjectByType<NightmareDirector>();
            if (director != null) director.StopAllCoroutines();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.5f);
            rec.Begin(Shots.Sum(s => s.secs) + 0.5f, 24, true);
            float t0 = Time.time;
            const float X = 106.68f, Y = 180.9f;
            foreach (var s in Shots)
            {
                var p = new Vector3(X, Y, s.z);
                cc.enabled = false;
                player.transform.position = p;
                var d = new Vector3(s.lx, 0f, s.lz - s.z);
                player.transform.rotation = Quaternion.LookRotation(d);
                cc.enabled = true;
                player.SyncCameraState(s.pitch, player.EyeHeight);
                log.AppendLine($"{Time.time - t0:0.0}  {s.label}");
                yield return new WaitForSeconds(s.secs);
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "nightmarechasevideo": o fluxo REAL do diretor. Ela é posta no ponto do rosnado (o diretor dispara o rosnado
        /// e libera a fuga); a fuga vira um LAPSO DE TEMPO — saltos de 30 m pelo corredor, olhando ora para a frente
        /// (calhas acesas adiante), ora para trás (as de trás apagando: o <c>corridorLights</c> pela distância que
        /// falta até a beira); no fim ela chega à beira e o diretor roda o agarrão inteiro até a Cafeteria.
        /// Marcador: "900 Assets/Scenes/Nightmare.unity nightmarechasevideo".
        /// </summary>
        private static IEnumerator Chase(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            var director = Object.FindFirstObjectByType<NightmareDirector>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var f = typeof(NightmareDirector).GetField("growlPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var growl = f?.GetValue(director) as Transform;
            var fa = typeof(NightmareDirector).GetField("abyssPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var abyss = fa?.GetValue(director) as Transform;
            rec.Begin(150f, 24, true);
            float t0 = Time.time;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[NightmareTourDemo] {Time.time - t0:0.0}s {s}"); }
            void Tp(Vector3 p, float yaw, float pitch)
            {
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.Euler(0, yaw, 0); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            Ev($"beat {director.CurrentBeat}: o spawn (2 s)");
            yield return new WaitForSeconds(2f);
            if (growl != null) Tp(growl.position, 180f, 2f);
            Ev("no ponto do rosnado");
            float w = Time.time;
            yield return new WaitUntil(() => director.CurrentBeat == NightmareBeat.TheChase || Time.time - w > 25f);
            Ev($"beat {director.CurrentBeat}: a fuga (lapso de tempo)");
            yield return new WaitForSeconds(1.5f);
            float x = growl != null ? growl.position.x : 106.46f, y = growl != null ? growl.position.y : 181.9f;
            float z = player.transform.position.z, zEnd = abyss != null ? abyss.position.z + 8f : 324f;
            int step = 0;
            while (z - 30f > zEnd && director.CurrentBeat == NightmareBeat.TheChase)
            {
                z -= 30f; step++;
                bool back = step % 3 == 0;
                Tp(new Vector3(x, y, z), back ? 0f : 180f, back ? 0f : 2f);
                yield return null; yield return null;
                var cl = typeof(NightmareDirector).GetField("corridorLights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(director) as Light[];
                int on = cl == null ? -1 : cl.Count(l => l != null && l.enabled);
                int behindOn = cl == null ? -1 : cl.Count(l => l != null && l.enabled && l.transform.position.z > z);
                Ev((back ? $"z {z:0}: olha para TRÁS" : $"z {z:0}: à frente") + $" | luzes acesas {on}/{cl?.Length}, atrás dela acesas {behindOn}");
                yield return new WaitForSeconds(back ? 2.0f : 1.4f);
            }
            if (director.CurrentBeat == NightmareBeat.TheChase && abyss != null)
            {
                Tp(new Vector3(abyss.position.x, y, abyss.position.z + 6f), 180f, 4f);
                Ev("perto da beira: anda até ela");
                for (int i = 0; i < 40 && director.CurrentBeat == NightmareBeat.TheChase; i++)
                {
                    var p = player.transform.position; p.z -= 0.15f;
                    cc.enabled = false; player.transform.position = p; cc.enabled = true;
                    yield return new WaitForSeconds(0.05f);
                }
            }
            Ev($"beat {director.CurrentBeat}");
            float g = Time.time;
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Cafeteria" || Time.time - g > 110f);
            Ev($"cena: {SceneManager.GetActiveScene().name}");
            yield return new WaitForSeconds(3f);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

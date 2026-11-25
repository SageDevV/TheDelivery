using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "cafeteriatourvideo" (2026-11-25): passeio por pontos fixos da Cafeteria (Ato 1) — porta, balcão, a mesa da
    /// Clear, janela, sofás e fliperama, paredes, teto e piso — para avaliar e conferir o acabamento. O diretor é parado
    /// (sem despertar nem Marina). Marcador: "200 Assets/Scenes/Cafeteria.unity cafeteriatourvideo".
    /// Sala: x 212,3–219,6 (janela em 212,2; porta em 219,7), z 90,45–101,45 (balcão em 90,4), piso y 0,115.
    /// </summary>
    [InitializeOnLoad]
    public static class CafeteriaTourDemo
    {
        static CafeteriaTourDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "cafeteriatourvideo") return;
            double at = EditorApplication.timeSinceStartup + 1.0;
            void Wait()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Wait; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Wait;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.RenderActiveCamera = true;
                rec.StartCoroutine(Script(rec));
            }
            EditorApplication.update += Wait;
        }

        // (rótulo, x, z, olhar x, olhar z, pitch, segundos)
        private static readonly (string label, float x, float z, float lx, float lz, float pitch, float secs)[] Shots =
        {
            ("Da porta: a sala inteira", 219.0f, 94.4f, 212.5f, 97.5f, 6f, 3.5f),
            ("O balcão, a vitrine e as prateleiras", 216.2f, 96.6f, 216.0f, 90.4f, 8f, 3.5f),
            ("A mesa da Clear (laptop e café)", 215.7f, 95.0f, 214.25f, 93.6f, 32f, 3f),
            ("A janela e a cortina", 215.2f, 94.4f, 212.2f, 94.6f, 4f, 3f),
            ("Sofás, tapete e fliperama", 214.3f, 96.3f, 217.8f, 100.8f, 10f, 3.5f),
            ("A parede dos fundos e os quadros", 216.0f, 97.0f, 216.0f, 101.5f, 0f, 3f),
            ("Da janela olhando a porta", 213.0f, 100.6f, 219.5f, 91.5f, 6f, 3.5f),
            ("O teto e as luminárias", 216.0f, 97.5f, 216.4f, 93.0f, -40f, 3f),
            ("O piso", 216.0f, 96.0f, 216.0f, 94.0f, 60f, 2.5f),
            ("Mesa da Clear, de cima", 214.25f, 94.55f, 214.25f, 93.64f, 58f, 3f),
            ("Mesa do meio, de cima", 216.58f, 96.45f, 216.58f, 95.59f, 58f, 2.5f),
            ("Mesa do jornal, de cima", 214.62f, 97.75f, 214.62f, 96.90f, 58f, 2.5f),
            ("Mesa do fliperama, de cima", 218.16f, 96.85f, 218.16f, 97.70f, 58f, 2.5f),
            ("Mesa perto do balcão, de cima", 217.27f, 94.50f, 217.27f, 93.63f, 58f, 2.5f),
        };

        private static IEnumerator Script(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            var director = Object.FindFirstObjectByType<Act1Director>();
            if (director != null) director.StopAllCoroutines();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.5f);
            rec.Begin(Shots.Sum(s => s.secs) + 0.5f, 24, true);
            float t0 = Time.time;
            foreach (var s in Shots)
            {
                cc.enabled = false;
                player.transform.position = new Vector3(s.x, 0.17f, s.z);
                player.transform.rotation = Quaternion.LookRotation(new Vector3(s.lx - s.x, 0f, s.lz - s.z));
                cc.enabled = true;
                player.SyncCameraState(s.pitch, player.EyeHeight);
                // o despertar deixa o CameraHolder tombado (roll 70°) e, com CanMove falso, o PlayerController não mexe
                // nele: endireita aqui
                var cam = player.CameraHolder;
                if (cam != null) { cam.localPosition = new Vector3(0f, player.EyeHeight, 0f); cam.localRotation = Quaternion.Euler(s.pitch, 0f, 0f); }
                log.AppendLine($"{Time.time - t0:0.0}  {s.label}");
                yield return new WaitForSeconds(s.secs);
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

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
    /// "roadtourvideo" (2026-11-25): passeio pelo TRAJETO da Clear no Interlúdio, na altura dos olhos — saída do café,
    /// as quatro ruas (x≈243, z≈372, x≈−27, z≈98, x≈−154), os pontos de ônibus, o estranho e a entrada do prédio — para
    /// avaliar e conferir o acabamento da rua. O diretor é parado (sem pensamentos nem encontro); o anoitecer segue.
    /// Marcador: "400 Assets/Scenes/Road.unity roadtourvideo".
    /// </summary>
    [InitializeOnLoad]
    public static class RoadTourDemo
    {
        static RoadTourDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "roadtourvideo") return;
            double at = EditorApplication.timeSinceStartup + 1.0;
            void Wait()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Wait; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                if (Object.FindFirstObjectByType<PlayerController>() == null) return;
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
            ("Saída do café (R. das Gaivotas)", 235.6f, 318.1f, 235.6f, 360f, 2f, 3.5f),
            ("R. das Gaivotas", 235.7f, 338f, 235.7f, 380f, 2f, 3f),
            ("Esquina 1: placa e semáforo", 235.7f, 352f, 236.6f, 366f, 4f, 3f),
            ("Av. dos Pescadores", 200f, 364.6f, 150f, 362f, 2f, 3f),
            ("Av. dos Pescadores: comércio", 120f, 364.6f, 85f, 360f, 2f, 3f),
            ("Ponto B e o anúncio do The Delivery", 30f, 378.3f, 12f, 378.6f, 4f, 3f),
            ("Esquina 2", -12f, 364.6f, -21.6f, 366f, 4f, 3f),
            ("R. do Farol", -34.5f, 340f, -34.5f, 295f, 2f, 3f),
            ("Ponto C", -34.5f, 314f, -34.9f, 302f, 4f, 3f),
            ("R. do Farol, meio", -34.5f, 200f, -34.5f, 160f, 2f, 3f),
            ("Esquina 3: agora direita", -34.5f, 118f, -34f, 104.5f, 4f, 3f),
            ("R. das Marés", -60f, 105.2f, -110f, 105.2f, 2f, 3f),
            ("O estranho", -91.4f, 76f, -94.2f, 65.7f, 4f, 3f),
            ("R. do Porto", -156f, 108f, -156f, 150f, 2f, 3f),
            ("A banca: a manchete da Helena", -11.0f, 377.6f, -13f, 380.2f, 14f, 3.5f),
            ("A portaria do Edifício Porto Sereno", -158.8f, 151.5f, -153.4f, 157.8f, -2f, 3.5f),
            ("A portaria, de frente", -159.8f, 157.8f, -153.4f, 157.8f, -4f, 3.5f),
        };

        private static IEnumerator Script(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            var director = Object.FindFirstObjectByType<JourneyDirector>();
            if (director != null) director.StopAllCoroutines();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.5f);
            rec.Begin(Shots.Sum(s => s.secs) + 0.5f, 24, true);
            float t0 = Time.time;
            foreach (var s in Shots)
            {
                float y = 0.1f;
                if (Physics.Raycast(new Vector3(s.x, 50f, s.z), Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore)) y = hit.point.y + 0.05f;
                cc.enabled = false;
                player.transform.position = new Vector3(s.x, y, s.z);
                player.transform.rotation = Quaternion.LookRotation(new Vector3(s.lx - s.x, 0f, s.lz - s.z));
                cc.enabled = true;
                player.SyncCameraState(s.pitch, player.EyeHeight);
                var cam = player.CameraHolder;
                if (cam != null) { cam.localPosition = new Vector3(0f, player.EyeHeight, 0f); cam.localRotation = Quaternion.Euler(s.pitch, 0f, 0f); }
                log.AppendLine($"{Time.time - t0:0.0}  {s.label}  (chão y {y:0.00})");
                yield return new WaitForSeconds(s.secs);
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

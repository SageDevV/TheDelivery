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
    /// "receptiontourvideo" (2026-11-25): passeio por pontos fixos da Recepção (Ato 2) — entrada, balcão, espera,
    /// elevador, porta de vidro, sala de produtos — para avaliar e conferir o acabamento. O diretor é parado (sem
    /// pensamentos/susto). Marcador: "200 Assets/Scenes/Reception.unity receptiontourvideo".
    /// </summary>
    [InitializeOnLoad]
    public static class ReceptionTourDemo
    {
        static ReceptionTourDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "receptiontourvideo") return;
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

        private static readonly (string label, float x, float z, float lx, float lz, float pitch, float secs)[] Shots =
        {
            ("Entrada: o saguão inteiro", 0.0f, 3.2f, 0.0f, -4.0f, 6f, 3.5f),
            ("Ripado com o nome do prédio", -1.4f, -0.2f, -2.6f, 3.9f, -8f, 3f),
            ("Balcão: a mesa do porteiro", -2.4f, 3.45f, -2.6f, 2.2f, 38f, 3.5f),
            ("Balcão visto do saguão", -0.4f, 0.8f, -3.0f, 2.8f, 12f, 3f),
            ("Caixas de correio (302)", -2.4f, -0.6f, -4.0f, -0.6f, 10f, 3f),
            ("Quadro de avisos e o cartaz", -3.15f, -2.35f, -4.0f, -2.35f, 8f, 4f),
            ("Área de espera", -1.2f, -0.2f, 3.0f, -2.6f, 10f, 3f),
            ("Elevador: botoeira e indicador", 1.2f, 1.6f, 4.0f, 1.9f, -2f, 3f),
            ("Porta de vidro, SAÍDA, câmera", 0.0f, -2.6f, 0.0f, 4.0f, -6f, 3f),
            ("Porta do depósito, piso molhado, extintor", 0.9f, -1.6f, -0.5f, -4.0f, 12f, 3.5f),
            ("Sala de produtos", -0.8f, -4.5f, -2.4f, -7.8f, 10f, 3f),
            ("Sala: balde e esfregão", -1.7f, -5.3f, -0.3f, -6.3f, 24f, 3f),
            ("Sala: galões e quadro de luz", -1.0f, -6.2f, -3.3f, -5.2f, 8f, 3f),
        };

        private static IEnumerator Script(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            var director = Object.FindFirstObjectByType<Act2Director>();
            if (director != null) director.StopAllCoroutines();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.5f);
            rec.Begin(Shots.Sum(s => s.secs) + 0.5f, 24, true);
            float t0 = Time.time;
            foreach (var s in Shots)
            {
                var p = new Vector3(s.x, 0.05f, s.z);
                cc.enabled = false;
                player.transform.position = p;
                var d = new Vector3(s.lx - s.x, 0f, s.lz - s.z);
                player.transform.rotation = Quaternion.LookRotation(d);
                cc.enabled = true;
                player.SyncCameraState(s.pitch, player.EyeHeight);
                log.AppendLine($"{Time.time - t0:0.0}  {s.label}");
                yield return new WaitForSeconds(s.secs);
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }
    }
}

using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Interaction;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "sweepfixvideo" (2026-11-26): os sons novos da varredura global no Ato 1 (fliperama e notebook ligando e
    /// desligando, levantar da cadeira) e no Ato 3 (sentar e levantar do sofá). Play pela Boot:
    /// "150 Assets/Scenes/Boot.unity sweepfixvideo". O menu fica no prep "menuvideo". Tempos em Temp/DemoFrames/log.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class SweepFixDemo
    {
        static SweepFixDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "sweepfixvideo") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            var log = new StringBuilder(); float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log("[SweepFixDemo] " + s); }
            var seat = typeof(SittableChair).GetMethod("PlaySeatSound", BindingFlags.Instance | BindingFlags.NonPublic);
            var sitF = typeof(SittableChair).GetField("sitSound", BindingFlags.Instance | BindingFlags.NonPublic);

            IEnumerator LookAt(Transform target, float back)
            {
                var pc = Object.FindFirstObjectByType<PlayerController>();
                if (pc == null || target == null) yield break;
                var cc = pc.GetComponent<CharacterController>();
                Vector3 d = target.position - Camera.main.transform.position; d.y = 0f;
                Vector3 fwd = target.forward; fwd.y = 0f;
                Vector3 pos = target.position + (fwd.sqrMagnitude > 0.01f ? fwd.normalized : -d.normalized) * back;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                pos.y = pc.transform.position.y;
                cc.enabled = false; pc.transform.position = pos;
                Vector3 look = target.position - pos; look.y = 0f;
                if (look.sqrMagnitude > 0.01f) pc.transform.rotation = Quaternion.LookRotation(look);
                cc.enabled = true;
                pc.SyncCameraState(12f, pc.EyeHeight);
                yield return null;
            }

            yield return new WaitForSeconds(2.5f);
            GameManager.Instance.GoToAct(GameAct.Act1, GameScene.Cafeteria);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Cafeteria");
            yield return new WaitForSeconds(3f);
            rec.Begin(150f, 24, true); t0 = Time.time;
            Ev("Ato 1 — Cafeteria");

            foreach (var tv in Object.FindObjectsByType<TVScreen>(FindObjectsSortMode.None))
            {
                yield return LookAt(tv.transform, 1.3f);
                Ev($"{tv.name}: desliga e liga");
                yield return new WaitForSeconds(1.2f);
                tv.Toggle(); yield return new WaitForSeconds(1.6f);
                tv.Toggle(); yield return new WaitForSeconds(1.8f);
            }
            int k = 0;
            foreach (var ch in Object.FindObjectsByType<SittableChair>(FindObjectsSortMode.None))
            {
                if (k++ >= 2) break;
                yield return LookAt(ch.transform, 1.2f);
                Ev($"{ch.name}: levantar (arrastar a cadeira)");
                yield return new WaitForSeconds(1f);
                ch.PlayStandSound(); yield return new WaitForSeconds(2.2f);
            }

            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment");
            yield return new WaitForSeconds(3f);
            Ev("Ato 3 — Apartamento");
            foreach (var ch in Object.FindObjectsByType<SittableChair>(FindObjectsSortMode.None))
            {
                if (!ch.name.Contains("Sofa")) continue;
                yield return LookAt(ch.transform, 1.4f);
                Ev("Sofá: sentar");
                yield return new WaitForSeconds(1f);
                seat.Invoke(ch, new object[] { sitF.GetValue(ch) }); yield return new WaitForSeconds(2.2f);
                Ev("Sofá: levantar");
                ch.PlayStandSound(); yield return new WaitForSeconds(2.5f);
            }
            Ev("FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
            rec.FinishEarly();
        }
    }
}

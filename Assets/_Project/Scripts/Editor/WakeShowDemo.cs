using System.Collections;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"wakeshow" (2026-11-26): o despertar do Ato 5 — caída no chão da cela, atordoada, levanta com o celular.</summary>
    [InitializeOnLoad]
    public static class WakeShowDemo
    {
        static WakeShowDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "wakeshow") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            yield return new WaitForSeconds(2f);
            rec.Begin(60f, 24, true);
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Escape" && Object.FindAnyObjectByType<Act5Director>() != null);
            var d = Object.FindAnyObjectByType<Act5Director>();
            float t0 = Time.time;
            while (d.CurrentBeat != Act5Beat.CellPuzzle && Time.time - t0 < 50f) yield return null;
            Debug.Log($"[WakeShow] de pé em {Time.time - t0:0.0}s");
            yield return new WaitForSeconds(3f);
            rec.FinishEarly();
        }
    }
}

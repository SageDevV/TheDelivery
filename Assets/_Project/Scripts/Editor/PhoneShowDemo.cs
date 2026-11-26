using System.Collections;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"phoneshow" (2026-11-26): o celular na mão dela iluminando a cela no breu — olha em volta, anda até as grades,
    /// aponta para o corredor.</summary>
    [InitializeOnLoad]
    public static class PhoneShowDemo
    {
        static PhoneShowDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "phoneshow") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Escape" && Object.FindAnyObjectByType<Act5Director>() != null);
            var d = Object.FindAnyObjectByType<Act5Director>();
            while (d.CurrentBeat != Act5Beat.CellPuzzle) yield return null;
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            rec.Begin(20f, 24, true);
            float yaw0 = pc.transform.eulerAngles.y;
            IEnumerator Look(float yawTo, float pitchTo, float secs)
            {
                float ys = pc.transform.eulerAngles.y, ps = pc.Pitch;
                for (float t = 0; t < secs; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0, 1, t / secs);
                    pc.transform.rotation = Quaternion.Euler(0, Mathf.LerpAngle(ys, yawTo, k), 0);
                    pc.SyncCameraState(Mathf.Lerp(ps, pitchTo, k), pc.EyeHeight);
                    yield return null;
                }
            }
            yield return Look(yaw0, 5f, 1.5f);                 // para a frente (grades)
            yield return Look(yaw0 - 70f, 10f, 2f);            // parede da esquerda
            yield return Look(yaw0 - 150f, 25f, 2.5f);         // fundo da cela (colchão)
            yield return Look(yaw0 + 60f, 30f, 2.5f);          // balde
            yield return Look(yaw0, 45f, 1.5f);                // olha para baixo: a mão e o celular
            yield return new WaitForSeconds(1f);
            yield return Look(yaw0, 3f, 1.2f);
            // anda até as grades e aponta para o corredor
            Vector3 a = pc.transform.position, b = new Vector3(-21.6f, a.y, -14.8f);
            for (float t = 0; t < 2.5f; t += Time.deltaTime)
            {
                cc.enabled = false; pc.transform.position = Vector3.Lerp(a, b, t / 2.5f); cc.enabled = true;
                yield return null;
            }
            yield return Look(yaw0 + 15f, 2f, 2f);
            yield return new WaitForSeconds(1.5f);
            rec.FinishEarly();
        }
    }
}

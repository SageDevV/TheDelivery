using System.Collections;
using System.Linq;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"lightchase" (2026-11-26): o Lenin a ~15 m. Lanterna apagada: ele não a nota. F acende: ele vê a luz e
    /// entra em perseguição na hora. (A demo o suspende a 3 m para não acabar em game over.)</summary>
    [InitializeOnLoad]
    public static class LightChaseDemo
    {
        static LightChaseDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "lightchase") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator PressF()
        {
            var kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Escape" && Object.FindAnyObjectByType<Act5Director>() != null);
            var d = Object.FindAnyObjectByType<Act5Director>();
            while (d.CurrentBeat != Act5Beat.CellPuzzle) yield return null;
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            var fl = Object.FindAnyObjectByType<PhoneFlashlight>();
            // a Clear no começo do corredor, olhando para o leste (a porta da casa de máquinas)
            cc.enabled = false; pc.transform.SetPositionAndRotation(new Vector3(-15.8f, 0f, -14.1f), Quaternion.Euler(0f, 90f, 0f)); cc.enabled = true;
            pc.SyncCameraState(2f, pc.EyeHeight);
            if (fl.On) yield return PressF();                                  // começa APAGADA (antes de ele acordar)
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[LightChase] antes de acordar: lanterna={(fl.On ? "ACESA" : "apagada")}");
            // o Lenin acordado, patrulhando a ~15 m, de lado para ela
            var ai = Object.FindObjectsByType<AntagonistAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
            ai.gameObject.SetActive(true);
            ai.transform.SetPositionAndRotation(new Vector3(-0.8f, 0f, -14.2f), Quaternion.Euler(0f, 0f, 0f));
            var agent = ai.GetComponent<UnityEngine.AI.NavMeshAgent>(); agent.enabled = true; agent.Warp(ai.transform.position);
            ai.ResumeFromDirector();
            var vision = ai.GetComponent<AIVision>();
            yield return null;
            agent.isStopped = true;                                             // parado na patrulha, longe dela (só a luz o faz notar)
            yield return new WaitForSeconds(0.3f);
            rec.Begin(20f, 24, true);
            float t0 = Time.time;
            void Log(string tag) => Debug.Log($"[LightChase] {Time.time - t0:0.0}s {tag} lanterna={(fl.On ? "ACESA" : "apagada")} estado={ai.CurrentState} dist={Vector3.Distance(ai.transform.position, pc.transform.position):0.0} pela_luz={vision.SawByLight}");
            for (int i = 0; i < 8; i++) { Log("apagada"); yield return new WaitForSeconds(0.5f); }
            yield return PressF();                                              // ACENDE
            for (int i = 0; i < 30; i++)
            {
                Log("acesa");
                if (Vector3.Distance(ai.transform.position, pc.transform.position) < 3f) { ai.SuspendForDirector(); Log("(demo) suspenso a 3 m"); break; }
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(2f);
            rec.FinishEarly();
        }
    }
}

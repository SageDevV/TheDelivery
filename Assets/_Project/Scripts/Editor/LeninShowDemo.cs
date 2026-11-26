using System.Collections;
using System.IO;
using System.Linq;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"leninshow" (2026-11-26): o Lenin (corpo novo) parado, andando e correndo até a cela, visto de dentro dela.</summary>
    [InitializeOnLoad]
    public static class LeninShowDemo
    {
        static LeninShowDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "leninshow") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            Debug.Log($"[LeninShow] início timeScale={Time.timeScale} frame={Time.frameCount}"); for (int i = 0; i < 4; i++) { yield return new WaitForSecondsRealtime(0.5f); Debug.Log($"[LeninShow] t={Time.time:0.00} timeScale={Time.timeScale} frame={Time.frameCount}"); }
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Escape" && Object.FindAnyObjectByType<Act5Director>() != null);
            Debug.Log("[LeninShow] Escape pronta"); yield return new WaitForSeconds(0.5f);
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var lenin = Object.FindObjectsByType<AntagonistAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
            var thoughts = ThoughtSystem.Instance;
            // de pé na cela, olhando para fora pelas grades
            var cc = pc.GetComponent<CharacterController>();
            cc.enabled = false; pc.transform.SetPositionAndRotation(new Vector3(-22.2f, 0f, -14.6f), Quaternion.Euler(0, 90, 0)); cc.enabled = true;
            pc.CanMove = false; pc.SyncCameraState(4f, pc.EyeHeight);
            // ele aparece no fundo do corredor
            lenin.gameObject.SetActive(true);
            var agent = lenin.GetComponent<UnityEngine.AI.NavMeshAgent>();
            lenin.transform.SetPositionAndRotation(new Vector3(-6f, 0f, -14f), Quaternion.Euler(0, 270, 0));
            agent.enabled = true; agent.Warp(lenin.transform.position);
            lenin.SuspendForDirector();
            var anim = lenin.GetComponentInChildren<Animator>();
            rec.Begin(16f, 24, true);
            Debug.Log($"[LeninShow] corpo={anim?.gameObject.name} avatar={anim?.avatar?.name} altura renderers={Height(lenin.gameObject):0.00} m");
            // 1) parado 2 s
            agent.isStopped = true; anim.SetFloat("Speed", 0f);
            Debug.Log($"[LeninShow] início timeScale={Time.timeScale} frame={Time.frameCount}"); for (int i = 0; i < 4; i++) { yield return new WaitForSecondsRealtime(0.5f); Debug.Log($"[LeninShow] t={Time.time:0.00} timeScale={Time.timeScale} frame={Time.frameCount}"); }
            // 2) anda até x -12
            agent.isStopped = false; agent.speed = 1.6f; agent.SetDestination(new Vector3(-12f, 0f, -14f));
            for (float t = 0; t < 6f && agent.remainingDistance > 0.3f || t < 0.5f; t += Time.deltaTime) { anim.SetFloat("Speed", agent.velocity.magnitude); yield return null; }
            Debug.Log($"[LeninShow] andou: pos={lenin.transform.position} altura={Height(lenin.gameObject):0.00} m");
            // 3) corre até as grades
            anim.SetBool("IsChasing", true); agent.speed = 4.5f; agent.SetDestination(new Vector3(-20.3f, 0f, -14.6f));
            for (float t = 0; t < 5f && (agent.remainingDistance > 0.3f || t < 0.5f); t += Time.deltaTime) { anim.SetFloat("Speed", agent.velocity.magnitude); yield return null; }
            anim.SetFloat("Speed", 0f); anim.SetBool("IsChasing", false); agent.isStopped = true;
            yield return new WaitForSeconds(3f);
            rec.FinishEarly();
        }

        private static float Height(GameObject go)
        {
            var rs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b.size.y;
        }
    }
}

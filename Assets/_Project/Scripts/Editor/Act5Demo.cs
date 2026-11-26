using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "act5video" (2026-11-26): o Ato 5 (greybox) jogado por roteiro. Play pela Boot:
    /// "600 Assets/Scenes/Boot.unity act5video". Acorda na cela; resolve o puzzle (chapa/barra, balde/arame, gancho, chave,
    /// cela); liga G1 e G2 (sinais do Lenin); liga G3 (o Lenin acorda e caça — na demo ele é suspenso depois de alguns
    /// segundos para o roteiro seguir); G4 na água; G5; o portão abre; o convés e o gancho do Ato 6.
    /// </summary>
    [InitializeOnLoad]
    public static class Act5Demo
    {
        static Act5Demo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "act5video") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            var log = new StringBuilder(); float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log("[Act5Demo] " + s); }
            yield return new WaitForSeconds(2.5f);
            rec.Begin(480f, 24, true); t0 = Time.time;
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Escape" && Object.FindAnyObjectByType<Act5Director>() != null);
            var d = Object.FindAnyObjectByType<Act5Director>();
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            Ev("Escape carregada — " + d.CurrentBeat);

            IEnumerator WalkTo(Vector3 target, float speed = 2.4f)
            {
                var path = new UnityEngine.AI.NavMeshPath();
                Vector3 from = pc.transform.position;
                Vector3 fA = UnityEngine.AI.NavMesh.SamplePosition(from, out var hf, 3f, UnityEngine.AI.NavMesh.AllAreas) ? hf.position : from;
                Vector3 tA = UnityEngine.AI.NavMesh.SamplePosition(target, out var ht, 3f, UnityEngine.AI.NavMesh.AllAreas) ? ht.position : new Vector3(target.x, from.y, target.z);
                UnityEngine.AI.NavMesh.CalculatePath(fA, tA, UnityEngine.AI.NavMesh.AllAreas, path);
                var pts = path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete && path.corners.Length > 1 ? path.corners : new[] { fA, tA };
                for (int i = 1; i < pts.Length; i++)
                {
                    Vector3 a = pts[i - 1], b = pts[i]; float len = Vector3.Distance(a, b);
                    for (float u = 0f; u < len; u += speed * Time.deltaTime)
                    {
                        Vector3 dir = b - a; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.001f) pc.transform.rotation = Quaternion.Slerp(pc.transform.rotation, Quaternion.LookRotation(dir), 7f * Time.deltaTime);
                        cc.enabled = false; pc.transform.position = Vector3.Lerp(a, b, u / Mathf.Max(0.01f, len)); cc.enabled = true;
                        yield return null;
                    }
                }
            }
            void Face(Vector3 p, float pitch = 8f)
            {
                Vector3 dir = p - pc.transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) pc.transform.rotation = Quaternion.LookRotation(dir);
                pc.SyncCameraState(pitch, pc.EyeHeight);
            }
            IEnumerator Use(string objName, float approach = 1.1f, float pitch = 12f)
            {
                var go = GameObject.Find(objName);
                if (go == null) { Ev("(não achei " + objName + ")"); yield break; }
                Vector3 p = go.transform.position;
                Vector3 toMe = pc.transform.position - p; toMe.y = 0f;
                Vector3 stand = p + toMe.normalized * approach;
                yield return WalkTo(stand);
                Face(p, pitch);
                yield return new WaitForSeconds(0.7f);
                var it = go.GetComponentInParent<TheDelivery.Interaction.IInteractable>();
                if (it != null && it.CanInteract) { it.Interact(null); Ev("Usou: " + objName); }
                yield return new WaitForSeconds(0.4f);
                while (ThoughtSystem.Instance != null && ThoughtSystem.Instance.IsShowing) yield return null;
                yield return new WaitForSeconds(0.5f);
            }

            // 1. acordar
            while (d.CurrentBeat != Act5Beat.CellPuzzle) yield return null;
            Ev("Cela: o puzzle");
            yield return new WaitForSeconds(1f);
            yield return Use("Mattress", 1.2f, 30f);
            yield return Use("LoosePanel", 1.0f, 25f);
            yield return Use("Bucket", 1.0f, 35f);
            // tenta a fechadura sem chave (o pensamento) e depois alcança a chave
            yield return Use("KeyReach", 0.9f, 5f);
            yield return Use("LockInteract", 0.9f, 10f);
            while (d.CurrentBeat == Act5Beat.CellPuzzle) yield return null;
            Ev("Cela aberta — " + d.CurrentBeat);
            yield return new WaitForSeconds(2f);

            // 2. G1 e G2 (sinais)
            foreach (var g in new[] { "Generator_1", "Generator_2" })
            {
                yield return Use(g, 1.4f, 15f);
                yield return new WaitForSeconds(3.6f);
                Ev(g + " ligado — " + d.CurrentBeat);
            }
            // 3. G3: o Lenin acorda
            yield return Use("Generator_3", 1.4f, 15f);
            yield return new WaitForSeconds(3.6f);
            Ev("G3 ligado — " + d.CurrentBeat);
            var ai = Object.FindObjectsByType<AntagonistAI>(FindObjectsSortMode.None).FirstOrDefault(a => a.isActiveAndEnabled);
            if (ai != null)
            {
                Ev($"O Lenin está acordado: {ai.name} ({ai.CurrentState}) a {Vector3.Distance(ai.transform.position, pc.transform.position):0.0} m");
                // deixa ele vir (ouviu o gerador) e olha para ele quando estiver perto
                for (float w = 0f; w < 25f && Vector3.Distance(ai.transform.position, pc.transform.position) > 10f; w += Time.deltaTime) yield return null;
                Face(ai.transform.position, 2f);
                Ev($"O Lenin chegou perto ({ai.CurrentState}, {Vector3.Distance(ai.transform.position, pc.transform.position):0.0} m)");
                yield return new WaitForSeconds(1.0f);
                ai.SuspendForDirector();                         // só na demo: para o roteiro seguir sem ser pega
                Ev("(demo) Lenin suspenso");
            }
            // 4. G4 na água, G5
            yield return Use("Generator_4", 1.4f, 15f);
            yield return new WaitForSeconds(3.6f);
            yield return Use("Generator_5", 1.4f, 15f);
            yield return new WaitForSeconds(4f);
            Ev("5 geradores — " + d.CurrentBeat);
            // 5. o portão e o convés
            var gate = GameObject.Find("GateLeaf");
            if (gate != null) { yield return WalkTo(gate.transform.position + new Vector3(-3f, 0, 0)); Face(gate.transform.position, 0f); }
            yield return new WaitForSeconds(5f);
            yield return WalkTo(new Vector3(35f, 0f, -8f));
            Ev("No convés — " + d.CurrentBeat);
            yield return new WaitForSeconds(16f);
            Ev("FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
            rec.FinishEarly();
        }
    }
}

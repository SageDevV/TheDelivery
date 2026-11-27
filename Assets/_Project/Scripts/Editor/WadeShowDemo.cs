using System.Collections;
using TheDelivery.Act5;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"wadeshow" (2026-11-26): a Clear ANDANDO DE VERDADE (ScriptedMove, com passos do PlayerController) da casa de
    /// máquinas para dentro da área inundada pela rampa: passos no aço → passos na água, ondulação e gotículas em volta dela.</summary>
    [InitializeOnLoad]
    public static class WadeShowDemo
    {
        static WadeShowDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "wadeshow") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator HoldPitch(PlayerController pc, System.Func<float> pitch)
        {
            var wait = new WaitForEndOfFrame();
            while (pc != null) { if (pc.CameraHolder != null) pc.CameraHolder.localRotation = Quaternion.Euler(pitch(), 0f, 0f); yield return null; }
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
            cc.enabled = false; pc.transform.SetPositionAndRotation(new Vector3(10f, 0.05f, -6.5f), Quaternion.Euler(0, 0, 0)); cc.enabled = true;
            pc.CanMove = false; pc.SyncCameraState(18f, pc.EyeHeight);
            yield return new WaitForSeconds(0.5f);
            rec.Begin(22f, 24, true);
            var wz = Object.FindAnyObjectByType<WaterZone>();
            Debug.Log($"[WadeShow] WaterZone={(wz != null)} passos atuais={pc.CurrentFootstepClips?.Length}");
            // com CanMove false o olhar do jogador não é aplicado: a demo inclina a câmera direto, todo quadro
            float pitch = 12f;
            rec.StartCoroutine(HoldPitch(pc, () => pitch));
            pc.ScriptedMove = new Vector2(0f, 1f);                     // anda para o norte: aço → rampa → água
            float t0 = Time.time; bool logged = false;
            while (Time.time - t0 < 11f)
            {
                if (!logged && pc.transform.position.y < -0.5f) { logged = true; Debug.Log($"[WadeShow] na água em {pc.transform.position} passos={pc.CurrentFootstepClips?[0]?.name}"); }
                if (Time.frameCount % 24 == 0) Debug.Log($"[WadeShow] pos={pc.transform.position} aneis={WaterRipples.Active()} moving={pc.IsMoving}");
                pitch = Time.time - t0 > 3.5f ? Mathf.MoveTowards(pitch, 38f, 14f * Time.deltaTime) : 12f;
                yield return null;
            }
            pc.ScriptedMove = new Vector2(0.6f, 0.6f);                  // vira um pouco e continua
            yield return new WaitForSeconds(4f);
            pc.ScriptedMove = Vector2.zero;                             // para: a água ainda se mexe em volta dela
            yield return new WaitForSeconds(4f);
            Debug.Log($"[WadeShow] fim em {pc.transform.position}");
            rec.FinishEarly();
        }
    }
}

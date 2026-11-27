using System.Collections;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"dresstour" (2026-11-26): passeio pelos assets de decoração do navio, parando em cada canto com a lanterna.</summary>
    [InitializeOnLoad]
    public static class DressTourDemo
    {
        static DressTourDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "dresstour") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static readonly (Vector3 from, Vector3 at, string what)[] Stops =
        {
            (new Vector3(-19.6f, 0f, -14.6f), new Vector3(-17f, 1.0f, -14f), "porta estanque das celas"),
            (new Vector3(-16f, 0f, -14.2f), new Vector3(-6f, 1.6f, -14.4f), "corredor: duto, luminárias, extintor, placa"),
            (new Vector3(11.8f, 0f, -16.5f), new Vector3(6.5f, 1.8f, -21f), "casa de máquinas: escada e passarela"),
            (new Vector3(2.2f, 0f, -17.4f), new Vector3(0.2f, 0.2f, -21f), "ferramentas junto ao gerador 1"),
            (new Vector3(3f, 0f, -5.2f), new Vector3(3f, 1.8f, -2.3f), "quadro de disjuntores e PERIGO"),
            (new Vector3(16.6f, 0f, -14.2f), new Vector3(20.5f, 0.5f, -10.3f), "refeitório: mesa e cadeira tombada"),
            (new Vector3(24.2f, 0f, -16f), new Vector3(27.3f, 1.0f, -17.2f), "rádio velho"),
            (new Vector3(17.6f, 0f, -7.5f), new Vector3(14.3f, 1.6f, -7.5f), "boia e calendário"),
            (new Vector3(-14.2f, 0f, 13.6f), new Vector3(-12f, 0.8f, 17.4f), "porão: caixote com lona e corrente"),
            (new Vector3(-15.6f, 0f, 16.2f), new Vector3(-18f, 0.4f, 18.6f), "palete com sacos"),
            (new Vector3(13.2f, -0.9f, 10.8f), new Vector3(14f, -0.03f, 14f), "tábuas boiando"),
            (new Vector3(14.5f, -0.9f, 15.5f), new Vector3(15f, 2.0f, 19.7f), "escotilhas"),
            (new Vector3(31f, 0f, -5.8f), new Vector3(33f, 0.75f, -2.2f), "boia no convés"),
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
            rec.Begin(Stops.Length * 4.2f + 2f, 24, true);
            foreach (var s in Stops)
            {
                Vector3 dir = s.at - s.from; Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                cc.enabled = false; pc.transform.SetPositionAndRotation(s.from, Quaternion.LookRotation(flat)); cc.enabled = true;
                float eye = s.from.y + pc.EyeHeight;
                float pitch = -Mathf.Atan2(s.at.y - eye, flat.magnitude) * Mathf.Rad2Deg;
                pc.SyncCameraState(pitch, pc.EyeHeight);
                Debug.Log("[DressTour] " + s.what);
                yield return new WaitForSeconds(4.2f);
            }
            rec.FinishEarly();
        }
    }
}

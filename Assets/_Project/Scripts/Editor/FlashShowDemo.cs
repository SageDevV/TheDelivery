using System.Collections;
using System.Linq;
using TheDelivery.Act5;
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
    /// <summary>"flashshow" (2026-11-26): a tecla F de verdade (evento do Input System) — sem alvo liga/desliga a lanterna;
    /// mirando o balde, interage e a lanterna fica como está.</summary>
    [InitializeOnLoad]
    public static class FlashShowDemo
    {
        static FlashShowDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "flashshow") return;
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
            yield return new WaitForSeconds(1.5f);
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            var fl = Object.FindAnyObjectByType<PhoneFlashlight>();
            void Place(Vector3 pos, float yaw, float pitch)
            { cc.enabled = false; pc.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); cc.enabled = true; pc.SyncCameraState(pitch, pc.EyeHeight); }

            Place(new Vector3(-23.6f, 0f, -15.4f), 180f, -4f);            // de frente para a parede sul da cela (nada interativo)
            rec.Begin(24f, 24, true);
            yield return new WaitForSeconds(2f);
            for (int i = 0; i < 3; i++)
            {
                yield return PressF();
                yield return new WaitForSeconds(0.2f);
                Debug.Log($"[FlashShow] F sem alvo -> lanterna {(fl.On ? "ACESA" : "APAGADA")}");
                yield return new WaitForSeconds(2.3f);
            }
            yield return PressF();                                           // volta a acender
            yield return new WaitForSeconds(0.2f);
            Debug.Log($"[FlashShow] F sem alvo -> lanterna {(fl.On ? "ACESA" : "APAGADA")}");
            yield return new WaitForSeconds(1.5f);
            // mirando o balde: interage (a lanterna fica como está)
            var bucket = GameObject.Find("Bucket");
            Vector3 b = bucket.transform.position; Vector3 from = b + new Vector3(0.55f, 0f, -0.55f);
            Vector3 dir = b - from; dir.y = 0f;
            cc.enabled = false; pc.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir)); cc.enabled = true;
            pc.SyncCameraState(Mathf.Atan2(pc.EyeHeight - 0.6f, dir.magnitude) * Mathf.Rad2Deg, pc.EyeHeight);
            yield return new WaitForSeconds(1.2f);
            var pi = pc.GetComponentInChildren<TheDelivery.Interaction.PlayerInteraction>();
            var camT = Camera.main.transform;
            Physics.Raycast(camT.position, camT.forward, out var hh, 5f, ~0, QueryTriggerInteraction.Collide);
            Debug.Log($"[FlashShow] mira: alvo={(pi.Current as Component)?.name} raio bate em {hh.collider?.name} (layer {hh.collider?.gameObject.layer}) a {hh.distance:0.00} m; cam {camT.position} fwd {camT.forward} balde {b}");
            bool hadWire = Act5Inventory.Has("wire");
            yield return PressF();
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[FlashShow] F no balde -> arame no inventário: {(!hadWire && Act5Inventory.Has("wire"))}; lanterna {(fl.On ? "ACESA" : "APAGADA")}");
            yield return new WaitForSeconds(4f);
            rec.FinishEarly();
        }
    }
}

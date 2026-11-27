using System.Collections;
using System.IO;
using TheDelivery.Act5;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    [InitializeOnLoad]
    public static class ShipMotionDemo
    {
        static ShipMotionDemo() => GameScreenshotTool.PlayReady += prep =>
        {
            if (prep != "shipmotion") return;
            var rec = new GameObject("ShipMotionRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.RenderSize = new Vector2Int(1280, 720);
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => Object.FindAnyObjectByType<Act5Director>()?.CurrentBeat == Act5Beat.CellPuzzle);
            var player = Object.FindAnyObjectByType<PlayerController>();
            var chain = GameObject.Find("HangingChain_Engine").transform;
            var hull = Object.FindAnyObjectByType<AmbientOneShots>();
            var pivot = Camera.main.transform.parent;
            var water = Object.FindAnyObjectByType<WaterRipples>();
            var comfort = ShipMotion.CameraComfort;

            // Exercise the actual pause-menu control, then return to the player's original preference.
            var menu = PauseMenu.Instance;
            menu.Pause();
            var button = menu.transform.Find("PauseCanvas/Column/CameraMotionButton").GetComponent<Button>();
            for (int i = 0; i < 3; i++)
            {
                button.onClick.Invoke();
                yield return null;
                yield return new WaitForEndOfFrame();
                if (ShipMotion.CameraComfort == ShipMotion.Comfort.Off)
                    Debug.Assert(Quaternion.Angle(pivot.localRotation, Quaternion.identity) < 0.01f);
            }
            Debug.Assert(ShipMotion.CameraComfort == comfort);
            menu.Resume();

            void LookFrom(Vector3 feet, Vector3 target)
            {
                var cc = player.GetComponent<CharacterController>();
                cc.enabled = false;
                Vector3 dir = target - (feet + Vector3.up * player.EyeHeight);
                var flat = new Vector3(dir.x, 0f, dir.z);
                player.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(flat));
                cc.enabled = true;
                player.CanMove = false;
                float pitch = -Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg;
                player.SyncCameraState(pitch, player.EyeHeight);
                player.CameraHolder.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            LookFrom(new Vector3(3f, 0.06f, -5.2f), new Vector3(3f, 1.8f, -2.3f));
            var anchor = chain.position;
            Quaternion initialChain = chain.rotation;
            float chainTravel = 0f, cameraTravel = 0f, initialSlosh = water.Slosh, sloshTravel = 0f;
            rec.At(10.5f, () => LookFrom(new Vector3(7.5f, 0.06f, -14.4f), chain.position + Vector3.down * 1.2f));
            rec.At(14.5f, hull.PlayNow);
            rec.At(19f, () => LookFrom(new Vector3(14.1f, -0.85f, 15.3f), new Vector3(12f, -0.04f, 17f)));
            rec.Begin(27f, 24, true);
            while (!File.Exists(DemoFrameRecorder.DoneMarker))
            {
                chainTravel = Mathf.Max(chainTravel, Quaternion.Angle(initialChain, chain.rotation));
                sloshTravel = Mathf.Max(sloshTravel, Mathf.Abs(water.Slosh - initialSlosh));
                cameraTravel = Mathf.Max(cameraTravel, Quaternion.Angle(Quaternion.identity, pivot.localRotation));
                yield return null;
            }
            Debug.Assert(chain.position == anchor && chainTravel > 3f && sloshTravel > 0.8f);
            Debug.Assert(comfort == ShipMotion.Comfort.Off ? cameraTravel < 0.01f : cameraTravel > 0.1f);
            Debug.Log($"[ShipMotionDemo] Comfort={comfort}, camera={cameraTravel:F2} deg, chain={chainTravel:F2} deg, water={sloshTravel:F2}");
            var director = Object.FindAnyObjectByType<Act5Director>();
            director.AdvanceToBeat(Act5Beat.Explore);
            int groans = 0;
            hull.Played += () => groans++;
            for (int i = 1; i <= 3; i++)
            {
                var generator = GameObject.Find($"Generator_{i}").GetComponent<GeneratorStation>();
                generator.Interact(null);
                yield return new WaitUntil(() => generator.Running);
            }
            // BeatDirector applies the requested beat on the next frame.
            yield return new WaitForSeconds(0.2f);
            Debug.Assert(groans > 0 && director.CurrentBeat == Act5Beat.Hunt);
            Debug.Log($"[ShipMotionDemo] Third generator: {director.CurrentBeat}, hull sounds={groans}");
            EditorApplication.ExitPlaymode();
        }
    }
}

//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>
    /// Input compatibility layer for the playable demo. Reads the new Input System when the
    /// project's active input handler enables it (ENABLE_INPUT_SYSTEM), otherwise falls back to
    /// the legacy Input Manager — so the demo scene plays out of the box under either input
    /// configuration. All members are null-device-safe (on WebGL a device can be absent until
    /// the first interaction).
    /// </summary>
    public static class BCG_DemoInput {

#if ENABLE_INPUT_SYSTEM

        //  New-input mouse delta is in pixels per frame; legacy "Mouse X/Y" axes are pre-scaled.
        //  This factor makes the two branches feel the same at the default look sensitivity.
        const float kMouseDeltaScale = 0.1f;

        /// <summary>WASD as a raw (-1..1, -1..1) vector — x = strafe, y = forward.</summary>
        public static Vector2 MoveAxes {
            get {
                Keyboard k = Keyboard.current;
                if (k == null)
                    return Vector2.zero;
                float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        /// <summary>Vertical fly input: Space/E = up, LeftCtrl/Q = down (-1..1).</summary>
        public static float UpDown {
            get {
                Keyboard k = Keyboard.current;
                if (k == null)
                    return 0f;
                float up = (k.spaceKey.isPressed || k.eKey.isPressed) ? 1f : 0f;
                float down = (k.leftCtrlKey.isPressed || k.qKey.isPressed) ? 1f : 0f;
                return up - down;
            }
        }

        /// <summary>True while Left Shift is held (fast-fly multiplier).</summary>
        public static bool Fast => Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        /// <summary>Per-frame mouse look delta, scaled to match the legacy axes.</summary>
        public static Vector2 LookDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() * kMouseDeltaScale : Vector2.zero;

        /// <summary>True on the frame the left mouse button was pressed.</summary>
        public static bool LeftClickDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        /// <summary>True while the right mouse button is held (hold-to-look).</summary>
        public static bool RightHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>True on the frame Escape was pressed (unlock cursor / deselect).</summary>
        public static bool EscapeDown => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        /// <summary>True on the frame H or F1 was pressed (toggle the help overlay).</summary>
        public static bool HelpDown {
            get {
                Keyboard k = Keyboard.current;
                return k != null && (k.hKey.wasPressedThisFrame || k.f1Key.wasPressedThisFrame);
            }
        }

        /// <summary>True on the frame N was pressed (toggle day / night).</summary>
        public static bool NightDown => Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame;

        /// <summary>True on the frame any key or mouse button was pressed (skip the intro).</summary>
        public static bool AnyInputDown {
            get {
                Keyboard k = Keyboard.current;
                if (k != null && k.anyKey.wasPressedThisFrame)
                    return true;
                Mouse m = Mouse.current;
                return m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame);
            }
        }

        /// <summary>True on the frame C was pressed (replay the cinematic intro).</summary>
        public static bool ReplayDown => Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;

        /// <summary>Current pointer position in screen pixels.</summary>
        public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

#else

        /// <summary>WASD as a raw (-1..1, -1..1) vector — x = strafe, y = forward.</summary>
        public static Vector2 MoveAxes {
            get {
                float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        /// <summary>Vertical fly input: Space/E = up, LeftCtrl/Q = down (-1..1).</summary>
        public static float UpDown {
            get {
                float up = (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E)) ? 1f : 0f;
                float down = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.Q)) ? 1f : 0f;
                return up - down;
            }
        }

        /// <summary>True while Left Shift is held (fast-fly multiplier).</summary>
        public static bool Fast => Input.GetKey(KeyCode.LeftShift);

        /// <summary>Per-frame mouse look delta (legacy "Mouse X/Y" axes exist in every project).</summary>
        public static Vector2 LookDelta => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        /// <summary>True on the frame the left mouse button was pressed.</summary>
        public static bool LeftClickDown => Input.GetMouseButtonDown(0);

        /// <summary>True while the right mouse button is held (hold-to-look).</summary>
        public static bool RightHeld => Input.GetMouseButton(1);

        /// <summary>True on the frame Escape was pressed (unlock cursor / deselect).</summary>
        public static bool EscapeDown => Input.GetKeyDown(KeyCode.Escape);

        /// <summary>True on the frame H or F1 was pressed (toggle the help overlay).</summary>
        public static bool HelpDown => Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1);

        /// <summary>True on the frame N was pressed (toggle day / night).</summary>
        public static bool NightDown => Input.GetKeyDown(KeyCode.N);

        /// <summary>True on the frame any key or mouse button was pressed (skip the intro).</summary>
        public static bool AnyInputDown => Input.anyKeyDown;

        /// <summary>True on the frame C was pressed (replay the cinematic intro).</summary>
        public static bool ReplayDown => Input.GetKeyDown(KeyCode.C);

        /// <summary>Current pointer position in screen pixels.</summary>
        public static Vector2 PointerPosition => (Vector2)Input.mousePosition;

#endif

    }

}

//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>
    /// Free-fly demo camera with real collision. Movement goes through CharacterController.Move
    /// (collide-and-slide, gravity off), so the camera slides along building colliders and the
    /// ground instead of clipping through them. Click locks the cursor for mouse-look (Esc
    /// releases it); right-mouse-hold also looks while the cursor is free. WASD moves, Space/E
    /// and Ctrl/Q fly up / down, Shift is fast. Position is clamped to a world-bounds box so the
    /// demo can never be flown out of sight.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BCG_DemoFlyCamera : MonoBehaviour {

        [Tooltip("Base fly speed in m/s.")]
        [Min(0.1f)] public float moveSpeed = 12f;

        [Tooltip("Speed multiplier while Left Shift is held.")]
        [Min(1f)] public float fastMultiplier = 3.5f;

        [Tooltip("Mouse look sensitivity (degrees per scaled mouse-delta unit).")]
        [Min(0.1f)] public float lookSensitivity = 2f;

        [Tooltip("Maximum up / down pitch in degrees.")]
        [Range(10f, 89f)] public float pitchLimit = 89f;

        [Tooltip("Center of the box the camera is confined to (world space).")]
        public Vector3 boundsCenter = new Vector3(0f, 60f, 0f);

        [Tooltip("Half-size of the confinement box on each axis.")]
        public Vector3 boundsExtents = new Vector3(250f, 90f, 250f);

        CharacterController controller;
        float yaw;
        float pitch;

        void Awake() {

            controller = GetComponent<CharacterController>();
            SyncAnglesFromTransform();

        }

        /// <summary>Re-seeds yaw / pitch from the current transform rotation. Call after external
        /// code (the cinematic director) has moved the camera, so the first mouse-look continues
        /// from the handed-off rotation instead of snapping back.</summary>
        public void SyncAnglesFromTransform() {

            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;

        }

        void Update() {

            //  Cursor lock: click grabs the pointer for mouse-look, Esc releases it. The lock
            //  request happening on the click frame is what makes this work on WebGL (browsers
            //  only honor pointer-lock from inside a user gesture).
            if (BCG_DemoInput.LeftClickDown && Cursor.lockState != CursorLockMode.Locked) {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (BCG_DemoInput.EscapeDown) {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            //  Mouse look while the cursor is captured, or while RMB is held with a free cursor.
            if (Cursor.lockState == CursorLockMode.Locked || BCG_DemoInput.RightHeld) {

                Vector2 look = BCG_DemoInput.LookDelta * lookSensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            }

            //  Camera-relative fly movement through the controller (collide-and-slide).
            Vector2 move = BCG_DemoInput.MoveAxes;
            Vector3 direction = transform.forward * move.y + transform.right * move.x + Vector3.up * BCG_DemoInput.UpDown;

            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            float speed = moveSpeed * (BCG_DemoInput.Fast ? fastMultiplier : 1f);
            controller.Move(direction * speed * Time.deltaTime);

            //  Hard world clamp so the demo can't be flown out of the city. Re-syncs the
            //  controller by moving the transform directly (safe: controller is told nothing).
            Vector3 clamped = ClampToBounds(transform.position, boundsCenter, boundsExtents);

            if (clamped != transform.position)
                transform.position = clamped;

        }

        /// <summary>Clamps a point into the axis-aligned box (center ± extents). Pure, unit-tested.</summary>
        public static Vector3 ClampToBounds(Vector3 point, Vector3 center, Vector3 extents) {
            return new Vector3(
                Mathf.Clamp(point.x, center.x - extents.x, center.x + extents.x),
                Mathf.Clamp(point.y, center.y - extents.y, center.y + extents.y),
                Mathf.Clamp(point.z, center.z - extents.z, center.z + extents.z));
        }

    }

}

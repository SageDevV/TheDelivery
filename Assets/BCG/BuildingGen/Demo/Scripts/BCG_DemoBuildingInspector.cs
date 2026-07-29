//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>
    /// Click-to-inspect for generated buildings. Raycasts from the camera on left click (screen
    /// center while the cursor is locked, pointer position otherwise), resolves the hit collider
    /// to its BCG_BuildingMarker, tints the building via a MaterialPropertyBlock (no material
    /// instantiation — batching and shared materials untouched), and raises SelectionChanged with
    /// the captured info (null = deselected). Esc or clicking empty space deselects.
    /// </summary>
    public class BCG_DemoBuildingInspector : MonoBehaviour {

        [Tooltip("Camera the pick ray comes from. Defaults to Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Maximum pick distance in meters.")]
        [Min(1f)] public float maxDistance = 600f;

        [Tooltip("Albedo tint applied to the selected building.")]
        public Color highlightTint = new Color(1f, 0.62f, 0.3f);

        /// <summary>Raised on every selection change; null payload = deselected.</summary>
        public event Action<BCG_DemoBuildingInfo> SelectionChanged;

        BCG_BuildingMarker current;
        Renderer[] currentRenderers;
        MaterialPropertyBlock block;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake() {

            block = new MaterialPropertyBlock();

            if (targetCamera == null)
                targetCamera = Camera.main;

        }

        void OnDisable() {
            Select(null);
        }

        void Update() {

            if (BCG_DemoInput.EscapeDown) {
                Select(null);
                return;
            }

            if (!BCG_DemoInput.LeftClickDown || targetCamera == null)
                return;

            Vector2 point = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : BCG_DemoInput.PointerPosition;

            Ray ray = targetCamera.ScreenPointToRay(point);
            BCG_BuildingMarker hitMarker = null;

            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
                hitMarker = hit.collider.GetComponentInParent<BCG_BuildingMarker>();

            Select(hitMarker);

        }

        void Select(BCG_BuildingMarker marker) {

            if (marker == current)
                return;

            //  Clear the previous highlight (defensively — renderers may have been destroyed).
            if (currentRenderers != null) {

                foreach (Renderer r in currentRenderers) {
                    if (r != null)
                        r.SetPropertyBlock(null);
                }

                currentRenderers = null;

            }

            current = marker;

            if (current != null) {

                currentRenderers = current.GetComponentsInChildren<Renderer>();
                block.Clear();
                block.SetColor(ColorId, highlightTint);
                block.SetColor(BaseColorId, highlightTint);

                foreach (Renderer r in currentRenderers)
                    r.SetPropertyBlock(block);

            }

            SelectionChanged?.Invoke(current != null ? BCG_DemoBuildingInfo.Capture(current) : null);

        }

    }

}

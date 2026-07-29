//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Scene-view hand-placement brush ("Paint in Scene" on the Single tab). While active it raycasts
    /// the cursor to ground (skipping generated buildings and zone markers), draws a guard-resolved
    /// footprint ghost — blue when the spot is free, amber when the guard would relocate or the
    /// obstacle mask blocks it — and stamps a building per left-click through the generator window's
    /// StampBuildingAt (so Save As Prefab / lightmap / LOD / props / ground-snap options all flow
    /// automatically). Shift-click repeats the exact same building (no reseed). Esc, toggling off,
    /// switching tabs, closing the window, and entering Play Mode all deactivate. Brush-active is
    /// deliberately session-transient (no EditorPrefs — a domain reload drops the SceneView hook, and
    /// the window toggle reads <see cref="Active"/> so it self-consistently shows off).
    /// </summary>
    public static class BCG_BuildingBrush {

        static BCG_BuildingGeneratorWindow window;                  //  Owner; null == inactive.
        static List<BCG_PlacementGuard.Footprint> occupied;         //  Collected on Activate, appended per stamp.
        static bool occupiedDirty;                                  //  Lazy re-collect (hierarchyChanged / undoRedo).
        static int claimedControl;                                  //  hotControl claimed on MouseDown (0 = none).

        static Vector3 hoverPoint;                                  //  Raw ground hit under the cursor.
        static Vector3 hoverResolved;                               //  Guard-resolved landing spot.
        static bool hoverValid;
        static bool hoverBlocked;                                   //  Relocated or obstacle-blocked.
        static bool hoverPlaceable;                                 //  False = fully blocked (no stamp).

        static readonly int kBrushHash = "BCG_BuildingBrush".GetHashCode();

        static readonly Color kGhostFreeFill = new Color(0.55f, 0.76f, 1f, 0.12f);
        static readonly Color kGhostFreeLine = new Color(0.55f, 0.76f, 1f, 0.9f);
        static readonly Color kGhostBlockedFill = new Color(1f, 0.78f, 0.45f, 0.12f);
        static readonly Color kGhostBlockedLine = new Color(1f, 0.78f, 0.45f, 0.9f);

        /// <summary>True while the brush is live in the Scene view.</summary>
        public static bool Active { get { return window != null; } }

        /// <summary>Arms the brush for the given generator window: snapshots existing building
        /// footprints once, hooks SceneView.duringSceneGui, and subscribes the teardown/staleness
        /// events. Re-activating under a new owner deactivates first. Focuses the last Scene view so
        /// Esc works immediately.</summary>
        public static void Activate(BCG_BuildingGeneratorWindow owner) {

            if (Active)
                Deactivate();

            if (owner == null)
                return;

            window = owner;
            occupied = BCG_PlacementGuard.CollectExisting();
            occupiedDirty = false;
            hoverValid = false;

            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.hierarchyChanged += MarkOccupiedDirty;
            Undo.undoRedoPerformed += MarkOccupiedDirty;

            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Focus();

            SceneView.RepaintAll();

        }

        /// <summary>Disarms the brush: unsubscribes every hook (safe when already inactive), clears
        /// state, repaints the Scene views (ghost removal) and the owner window (toggle state).</summary>
        public static void Deactivate() {

            //  Release a mid-drag hotControl claim: Esc between MouseDown and MouseUp would
            //  otherwise leave a dead control hot after this handler unsubscribes — Scene-view
            //  clicks / selection stay wedged until something else resets it.
            if (claimedControl != 0 && GUIUtility.hotControl == claimedControl)
                GUIUtility.hotControl = 0;

            claimedControl = 0;

            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.hierarchyChanged -= MarkOccupiedDirty;
            Undo.undoRedoPerformed -= MarkOccupiedDirty;

            BCG_BuildingGeneratorWindow owner = window;

            window = null;
            occupied = null;
            hoverValid = false;

            SceneView.RepaintAll();

            if (owner != null)
                owner.Repaint();

        }

        /// <summary>Pure y = 0 ground-plane fallback for the cursor ray. False when the ray is
        /// parallel to the plane or points away from it.</summary>
        public static bool RayToGroundPlane(Ray ray, out Vector3 point) {

            point = Vector3.zero;

            if (Mathf.Abs(ray.direction.y) < 1e-6f)
                return false;

            float t = -ray.origin.y / ray.direction.y;

            if (t < 0f)
                return false;

            point = ray.origin + ray.direction * t;
            return true;

        }

        static void OnSceneGUI(SceneView sceneView) {

            if (window == null) {

                //  Owner died without teardown (shouldn't happen — belt and braces).
                Deactivate();
                return;

            }

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(kBrushHash, FocusType.Passive);

            switch (e.type) {

                case EventType.Layout:
                    //  Claim the default control so clicks never fall through to selection/marquee.
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseMove:
                case EventType.MouseDrag:
                    UpdateHover(e);
                    sceneView.Repaint();
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt) {

                        GUIUtility.hotControl = controlId;
                        claimedControl = controlId;
                        UpdateHover(e);     //  Stamp at the true cursor position, not the last hover.
                        Stamp(reseed: !e.shift);
                        e.Use();

                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId) {

                        GUIUtility.hotControl = 0;
                        claimedControl = 0;
                        e.Use();

                    }
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape) {

                        Deactivate();
                        e.Use();

                    }
                    break;

                case EventType.Repaint:
                    DrawGhost();
                    DrawOverlayHint();
                    break;

            }

        }

        static void UpdateHover(Event e) {

            if (occupiedDirty || occupied == null) {

                occupied = BCG_PlacementGuard.CollectExisting();
                occupiedDirty = false;

            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            hoverValid = TryGetGroundPoint(ray, out hoverPoint);

            if (!hoverValid)
                return;

            BCG_BuildingMeshBuilder.TowerParams p = window.CurrentParams;
            BCG_PlacementGuard.ObstacleQuery obstacles =
                BCG_PlacementGuard.MakeObstacleQuery(BCG_BuildingGeneratorWindow.ActiveObstacleLayers, p.PlacementHeight, null);

            bool wouldRelocate;
            hoverPlaceable = BCG_PlacementGuard.PreviewPosition(occupied, hoverPoint, p.Width, p.Depth, 0f, obstacles, out hoverResolved, out wouldRelocate);
            hoverBlocked = !hoverPlaceable || wouldRelocate;

        }

        /// <summary>Nearest physics hit under the cursor that is neither a generated building nor a
        /// zone marker; falls back to the pure y = 0 plane when nothing is hit.</summary>
        static bool TryGetGroundPoint(Ray ray, out Vector3 point) {

            point = Vector3.zero;

            RaycastHit[] hits = Physics.RaycastAll(ray, 100000f, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float bestDistance = float.MaxValue;

            foreach (RaycastHit hit in hits) {

                if (BCG_PlacementGuard.IsProtectedCollider(hit.collider, null))
                    continue;

                if (hit.distance < bestDistance) {

                    bestDistance = hit.distance;
                    point = hit.point;
                    found = true;

                }

            }

            if (found)
                return true;

            return RayToGroundPlane(ray, out point);

        }

        static void Stamp(bool reseed) {

            if (!hoverValid || window == null)
                return;

            //  StampBuildingAt owns the obstacle/guard resolve (appending to our occupied list), the
            //  ground snap + skirt, the Undo registration, and every output toggle via SpawnBuilding.
            window.StampBuildingAt(hoverPoint, reseed, occupied);

        }

        static void DrawGhost() {

            if (!hoverValid || window == null)
                return;

            BCG_BuildingMeshBuilder.TowerParams p = window.CurrentParams;

            float hw = p.Width * .5f;
            float hd = p.Depth * .5f;

            Vector3 c = hoverResolved;
            Vector3[] rect = {
                c + new Vector3(-hw, 0f, -hd),
                c + new Vector3(hw, 0f, -hd),
                c + new Vector3(hw, 0f, hd),
                c + new Vector3(-hw, 0f, hd)
            };

            Color fill = hoverBlocked ? kGhostBlockedFill : kGhostFreeFill;
            Color line = hoverBlocked ? kGhostBlockedLine : kGhostFreeLine;

            Handles.DrawSolidRectangleWithOutline(rect, fill, line);

            Color prev = Handles.color;
            Handles.color = line;
            Handles.DrawWireCube(c + Vector3.up * p.TotalHeight * .5f, new Vector3(p.Width, p.TotalHeight, p.Depth));

            //  When the guard moved the ghost, show where the cursor actually pointed.
            if (hoverBlocked && (hoverResolved - hoverPoint).sqrMagnitude > 0.01f)
                Handles.DrawDottedLine(hoverPoint, hoverResolved, 4f);

            Handles.color = prev;

        }

        static void DrawOverlayHint() {

            Handles.BeginGUI();
            GUI.Label(new Rect(10f, 10f, 400f, 22f),
                "Building Brush:  Click = stamp   ·   Shift+Click = repeat last building   ·   Esc = exit",
                EditorStyles.helpBox);
            Handles.EndGUI();

        }

        static void OnPlayModeChanged(PlayModeStateChange change) {

            Deactivate();

        }

        static void MarkOccupiedDirty() {

            occupiedDirty = true;

        }

    }

}

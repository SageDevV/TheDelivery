//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Polyline street marker for the Building Generator. Lay points along a road (in this object's
    /// local space) and fill from the generator window's Street tab ("Along Path" layout): buildings
    /// line the path from the first point to the last, facing the carriageway. Data-only: the
    /// arc-length walk and packing live in the editor-side populator, so this script carries no
    /// generation logic and is safe in a build.
    /// </summary>
    [DisallowMultipleComponent]
    public class BCG_StreetPath : MonoBehaviour {

        [Tooltip("Path waypoints in the object's local space, in order (minimum 2). Buildings line the path from the first point to the last. Drag the handles in the Scene view or edit the list in the inspector. Points normally sit on the ground — buildings inherit the sampled height.")]
        public List<Vector3> points = new List<Vector3> { Vector3.zero, new Vector3(40f, 0f, 0f) };

        [Range(6f, 40f)]
        [Tooltip("Width of the clear carriageway along the path centre (m). Each building row is set back from the centre line by half this distance.")]
        public float roadWidth = 16f;

        [Tooltip("Line both sides of the path, each row facing the street. Off = only the left-hand side (looking from the first point toward the last).")]
        public bool bothSides = true;

        //  Scene reference to the parent GameObject of this path's last output, so a repopulate can
        //  replace it and the gizmo can show whether the path has been filled. Written by the tool.
        [HideInInspector]
        public GameObject lastPopulated;

#if UNITY_EDITOR

        //  Cyan while the path is empty, green once it has been populated (BCG_BuildingZone colours).
        static readonly Color gizmoEmpty = new Color(0.2f, 0.9f, 1f);
        static readonly Color gizmoFilled = new Color(0.3f, 1f, 0.4f);

        void OnDrawGizmos() {

            DrawPathGizmo(false);

        }

        void OnDrawGizmosSelected() {

            DrawPathGizmo(true);

        }

        /// <summary>Centre polyline + a per-segment road ribbon (presentation only — no miters; the
        /// populator does its own exact arc-length math).</summary>
        void DrawPathGizmo(bool selected) {

            if (points == null || points.Count < 2)
                return;

            Color baseColor = lastPopulated == null ? gizmoEmpty : gizmoFilled;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = selected ? baseColor : new Color(baseColor.r, baseColor.g, baseColor.b, 0.55f);

            for (int i = 0; i < points.Count - 1; i++) {

                Vector3 a = points[i];
                Vector3 b = points[i + 1];

                Gizmos.DrawLine(a, b);

                Vector3 dir = b - a;

                if (dir.sqrMagnitude < 0.0001f)
                    continue;

                Vector3 side = Vector3.Cross(dir.normalized, Vector3.up) * (roadWidth * .5f);

                Gizmos.DrawLine(a + side, b + side);
                Gizmos.DrawLine(a - side, b - side);

            }

            if (selected)
                foreach (Vector3 p in points)
                    Gizmos.DrawSphere(p, 0.6f);

        }

#endif

    }

}

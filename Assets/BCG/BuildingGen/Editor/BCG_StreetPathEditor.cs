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
    /// Inspector for <see cref="BCG_StreetPath"/>: themed header, validation, road fields, a
    /// per-point list with insert/delete, Add Point, a status line, and Scene-view position handles
    /// (Undo-recorded). Populating happens from the generator window's Street tab ("Along Path"
    /// layout). Single-object (Scene handles and multi-edit don't mix).
    /// </summary>
    [CustomEditor(typeof(BCG_StreetPath))]
    public class BCG_StreetPathEditor : Editor {

        const float kNewSegmentLength = 30f;

        static readonly Color kAccent = new Color(0.45f, 0.70f, 0.95f);

        static GUIStyle sTitleStyle;
        static GUIStyle sSubStyle;

        public override void OnInspectorGUI() {

            //  The asset skin applies per-window; inspectors draw inside Unity's own chrome, so only
            //  the themed band below is custom (matches BCG_BuildingZoneEditor).
            BCG_StreetPath path = (BCG_StreetPath)target;

            DrawHeaderBand();
            DrawValidation(path);

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadWidth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bothSides"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(2f);
            DrawPointsList(path);

            if (GUILayout.Button(new GUIContent("Add Point", "Appends a new waypoint beyond the last segment's direction."), GUILayout.Height(20f))) {

                Undo.RecordObject(path, "Add Street Path Point");
                path.points.Add(NextAppendPoint(path.points));
                EditorUtility.SetDirty(path);

            }

            EditorGUILayout.Space(4f);
            DrawStatus(path);

            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox("Populate from the generator window's Build ▸ Street pane (layout: Along Path).", MessageType.None);

            if (GUILayout.Button("Open Generator Window", EditorStyles.miniButton))
                BCG_BuildingGeneratorWindow.Open();

        }

        /// <summary>Themed brand band (follows Light/Dark via isProSkin) with title + subtitle.</summary>
        static void DrawHeaderBand() {

            if (sTitleStyle == null) {

                sTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                sSubStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false };

            }

            Rect r = EditorGUILayout.GetControlRect(false, 36f);
            Color bg = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.20f, 0.24f) : new Color(0.76f, 0.79f, 0.85f);
            EditorGUI.DrawRect(r, bg);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 6f, 3f, 24f), kAccent);
            GUI.Label(new Rect(r.x + 12f, r.y + 4f, r.width - 16f, 18f), "STREET PATH", sTitleStyle);
            GUI.Label(new Rect(r.x + 12f, r.y + 20f, r.width - 16f, 14f),
                "Polyline street marker — buildings follow it from Build ▸ Street.", sSubStyle);
            EditorGUILayout.Space(2f);

        }

        static void DrawValidation(BCG_StreetPath path) {

            if (path.points == null || path.points.Count < 2) {

                EditorGUILayout.HelpBox("A street path needs at least 2 points.", MessageType.Warning);
                return;

            }

            float length = BCG_StreetPathPopulator.PathLength(BCG_StreetPathPopulator.WorldPoints(path));

            if (length < BCG_StreetPathPopulator.kMinPathLength)
                EditorGUILayout.HelpBox("Path too short to hold a single plot (< " + BCG_StreetPathPopulator.kMinPathLength.ToString("0.#") + " m).", MessageType.Warning);

        }

        /// <summary>Direct-mutation point rows (the standard Handles-compatible path): Vector3 field,
        /// '+' inserts after, '−' deletes (kept at a 2-point minimum).</summary>
        void DrawPointsList(BCG_StreetPath path) {

            if (path.points == null)
                return;

            for (int i = 0; i < path.points.Count; i++) {

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("P" + i, GUILayout.Width(26f));

                EditorGUI.BeginChangeCheck();
                Vector3 moved = EditorGUILayout.Vector3Field(GUIContent.none, path.points[i]);

                if (EditorGUI.EndChangeCheck()) {

                    Undo.RecordObject(path, "Move Street Path Point");
                    path.points[i] = moved;
                    EditorUtility.SetDirty(path);

                }

                if (GUILayout.Button(new GUIContent("+", "Insert a point after this one."), EditorStyles.miniButtonLeft, GUILayout.Width(22f))) {

                    Undo.RecordObject(path, "Insert Street Path Point");
                    Vector3 inserted = i < path.points.Count - 1
                        ? Vector3.Lerp(path.points[i], path.points[i + 1], 0.5f)
                        : NextAppendPoint(path.points);
                    path.points.Insert(i + 1, inserted);
                    EditorUtility.SetDirty(path);

                }

                using (new EditorGUI.DisabledScope(path.points.Count <= 2)) {

                    if (GUILayout.Button(new GUIContent("−", "Delete this point."), EditorStyles.miniButtonRight, GUILayout.Width(22f))) {

                        Undo.RecordObject(path, "Delete Street Path Point");
                        path.points.RemoveAt(i);
                        EditorUtility.SetDirty(path);
                        EditorGUILayout.EndHorizontal();
                        return;     //  List changed mid-draw — bail this pass.

                    }

                }

                EditorGUILayout.EndHorizontal();

            }

        }

        /// <summary>Where "Add Point" lands: beyond the last segment's direction, or +X when the
        /// tail is degenerate. PURE — testable.</summary>
        public static Vector3 NextAppendPoint(List<Vector3> pts) {

            if (pts == null || pts.Count == 0)
                return Vector3.zero;

            Vector3 last = pts[pts.Count - 1];

            if (pts.Count < 2)
                return last + Vector3.right * kNewSegmentLength;

            Vector3 dir = last - pts[pts.Count - 2];

            if (dir.sqrMagnitude < 0.0001f)
                return last + Vector3.right * kNewSegmentLength;

            return last + dir.normalized * kNewSegmentLength;

        }

        void DrawStatus(BCG_StreetPath path) {

            float length = BCG_StreetPathPopulator.PathLength(BCG_StreetPathPopulator.WorldPoints(path));
            string line = "Length ≈ " + length.ToString("0.#") + " m";

            if (path.lastPopulated != null)
                line += "   ·   Output: " + path.lastPopulated.name + " (" + path.lastPopulated.transform.childCount + " buildings)";
            else
                line += "   ·   Not populated yet.";

            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);

        }

        void OnSceneGUI() {

            BCG_StreetPath path = (BCG_StreetPath)target;

            if (path.points == null || path.points.Count == 0)
                return;

            Vector3[] world = new Vector3[path.points.Count];

            for (int i = 0; i < path.points.Count; i++) {

                world[i] = path.transform.TransformPoint(path.points[i]);

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(world[i], Quaternion.identity);

                if (EditorGUI.EndChangeCheck()) {

                    Undo.RecordObject(path, "Move Street Path Point");
                    path.points[i] = path.transform.InverseTransformPoint(moved);
                    EditorUtility.SetDirty(path);
                    world[i] = moved;

                }

                Handles.Label(world[i] + Vector3.up, "P" + i);

            }

            Color prev = Handles.color;
            Handles.color = kAccent;
            Handles.DrawAAPolyLine(3f, world);
            Handles.color = prev;

        }

    }

}

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

    /// <summary>One facade material's day / night variant pair, wired in the scene.</summary>
    [Serializable]
    public class BCG_DemoMaterialPair {

        public Material day;
        public Material night;

    }

    /// <summary>
    /// Demo day / night toggle (N key). Swaps every generated building's shared facade materials
    /// between the day and night variants (direct asset references — no name parsing, no material
    /// instantiation), dims / tints the sun, and drops the ambient intensity. State is not
    /// persisted — the scene always starts as authored (day).
    /// </summary>
    public class BCG_DemoDayNight : MonoBehaviour {

        [Tooltip("The scene's directional light.")]
        public Light sun;

        [Tooltip("Facade day ↔ night material pairs (one per palette A–D).")]
        public BCG_DemoMaterialPair[] facadePairs;

        [Tooltip("Road day ↔ night material pair (the shared base road material ↔ its emissive-markings night variant). Optional — left empty when the scene has no generated roads.")]
        public BCG_DemoMaterialPair[] roadPairs;

        [Tooltip("Sun intensity by state.")]
        public float dayIntensity = 1f;
        public float nightIntensity = 0.12f;

        [Tooltip("Sun color by state.")]
        public Color dayColor = new Color(1f, 0.956f, 0.839f);
        public Color nightColor = new Color(0.55f, 0.62f, 0.85f);

        [Tooltip("RenderSettings.ambientIntensity by state.")]
        public float dayAmbient = 1f;
        public float nightAmbient = 0.15f;

        [Tooltip("Camera background color while it is night (the skybox stays daytime-bright, so night swaps the camera to a dark solid clear).")]
        public Color nightSkyColor = new Color(0.016f, 0.025f, 0.06f);

        /// <summary>Current state (scene always starts as day).</summary>
        public bool IsNight { get; private set; }

        Renderer[] buildingRenderers;
        Renderer[] roadRenderers;
        CameraClearFlags dayClearFlags;
        Color dayBackground;
        bool cameraStateCaptured;

        void Update() {

            if (BCG_DemoInput.NightDown)
                Toggle();

        }

        /// <summary>Flips between day and night.</summary>
        public void Toggle() {
            SetNight(!IsNight);
        }

        /// <summary>Applies the given state to all generated buildings, the sun, and the ambient.</summary>
        public void SetNight(bool night) {

            IsNight = night;

            if (facadePairs == null || facadePairs.Length == 0) {
                Debug.LogWarning("[BCG BuildingGen Demo] Day/Night: no facade material pairs wired — lights only.", this);
            } else {

                //  Re-collect when empty, not just when null: the building scan is heavier than
                //  the road scan (every BCG_BuildingMarker's full child renderer tree, not one
                //  renderer per road object), but an empty cache is still always wrong in a
                //  populated scene — a wasted re-scan is cheap next to a permanently broken night
                //  toggle across every building. (Observed live: a collect run right after a heavy
                //  editor operation raced against FindObjectsByType transiently returning zero
                //  markers, wedging the day/night swap for all buildings.)
                if (buildingRenderers == null || buildingRenderers.Length == 0)
                    CollectBuildingRenderers();

                foreach (Renderer r in buildingRenderers) {

                    if (r == null)
                        continue;

                    Material[] materials = r.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < materials.Length; i++) {

                        Material mapped = Map(materials[i], facadePairs, night);

                        if (!ReferenceEquals(mapped, materials[i])) {
                            materials[i] = mapped;
                            changed = true;
                        }

                    }

                    if (changed)
                        r.sharedMaterials = materials;

                }

            }

            //  Roads are optional and live under BCG_RoadMarker, not BCG_BuildingMarker — a
            //  separate cache + pair table, same Map() swap mechanism.
            if (roadPairs != null && roadPairs.Length > 0) {

                //  Re-collect when empty, not just when null: roads are far fewer objects than
                //  buildings (no meaningful cost to re-scan), and a stale empty cache would
                //  otherwise wedge the swap permanently if the very first collect ever raced
                //  against the scene still settling (observed once during authoring).
                if (roadRenderers == null || roadRenderers.Length == 0)
                    CollectRoadRenderers();

                foreach (Renderer r in roadRenderers) {

                    if (r == null)
                        continue;

                    Material[] materials = r.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < materials.Length; i++) {

                        Material mapped = Map(materials[i], roadPairs, night);

                        if (!ReferenceEquals(mapped, materials[i])) {
                            materials[i] = mapped;
                            changed = true;
                        }

                    }

                    if (changed)
                        r.sharedMaterials = materials;

                }

            }

            if (sun != null) {
                sun.intensity = night ? nightIntensity : dayIntensity;
                sun.color = night ? nightColor : dayColor;
            }

            RenderSettings.ambientIntensity = night ? nightAmbient : dayAmbient;

            //  The scene's skybox would keep the sky daytime-bright, so night clears to a dark
            //  solid color instead; day restores whatever the camera was authored with.
            Camera cam = Camera.main;

            if (cam != null) {

                if (!cameraStateCaptured) {
                    dayClearFlags = cam.clearFlags;
                    dayBackground = cam.backgroundColor;
                    cameraStateCaptured = true;
                }

                cam.clearFlags = night ? CameraClearFlags.SolidColor : dayClearFlags;
                cam.backgroundColor = night ? nightSkyColor : dayBackground;

            }

        }

        /// <summary>Drops the cached renderer lists so the next <see cref="SetNight"/> re-scans the
        /// scene. Call after buildings or roads are created / destroyed at runtime (the cinematic
        /// timelapse does) — the cache otherwise only refills when it is empty.</summary>
        public void InvalidateRendererCache() {

            buildingRenderers = null;
            roadRenderers = null;

        }

        void CollectBuildingRenderers() {

            //  Unity 6.4 (6000.4) deprecated the FindObjectsSortMode overload; the sort-free form does not
            //  exist on the 6000.3 baseline. Editor code shares BCG_EditorCompat, but this Demo runtime
            //  assembly can't reference it, so guard the one call site here.
#if UNITY_6000_4_OR_NEWER
            BCG_BuildingMarker[] markers = FindObjectsByType<BCG_BuildingMarker>(FindObjectsInactive.Exclude);
#else
            BCG_BuildingMarker[] markers = FindObjectsByType<BCG_BuildingMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            int total = 0;

            Renderer[][] perMarker = new Renderer[markers.Length][];

            for (int i = 0; i < markers.Length; i++) {
                perMarker[i] = markers[i].GetComponentsInChildren<Renderer>();
                total += perMarker[i].Length;
            }

            buildingRenderers = new Renderer[total];
            int at = 0;

            for (int i = 0; i < perMarker.Length; i++) {
                Array.Copy(perMarker[i], 0, buildingRenderers, at, perMarker[i].Length);
                at += perMarker[i].Length;
            }

        }

        /// <summary>Same shape as <see cref="CollectBuildingRenderers"/>, over BCG_RoadMarker
        /// (surface + markings) instead of BCG_BuildingMarker. Both renderer kinds share ONE
        /// material asset day-side (BCG_RoadBuilder.BuildRoadObjects), so one pair swaps both.</summary>
        void CollectRoadRenderers() {

#if UNITY_6000_4_OR_NEWER
            BCG_RoadMarker[] markers = FindObjectsByType<BCG_RoadMarker>(FindObjectsInactive.Exclude);
#else
            BCG_RoadMarker[] markers = FindObjectsByType<BCG_RoadMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            roadRenderers = new Renderer[markers.Length];

            for (int i = 0; i < markers.Length; i++)
                roadRenderers[i] = markers[i].GetComponent<Renderer>();

        }

        /// <summary>
        /// Maps one material through the pair table for the requested state. Unknown / null
        /// materials and incomplete pairs pass through unchanged. Pure, unit-tested.
        /// </summary>
        public static Material Map(Material current, BCG_DemoMaterialPair[] pairs, bool toNight) {

            if (current == null || pairs == null)
                return current;

            foreach (BCG_DemoMaterialPair pair in pairs) {

                if (pair == null)
                    continue;

                if (toNight && current == pair.day && pair.night != null)
                    return pair.night;

                if (!toNight && current == pair.night && pair.day != null)
                    return pair.day;

            }

            return current;

        }

    }

}

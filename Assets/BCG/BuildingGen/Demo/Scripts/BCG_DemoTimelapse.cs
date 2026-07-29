//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>One planned timelapse building: parameters, plot-local placement, palette pick.</summary>
    [Serializable]
    public class BCG_TimelapseEntry {

        public BCG_BuildingParams buildingParams;
        public Vector3 localPosition;
        public float rotationY;
        public int materialIndex;

    }

    /// <summary>
    /// The cinematic's runtime-generation beat: spawns a deterministic set of buildings one by one
    /// on the reserved plot via <see cref="BCG_RuntimeBuildingFactory"/>, each with a short eased
    /// Y-scale grow-in. Same seed = same little block every run. Spawned buildings persist after
    /// the intro (they are ordinary marker-tagged buildings, realtime-lit). Skip-safe:
    /// <see cref="CompleteInstantly"/> finishes everything from any state, including never begun.
    /// </summary>
    public class BCG_DemoTimelapse : MonoBehaviour {

        [Tooltip("Layout seed — same seed, same buildings, every run.")]
        public int seed = 4711;

        [Tooltip("Plot size in meters (X × Z), centered on this transform.")]
        public Vector2 plotSize = new Vector2(44f, 30f);

        [Tooltip("How many buildings the timelapse spawns.")]
        [Range(1, 16)] public int buildingCount = 8;

        [Tooltip("Facade materials indexed by palette (A–D day materials). A building's materialIndex wraps into this array.")]
        public Material[] facadeMaterials;

        [Tooltip("Seconds between building spawns.")]
        [Min(0.1f)] public float cadence = 0.9f;

        [Tooltip("Time multiplier applied to cadence and grow-in — the cinematic director sets this to its own playback speed so the timelapse keeps pace with its shot.")]
        [Min(0.1f)] public float speedMultiplier = 1f;

        [Tooltip("Seconds a building takes to grow to full height.")]
        [Min(0.05f)] public float growDuration = 0.35f;

        [Tooltip("Day/night controller to notify (its renderer cache must include the new buildings). Optional.")]
        public BCG_DemoDayNight dayNight;

        /// <summary>True once every planned building is spawned at full scale.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>How many buildings have been spawned so far.</summary>
        public int SpawnedCount { get { return spawned.Count; } }

        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<float> growTime = new List<float>();
        BCG_TimelapseEntry[] entries;
        bool running;
        float clock;
        bool warnedNoMaterials;

        /// <summary>Starts the cadence spawn. Idempotent while running or complete.</summary>
        public void Begin() {

            if (running || IsComplete)
                return;

            EnsureLayout();
            running = true;
            clock = cadence;    //  first building appears immediately

        }

        void Update() {

            if (!running)
                return;

            float dt = Time.deltaTime * Mathf.Max(0.1f, speedMultiplier);
            clock += dt;

            if (spawned.Count < entries.Length && clock >= cadence) {
                clock = 0f;
                Spawn(entries[spawned.Count]);
            }

            //  Advance grow-ins (eased Y scale). Finished entries settle at exactly 1.
            bool anyGrowing = false;

            for (int i = 0; i < spawned.Count; i++) {

                if (growTime[i] >= growDuration || spawned[i] == null)
                    continue;

                growTime[i] = Mathf.Min(growTime[i] + dt, growDuration);
                float t = growTime[i] / growDuration;
                float eased = 1f - (1f - t) * (1f - t);    //  ease-out quad
                spawned[i].transform.localScale = new Vector3(1f, Mathf.Max(0.05f, eased), 1f);

                if (growTime[i] < growDuration)
                    anyGrowing = true;

            }

            if (spawned.Count >= entries.Length && !anyGrowing)
                Finish();

        }

        /// <summary>Spawns everything not yet spawned and settles all grow-ins — the skip path.
        /// Safe from any state, including before <see cref="Begin"/> was ever called.</summary>
        public void CompleteInstantly() {

            if (IsComplete)
                return;

            EnsureLayout();

            while (spawned.Count < entries.Length)
                Spawn(entries[spawned.Count]);

            for (int i = 0; i < spawned.Count; i++) {
                growTime[i] = growDuration;
                if (spawned[i] != null)
                    spawned[i].transform.localScale = Vector3.one;
            }

            Finish();

        }

        /// <summary>Destroys every spawned building and resets state (the replay path).</summary>
        public void DespawnAll() {

            foreach (GameObject go in spawned) {

                if (go == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(go);
                else
                    DestroyImmediate(go);

            }

            spawned.Clear();
            growTime.Clear();
            running = false;
            IsComplete = false;
            clock = 0f;

            if (dayNight != null)
                dayNight.InvalidateRendererCache();

        }

        void Finish() {

            running = false;
            IsComplete = true;

            if (dayNight != null)
                dayNight.InvalidateRendererCache();

        }

        void EnsureLayout() {

            if (entries == null || entries.Length != buildingCount)
                entries = BuildLayout(seed, plotSize, buildingCount);

        }

        void Spawn(BCG_TimelapseEntry entry) {

            Material material = null;

            if (facadeMaterials != null && facadeMaterials.Length > 0)
                material = facadeMaterials[entry.materialIndex % facadeMaterials.Length];

            if (material == null && !warnedNoMaterials) {
                warnedNoMaterials = true;
                Debug.LogWarning("[BCG BuildingGen Demo] Timelapse: no facade materials wired — buildings will render magenta.", this);
            }

            GameObject go = BCG_RuntimeBuildingFactory.Build(entry.buildingParams, material, true);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = entry.localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, entry.rotationY, 0f);
            go.transform.localScale = new Vector3(1f, 0.05f, 1f);

            spawned.Add(go);
            growTime.Add(0f);

        }

        /// <summary>
        /// Plans the timelapse block: a 2-row grid across the plot, mixed archetypes, palette and
        /// per-building seed from one seeded stream. Pure and deterministic — same inputs, same
        /// plan, on every platform. Footprints always fit their grid cell (and so the plot).
        /// </summary>
        public static BCG_TimelapseEntry[] BuildLayout(int seed, Vector2 plotSize, int count) {

            System.Random rng = new System.Random(seed);
            int cols = Mathf.Max(1, (count + 1) / 2);
            int rows = count > 1 ? 2 : 1;
            float cellW = plotSize.x / cols;
            float cellD = plotSize.y / rows;

            BCG_TimelapseEntry[] entries = new BCG_TimelapseEntry[count];

            for (int i = 0; i < count; i++) {

                int col = i % cols;
                int row = i / cols;

                //  Archetype mix: mostly Apartments/Shops with a couple of Towers for skyline.
                int roll = rng.Next(0, 100);
                BCG_BuildingArchetype archetype =
                    roll < 30 ? BCG_BuildingArchetype.Tower :
                    roll < 70 ? BCG_BuildingArchetype.Apartment :
                                BCG_BuildingArchetype.Shop;

                //  Cell budget: whole 3 m window cells that fit the grid cell with a 2 m margin
                //  per side. Never below 3 cells (9 m) so buildings read at city scale.
                int maxCellsX = Mathf.Max(3, Mathf.FloorToInt((cellW - 4f) / 3f));
                int maxCellsZ = Mathf.Max(3, Mathf.FloorToInt((cellD - 4f) / 3f));

                var p = new BCG_BuildingParams {
                    archetype = archetype,
                    variant = rng.Next(0, 4),
                    cellsX = Mathf.Min(3 + rng.Next(0, 3), maxCellsX),
                    cellsZ = Mathf.Min(3 + rng.Next(0, 2), maxCellsZ),
                    floors = archetype == BCG_BuildingArchetype.Tower ? 6 + rng.Next(0, 5)
                           : archetype == BCG_BuildingArchetype.Apartment ? 3 + rng.Next(0, 3)
                           : 1 + rng.Next(0, 2),
                    seed = rng.Next(1, 100000)
                };

                float cx = -plotSize.x * 0.5f + cellW * (col + 0.5f);
                float cz = -plotSize.y * 0.5f + cellD * (row + 0.5f);

                //  Jitter inside whatever slack the footprint leaves in its cell.
                float slackX = Mathf.Max(0f, (cellW - p.Width) * 0.5f - 0.5f);
                float slackZ = Mathf.Max(0f, (cellD - p.Depth) * 0.5f - 0.5f);
                cx += ((float)rng.NextDouble() * 2f - 1f) * slackX;
                cz += ((float)rng.NextDouble() * 2f - 1f) * slackZ;

                entries[i] = new BCG_TimelapseEntry {
                    buildingParams = p,
                    localPosition = new Vector3(cx, 0f, cz),
                    rotationY = row == 0 ? 180f : 0f,    //  both rows face outward
                    materialIndex = p.variant
                };

            }

            return entries;

        }

    }

}

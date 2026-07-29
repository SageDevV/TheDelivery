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
    /// A road network's opaque identity for the backend to populate.
    /// </summary>
    public struct BCG_RoadBackendNetwork {
        /// <summary>
        /// Human-readable name or label for this network (e.g. "Main Street", "Ring Road").
        /// </summary>
        public string label;

        /// <summary>
        /// Opaque handle owned by the backend (e.g. a spline ID, road marker reference, or custom state).
        /// </summary>
        public object handle;
    }

    /// <summary>
    /// The bridge interface for a road-generation backend (e.g. Road Constructor).
    /// </summary>
    public interface IBCG_RoadBackend {
        /// <summary>
        /// Display name for this backend (e.g. "Road Constructor").
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Lays the City Blocks street grid through this backend INSTEAD of the built-in road
        /// geometry. cityRoot already exists; called after Generate, before the populate job.
        /// Returns false with a human report on failure (the city still populates, roadless).
        /// </summary>
        bool LayGrid(BCG_CityBlockGenerator.CityBlockConfig config, GameObject cityRoot, out string report);

        /// <summary>
        /// Road networks in the open scene this backend can line with buildings.
        /// </summary>
        List<BCG_RoadBackendNetwork> FindNetworks();

        /// <summary>
        /// Lines one network with building rows. Returns built count; skipped = plots dropped.
        /// </summary>
        int PopulateAlong(object networkHandle, int seed, BCG_ZonePopulator.BCG_ZoneSettings settings, out int skipped);
    }

    /// <summary>
    /// Static registry of road backends. The bridge assembly self-registers under BCG_URBUGE_RC;
    /// an empty registry means no backend UI renders.
    /// </summary>
    public static class BCG_RoadBackendRegistry {
        private static readonly List<IBCG_RoadBackend> s_backends = new List<IBCG_RoadBackend>();
        private static IReadOnlyList<IBCG_RoadBackend> s_backendsCached;

        /// <summary>
        /// All registered backends (read-only view; cached).
        /// </summary>
        public static IReadOnlyList<IBCG_RoadBackend> Backends {
            get {
                if (s_backendsCached == null) {
                    s_backendsCached = s_backends.AsReadOnly();
                }
                return s_backendsCached;
            }
        }

        /// <summary>
        /// Returns true if at least one backend is registered.
        /// </summary>
        public static bool Any {
            get { return s_backends.Count > 0; }
        }

        /// <summary>
        /// Registers a backend. Null-safe and duplicate-safe: registering the same instance
        /// twice does not increase the count.
        /// </summary>
        public static void Register(IBCG_RoadBackend backend) {
            if (backend == null || s_backends.Contains(backend)) {
                return;
            }
            s_backends.Add(backend);
            s_backendsCached = null;  //  invalidate cache
        }

        /// <summary>
        /// Unregisters a backend.
        /// </summary>
        public static void Unregister(IBCG_RoadBackend backend) {
            if (s_backends.Remove(backend)) {
                s_backendsCached = null;  //  invalidate cache
            }
        }
    }

}

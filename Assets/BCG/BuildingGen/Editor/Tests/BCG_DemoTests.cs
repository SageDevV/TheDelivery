//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using NUnit.Framework;
using UnityEngine;
using BoneCrackerGames.BuildingGen;
using BoneCrackerGames.BuildingGen.Demo;

/// <summary>
/// EditMode tests for the playable-demo scripts (Demo/Scripts, BCG_BuildingGen.Demo asmdef):
/// info capture, day/night material mapping, and the fly camera's bounds clamp. The behaviours'
/// Update loops (input, raycast, cursor lock) are Play-Mode-only surface and are verified live.
/// </summary>
public class BCG_DemoTests {

    //  BCG_DemoCinematic.Finish persists a machine-local "intro seen" PlayerPrefs flag; bracket
    //  every test so tour-finishing tests can never rewrite this machine's real demo state.
    const string kIntroSeenKey = "BCG.BuildingGen.Demo.IntroSeen";
    bool introSeenHadKey;
    int introSeenPrior;

    [SetUp]
    public void SnapshotIntroSeenPref() {
        introSeenHadKey = PlayerPrefs.HasKey(kIntroSeenKey);
        introSeenPrior = PlayerPrefs.GetInt(kIntroSeenKey, 0);
    }

    [TearDown]
    public void RestoreIntroSeenPref() {
        if (introSeenHadKey)
            PlayerPrefs.SetInt(kIntroSeenKey, introSeenPrior);
        else
            PlayerPrefs.DeleteKey(kIntroSeenKey);
    }

    [Test]
    public void DemoInfo_Capture_ReadsMarkerFactsAndMeshCounts() {

        var p = new BCG_BuildingParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2, cellsX = 7, cellsZ = 5, floors = 9, seed = 880011
        };

        Material material = new Material(Shader.Find("Standard"));
        GameObject go = BCG_RuntimeBuildingFactory.Build(p, material);

        try {
            BCG_BuildingMarker marker = go.GetComponent<BCG_BuildingMarker>();
            BCG_DemoBuildingInfo info = BCG_DemoBuildingInfo.Capture(marker);

            Assert.IsNotNull(info);
            Assert.AreEqual(BCG_BuildingArchetype.Tower, info.archetype);
            Assert.AreEqual(2, info.variant);
            Assert.AreEqual(880011, info.seed);
            Assert.AreEqual(marker.footprintWidth, info.width, 0.001f);
            Assert.AreEqual(marker.footprintDepth, info.depth, 0.001f);
            Assert.AreEqual(marker.footprintHeight, info.height, 0.001f);

            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(mesh.vertexCount, info.vertexCount, "Vertex count sums the building's meshes.");
            Assert.AreEqual(mesh.triangles.Length / 3, info.triangleCount, "Triangle count sums the building's meshes.");

            StringAssert.Contains("Tower", info.HeaderLine);
            StringAssert.Contains("880011", info.BodyText());
        } finally {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(material);
        }

    }

    [Test]
    public void DemoInfo_Capture_NullMarker_ReturnsNull() {
        Assert.IsNull(BCG_DemoBuildingInfo.Capture(null));
    }

    [Test]
    public void DemoDayNight_Map_MapsPairsBothWays_PassesUnknownThrough() {

        Material day = new Material(Shader.Find("Standard"));
        Material night = new Material(Shader.Find("Standard"));
        Material unrelated = new Material(Shader.Find("Standard"));

        var pairs = new[] { new BCG_DemoMaterialPair { day = day, night = night } };

        try {
            Assert.AreSame(night, BCG_DemoDayNight.Map(day, pairs, true), "day → night");
            Assert.AreSame(day, BCG_DemoDayNight.Map(night, pairs, false), "night → day");
            Assert.AreSame(day, BCG_DemoDayNight.Map(day, pairs, false), "already-day stays put");
            Assert.AreSame(unrelated, BCG_DemoDayNight.Map(unrelated, pairs, true), "unknown passes through");
            Assert.IsNull(BCG_DemoDayNight.Map(null, pairs, true), "null material passes through");
            Assert.AreSame(day, BCG_DemoDayNight.Map(day, null, true), "null pair table is a no-op");
        } finally {
            Object.DestroyImmediate(day);
            Object.DestroyImmediate(night);
            Object.DestroyImmediate(unrelated);
        }

    }

    [Test]
    public void DemoDayNight_InvalidateRendererCache_LaterBuildingsJoinTheSwap() {

        float ambient = RenderSettings.ambientIntensity;

        Material day = new Material(Shader.Find("Standard"));
        Material night = new Material(Shader.Find("Standard"));

        var host = new GameObject("DayNightHost");
        GameObject early = null, late = null;

        try {
            var dn = host.AddComponent<BCG_DemoDayNight>();
            dn.facadePairs = new[] { new BCG_DemoMaterialPair { day = day, night = night } };

            var p = new BCG_BuildingParams { cellsX = 3, cellsZ = 3, floors = 2, seed = 11 };
            early = BCG_RuntimeBuildingFactory.Build(p, day, false);

            dn.SetNight(true);
            Assert.AreSame(night, early.GetComponent<MeshRenderer>().sharedMaterial, "Early building swaps.");

            var p2 = new BCG_BuildingParams { cellsX = 3, cellsZ = 3, floors = 2, seed = 22 };
            late = BCG_RuntimeBuildingFactory.Build(p2, day, false);

            dn.SetNight(true);
            Assert.AreSame(day, late.GetComponent<MeshRenderer>().sharedMaterial,
                "Stale non-empty cache misses the late building (pins the bug the API fixes).");

            dn.InvalidateRendererCache();
            dn.SetNight(true);
            Assert.AreSame(night, late.GetComponent<MeshRenderer>().sharedMaterial, "After invalidation the late building joins the swap.");
        } finally {
            RenderSettings.ambientIntensity = ambient;
            if (early != null) Object.DestroyImmediate(early);
            if (late != null) Object.DestroyImmediate(late);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(day);
            Object.DestroyImmediate(night);
        }

    }

    [Test]
    public void DemoTimelapse_BuildLayout_IsDeterministic_AndInsidePlot() {

        Vector2 plot = new Vector2(44f, 30f);
        BCG_TimelapseEntry[] a = BCG_DemoTimelapse.BuildLayout(4711, plot, 8);
        BCG_TimelapseEntry[] b = BCG_DemoTimelapse.BuildLayout(4711, plot, 8);

        Assert.AreEqual(8, a.Length);

        for (int i = 0; i < a.Length; i++) {
            Assert.AreEqual(a[i].buildingParams.archetype, b[i].buildingParams.archetype, "arch " + i);
            Assert.AreEqual(a[i].buildingParams.seed, b[i].buildingParams.seed, "seed " + i);
            Assert.AreEqual(a[i].localPosition, b[i].localPosition, "pos " + i);
            Assert.AreEqual(a[i].materialIndex, b[i].materialIndex, "mat " + i);

            float halfW = a[i].buildingParams.Width * 0.5f;
            float halfD = a[i].buildingParams.Depth * 0.5f;
            Assert.LessOrEqual(Mathf.Abs(a[i].localPosition.x) + halfW, plot.x * 0.5f + 0.001f, "x-fit " + i);
            Assert.LessOrEqual(Mathf.Abs(a[i].localPosition.z) + halfD, plot.y * 0.5f + 0.001f, "z-fit " + i);
            Assert.That(a[i].materialIndex, Is.InRange(0, 3), "variant index " + i);
        }

        BCG_TimelapseEntry[] c = BCG_DemoTimelapse.BuildLayout(999, plot, 8);
        bool anyDifferent = false;
        for (int i = 0; i < c.Length; i++)
            if (c[i].buildingParams.seed != a[i].buildingParams.seed) anyDifferent = true;
        Assert.IsTrue(anyDifferent, "Different seed produces a different layout.");

    }

    [Test]
    public void DemoTimelapse_CompleteInstantly_SpawnsAll_DespawnAllRemoves() {

        Material mat = new Material(Shader.Find("Standard"));
        var go = new GameObject("TimelapsePlot");

        try {
            var tl = go.AddComponent<BCG_DemoTimelapse>();
            tl.seed = 4711;
            tl.plotSize = new Vector2(44f, 30f);
            tl.buildingCount = 6;
            tl.facadeMaterials = new[] { mat };

            Assert.IsFalse(tl.IsComplete);

            tl.CompleteInstantly();   //  never Begin()-ed — must still spawn everything (skip path)
            Assert.IsTrue(tl.IsComplete);
            Assert.AreEqual(6, tl.SpawnedCount);
            Assert.AreEqual(6, go.GetComponentsInChildren<BCG_BuildingMarker>().Length);

            foreach (BCG_BuildingMarker m in go.GetComponentsInChildren<BCG_BuildingMarker>())
                Assert.AreEqual(1f, m.transform.localScale.y, 0.001f, "grow-in finished");

            tl.CompleteInstantly();   //  idempotent
            Assert.AreEqual(6, tl.SpawnedCount);

            tl.DespawnAll();
            Assert.IsFalse(tl.IsComplete);
            Assert.AreEqual(0, tl.SpawnedCount);
            Assert.AreEqual(0, go.GetComponentsInChildren<BCG_BuildingMarker>().Length);
        } finally {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mat);
        }

    }

    [Test]
    public void DemoCinematic_EvaluatePath_EndpointsExact_Continuous_TwoPointLerp_Clamped() {

        Vector3[] path = {
            new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f),
            new Vector3(10f, 0f, 10f), new Vector3(0f, 0f, 10f)
        };

        Assert.AreEqual(path[0], BCG_DemoCinematic.EvaluatePath(path, 0f), "t=0 is the first point");
        Assert.AreEqual(path[3], BCG_DemoCinematic.EvaluatePath(path, 1f), "t=1 is the last point");
        Assert.AreEqual(path[0], BCG_DemoCinematic.EvaluatePath(path, -5f), "t clamps low");
        Assert.AreEqual(path[3], BCG_DemoCinematic.EvaluatePath(path, 5f), "t clamps high");

        //  Continuity across the interior segment boundary (t = 1/3 for 3 segments).
        float boundary = 1f / 3f;
        Vector3 before = BCG_DemoCinematic.EvaluatePath(path, boundary - 0.0005f);
        Vector3 after = BCG_DemoCinematic.EvaluatePath(path, boundary + 0.0005f);
        Assert.Less((after - before).magnitude, 0.5f, "no jump across a segment boundary");

        //  Two points degrade to exact lerp.
        Vector3[] two = { Vector3.zero, new Vector3(8f, 0f, 0f) };
        Assert.AreEqual(new Vector3(4f, 0f, 0f), BCG_DemoCinematic.EvaluatePath(two, 0.5f), "2-point midpoint is the average");

        //  Degenerate inputs.
        Assert.AreEqual(Vector3.zero, BCG_DemoCinematic.EvaluatePath(new Vector3[0], 0.5f), "empty path is origin");
        Assert.AreEqual(new Vector3(3f, 3f, 3f), BCG_DemoCinematic.EvaluatePath(new[] { new Vector3(3f, 3f, 3f) }, 0.7f), "1-point path is that point");

    }

    [Test]
    public void DemoCinematic_CaptionAlpha_FadesInHoldsFadesOut() {

        Assert.AreEqual(0f, BCG_DemoCinematic.CaptionAlpha(0f, 10f, 0.5f), 0.001f, "starts transparent");
        Assert.AreEqual(1f, BCG_DemoCinematic.CaptionAlpha(0.5f, 10f, 0.5f), 0.001f, "faded in");
        Assert.AreEqual(1f, BCG_DemoCinematic.CaptionAlpha(5f, 10f, 0.5f), 0.001f, "holds");
        Assert.AreEqual(0.5f, BCG_DemoCinematic.CaptionAlpha(9.75f, 10f, 0.5f), 0.001f, "fading out");
        Assert.AreEqual(0f, BCG_DemoCinematic.CaptionAlpha(10f, 10f, 0.5f), 0.001f, "ends transparent");
        Assert.AreEqual(1f, BCG_DemoCinematic.CaptionAlpha(1f, 2f, 0f), 0.001f, "zero fade = fully on");

    }

    static BCG_DemoCinematic BuildCinematicRig(out Camera cam, out BCG_DemoFlyCamera fly, int shotCount) {

        var camGO = new GameObject("CineTestCam");
        cam = camGO.AddComponent<Camera>();
        camGO.AddComponent<CharacterController>();
        fly = camGO.AddComponent<BCG_DemoFlyCamera>();

        var rigGO = new GameObject("CineTestRig");
        var cine = rigGO.AddComponent<BCG_DemoCinematic>();
        cine.targetCamera = cam;
        cine.flyCamera = fly;
        cine.autoPlayOnStart = false;

        cine.shots = new BCG_DemoShot[shotCount];

        for (int s = 0; s < shotCount; s++) {

            var wpA = new GameObject("wpA" + s).transform;
            var wpB = new GameObject("wpB" + s).transform;
            wpA.SetParent(rigGO.transform);
            wpB.SetParent(rigGO.transform);
            wpA.position = new Vector3(s * 20f, 10f, 0f);
            wpB.position = new Vector3(s * 20f + 10f, 10f, 0f);

            var look = new GameObject("look" + s).transform;
            look.SetParent(rigGO.transform);
            look.position = new Vector3(s * 20f + 5f, 0f, 30f);

            cine.shots[s] = new BCG_DemoShot {
                title = "Shot " + s, body = "Body " + s,
                waypoints = new[] { wpA, wpB }, lookTarget = look, duration = 1f
            };

        }

        return cine;

    }

    [Test]
    public void DemoCinematic_TickAdvancesShots_FinishRestoresControl() {

        Camera cam; BCG_DemoFlyCamera fly;
        BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 3);

        try {
            cine.Play();
            Assert.IsTrue(cine.IsPlaying);
            Assert.AreEqual(0, cine.CurrentShot);
            Assert.IsFalse(fly.enabled, "fly camera off while playing");

            cine.Tick(0.5f);
            Assert.AreEqual(0, cine.CurrentShot);
            cine.Tick(0.6f);
            Assert.AreEqual(1, cine.CurrentShot, "advances across shot boundary");
            cine.Tick(1.0f);
            Assert.AreEqual(2, cine.CurrentShot);
            cine.Tick(1.1f);

            Assert.IsFalse(cine.IsPlaying, "tour finished");
            Assert.IsTrue(fly.enabled, "fly camera restored");
            Vector3 finale = new Vector3(2 * 20f + 10f, 10f, 0f);
            Assert.Less((cam.transform.position - finale).magnitude, 0.01f, "camera parked at the finale pose");
        } finally {
            Object.DestroyImmediate(cine.transform.root.gameObject);
            Object.DestroyImmediate(cam.gameObject);
        }

    }

    [Test]
    public void DemoCinematic_SkipFromAnyShot_LandsInCanonicalEndState() {

        for (int skipAt = 0; skipAt < 3; skipAt++) {

            Camera cam; BCG_DemoFlyCamera fly;
            BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 3);

            try {
                cine.Play();
                for (int s = 0; s < skipAt; s++)
                    cine.Tick(1.05f);

                cine.Skip();

                Assert.IsFalse(cine.IsPlaying, "skip@" + skipAt);
                Assert.IsTrue(fly.enabled, "fly restored skip@" + skipAt);
                Vector3 finale = new Vector3(2 * 20f + 10f, 10f, 0f);
                Assert.Less((cam.transform.position - finale).magnitude, 0.01f, "finale pose skip@" + skipAt);
            } finally {
                Object.DestroyImmediate(cine.transform.root.gameObject);
                Object.DestroyImmediate(cam.gameObject);
            }

        }

    }

    [Test]
    public void DemoCinematic_EndState_RestoresDay_AfterNightBeat_SkipAndNaturalFinish() {

        float ambient = RenderSettings.ambientIntensity;

        Material day = new Material(Shader.Find("Standard"));
        Material night = new Material(Shader.Find("Standard"));

        Camera cam; BCG_DemoFlyCamera fly;
        BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 3);
        var dnHost = new GameObject("CineTestDayNight");

        try {
            var dn = dnHost.AddComponent<BCG_DemoDayNight>();
            dn.facadePairs = new[] { new BCG_DemoMaterialPair { day = day, night = night } };
            cine.dayNight = dn;
            cine.shots[1].action = BCG_DemoShotAction.SetNight;

            //  Skip while the night beat is active → day restored.
            cine.Play();
            cine.Tick(1.05f);
            Assert.IsTrue(dn.IsNight, "night beat fired on entering shot 1");
            cine.Skip();
            Assert.IsFalse(cine.IsPlaying);
            Assert.IsFalse(dn.IsNight, "skip mid-night restores the authored day state");

            //  Natural finish → day restored too.
            cine.Replay();
            cine.Tick(1.05f);
            Assert.IsTrue(dn.IsNight, "night beat fired again on replay");
            cine.Tick(1.05f);
            cine.Tick(1.05f);
            Assert.IsFalse(cine.IsPlaying, "tour finished naturally");
            Assert.IsFalse(dn.IsNight, "natural finish restores the authored day state");
        } finally {
            RenderSettings.ambientIntensity = ambient;
            Object.DestroyImmediate(cine.transform.root.gameObject);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(dnHost);
            Object.DestroyImmediate(day);
            Object.DestroyImmediate(night);
        }

    }

    [Test]
    public void DemoCinematic_PlaybackSpeed_PropagatesToTimelapse() {

        Material mat = new Material(Shader.Find("Standard"));
        Camera cam; BCG_DemoFlyCamera fly;
        BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 2);
        var plotGO = new GameObject("CineTestPlot");

        try {
            var tl = plotGO.AddComponent<BCG_DemoTimelapse>();
            tl.buildingCount = 2;
            tl.facadeMaterials = new[] { mat };
            cine.timelapse = tl;
            cine.playbackSpeed = 2.5f;
            cine.shots[0].action = BCG_DemoShotAction.BeginTimelapse;

            cine.Play();
            Assert.AreEqual(2.5f, tl.speedMultiplier, 0.001f, "director hands its playback speed to the timelapse at Begin");
        } finally {
            Object.DestroyImmediate(cine.transform.root.gameObject);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(plotGO);
            Object.DestroyImmediate(mat);
        }

    }

    [Test]
    public void DemoCinematic_OverlayAlpha_BlackAtStart_ClearMidTour_BlackAtEnd() {

        Assert.AreEqual(1f, BCG_DemoCinematic.OverlayAlpha(0f, float.PositiveInfinity, 0.8f), 0.001f, "opens on black");
        Assert.AreEqual(0.5f, BCG_DemoCinematic.OverlayAlpha(0.4f, float.PositiveInfinity, 0.8f), 0.001f, "fading in");
        Assert.AreEqual(0f, BCG_DemoCinematic.OverlayAlpha(5f, float.PositiveInfinity, 0.8f), 0.001f, "clear mid-tour");
        Assert.AreEqual(0.5f, BCG_DemoCinematic.OverlayAlpha(60f, 0.4f, 0.8f), 0.001f, "fading out near the end");
        Assert.AreEqual(1f, BCG_DemoCinematic.OverlayAlpha(60f, 0f, 0.8f), 0.001f, "black at the end");
        Assert.AreEqual(0f, BCG_DemoCinematic.OverlayAlpha(0f, 0f, 0f), 0.001f, "fade 0 disables");

    }

    [Test]
    public void DemoCinematic_FadeOverlay_RunsRevealAfterFinish_ThenDeactivatesCanvas() {

        Camera cam; BCG_DemoFlyCamera fly;
        BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 2);

        var canvasGO = new GameObject("CineTestCanvas");
        var overlayGO = new GameObject("FadeOverlay");
        var siblingGO = new GameObject("Letterbox");
        overlayGO.transform.SetParent(canvasGO.transform, false);
        siblingGO.transform.SetParent(canvasGO.transform, false);
        var overlay = overlayGO.AddComponent<UnityEngine.UI.Image>();

        try {
            cine.captionCanvasRoot = canvasGO;
            cine.fadeOverlay = overlay;
            cine.fadeDuration = 0.8f;
            cine.shots[0].fadeIn = true;
            cine.shots[1].fadeOut = true;

            cine.Play();
            Assert.AreEqual(1f, overlay.color.a, 0.001f, "opens on black");
            Assert.IsTrue(siblingGO.activeSelf, "all overlay elements active while playing");

            cine.Tick(0.9f);
            Assert.AreEqual(0f, overlay.color.a, 0.001f, "clear after the fade-in (shot 0)");

            cine.Tick(0.9f);   //  into the final shot (t = 0.8, 0.2 s left) → fading out
            Assert.Greater(overlay.color.a, 0.5f, "ramping to black near the end");

            cine.Tick(0.3f);   //  past the end → Finish
            Assert.IsFalse(cine.IsPlaying);
            Assert.IsTrue(canvasGO.activeSelf, "canvas stays on for the reveal");
            Assert.AreEqual(1f, overlay.color.a, 0.001f, "handoff happens under black");
            Assert.IsFalse(siblingGO.activeSelf, "only the fade image survives the handoff");

            cine.Tick(0.5f);   //  reveal in progress
            Assert.IsTrue(canvasGO.activeSelf);
            Assert.AreEqual(0.375f, overlay.color.a, 0.01f, "revealing gameplay");

            cine.Tick(0.4f);   //  reveal done
            Assert.IsFalse(canvasGO.activeSelf, "canvas off once the reveal completes");

            //  Replay re-arms everything.
            cine.Replay();
            Assert.IsTrue(canvasGO.activeSelf && siblingGO.activeSelf && overlayGO.activeSelf, "replay re-activates the overlay elements");
            Assert.AreEqual(1f, overlay.color.a, 0.001f, "replay opens on black again");
        } finally {
            Object.DestroyImmediate(cine.transform.root.gameObject);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(canvasGO);
        }

    }

    [Test]
    public void DemoCinematic_PerShotFadeFlags_SelectWhereFadesApply() {

        Camera cam; BCG_DemoFlyCamera fly;
        BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 3);

        var canvasGO = new GameObject("CineTestCanvas2");
        var overlayGO = new GameObject("FadeOverlay");
        overlayGO.transform.SetParent(canvasGO.transform, false);
        var overlay = overlayGO.AddComponent<UnityEngine.UI.Image>();

        try {
            cine.captionCanvasRoot = canvasGO;
            cine.fadeOverlay = overlay;
            cine.fadeDuration = 0.8f;
            //  Only the MIDDLE shot fades: out at its end, and in at its start. First/last stay hard.
            cine.shots[1].fadeIn = true;
            cine.shots[1].fadeOut = true;

            cine.Play();
            Assert.AreEqual(0f, overlay.color.a, 0.001f, "no fade-in flag on shot 0 — opens clear");

            cine.Tick(1.1f);   //  into shot 1 at t = 0.1 — fading in
            Assert.AreEqual(1, cine.CurrentShot);
            Assert.Greater(overlay.color.a, 0.8f, "mid shot fades in from black");

            cine.Tick(0.7f);   //  shot 1 t = 0.8, 0.2 s left — fading out
            Assert.Greater(overlay.color.a, 0.5f, "mid shot fades out to black");

            cine.Tick(0.3f);   //  into shot 2 at t = 0.1 — no fadeIn flag
            Assert.AreEqual(2, cine.CurrentShot);
            Assert.AreEqual(0f, overlay.color.a, 0.001f, "no fade-in flag on shot 2 — clear again");

            cine.Tick(1.0f);   //  natural finish; last shot has NO fadeOut → direct handoff, no reveal
            Assert.IsFalse(cine.IsPlaying);
            Assert.IsFalse(canvasGO.activeSelf, "no fade-out on the last shot: canvas goes straight off, no reveal");

            //  Skip always hands off under the fade, regardless of flags.
            cine.Replay();
            cine.Skip();
            Assert.IsTrue(canvasGO.activeSelf, "skip hands off under the fade even with no shot flags");
            Assert.AreEqual(1f, overlay.color.a, 0.001f, "skip cuts to black");
            cine.Tick(1.0f);
            Assert.IsFalse(canvasGO.activeSelf, "skip reveal completes");
        } finally {
            Object.DestroyImmediate(cine.transform.root.gameObject);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(canvasGO);
        }

    }

    [Test]
    public void DemoCinematic_FirstRunCantSkip_LocksInputSkipUntilSeen() {

        PlayerPrefs.DeleteKey(kIntroSeenKey);   //  simulate a genuinely fresh machine

        Camera cam; BCG_DemoFlyCamera fly;
        BCG_DemoCinematic cine = BuildCinematicRig(out cam, out fly, 2);
        var hintGO = new GameObject("SkipHint");

        try {
            cine.skipHint = hintGO;
            cine.firstRunCantSkip = true;

            cine.Play();
            Assert.IsFalse(cine.CanSkip, "first ever run is input-skip-locked");
            Assert.IsFalse(hintGO.activeSelf, "skip hint hidden while locked");

            cine.Tick(1.05f);
            cine.Tick(1.05f);   //  natural finish marks the intro as seen
            Assert.IsFalse(cine.IsPlaying);
            Assert.AreEqual(1, PlayerPrefs.GetInt(kIntroSeenKey, 0), "completion persists the seen flag");

            cine.Replay();
            Assert.IsTrue(cine.CanSkip, "after the first completion skipping unlocks");
            Assert.IsTrue(hintGO.activeSelf, "skip hint visible once unlocked");
            cine.Skip();

            //  Toggle off: never locked, even on a fresh machine.
            PlayerPrefs.DeleteKey(kIntroSeenKey);
            cine.firstRunCantSkip = false;
            cine.Replay();
            Assert.IsTrue(cine.CanSkip, "toggle off never locks");
            cine.Skip();
        } finally {
            Object.DestroyImmediate(cine.transform.root.gameObject);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(hintGO);
        }

    }

    [Test]
    public void DemoCinematic_PlayWithNoShots_DoesNotStart() {

        var rigGO = new GameObject("CineEmpty");

        try {
            var cine = rigGO.AddComponent<BCG_DemoCinematic>();
            cine.autoPlayOnStart = false;
            cine.shots = new BCG_DemoShot[0];
            cine.Play();
            Assert.IsFalse(cine.IsPlaying, "nothing to play");
        } finally {
            Object.DestroyImmediate(rigGO);
        }

    }

    [Test]
    public void DemoFlyCamera_ClampToBounds_ClampsPerAxis_KeepsInsidePoints() {

        Vector3 center = new Vector3(0f, 60f, 0f);
        Vector3 extents = new Vector3(250f, 90f, 250f);

        Vector3 inside = new Vector3(10f, 40f, -200f);
        Assert.AreEqual(inside, BCG_DemoFlyCamera.ClampToBounds(inside, center, extents), "Inside points pass through.");

        Vector3 outside = new Vector3(9999f, -500f, -9999f);
        Vector3 clamped = BCG_DemoFlyCamera.ClampToBounds(outside, center, extents);
        Assert.AreEqual(250f, clamped.x, 0.001f);
        Assert.AreEqual(-30f, clamped.y, 0.001f);
        Assert.AreEqual(-250f, clamped.z, 0.001f);

    }

}

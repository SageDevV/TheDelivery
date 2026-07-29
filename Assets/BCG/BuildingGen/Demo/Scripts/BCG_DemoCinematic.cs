//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>What a shot triggers the moment it starts.</summary>
    public enum BCG_DemoShotAction { None, BeginTimelapse, SetNight, SetDay }

    /// <summary>One cinematic shot: a camera path, a look target, a caption card, a beat action.</summary>
    [Serializable]
    public class BCG_DemoShot {

        [Tooltip("Caption card title (feature name).")]
        public string title;

        [Tooltip("Caption card body line.")]
        [TextArea] public string body;

        [Tooltip("Camera path control points, flown through in order (Catmull-Rom; 2 points = straight dolly).")]
        public Transform[] waypoints;

        [Tooltip("What the camera faces during the shot.")]
        public Transform lookTarget;

        [Tooltip("Shot length in seconds.")]
        [Min(0.5f)] public float duration = 10f;

        [Tooltip("Path progress easing over the shot (0..1 → 0..1).")]
        public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Fired once when the shot starts.")]
        public BCG_DemoShotAction action = BCG_DemoShotAction.None;

        [Tooltip("Fade from black as this shot starts (uses the director's Fade Duration).")]
        public bool fadeIn;

        [Tooltip("Fade to black as this shot ends (uses the director's Fade Duration).")]
        public bool fadeOut;

    }

    /// <summary>
    /// The demo's cinematic intro director. Flies the demo camera through an authored shot list
    /// (Catmull-Rom path + damped look-at + subtle Perlin sway), shows one feature caption card
    /// per shot on the cinematic canvas, and fires each shot's beat action (timelapse / night).
    /// Any key or click skips; C replays after it ends. Natural finish and skip both land in the
    /// same canonical end state: timelapse complete, the authored DAY state restored, camera at
    /// the finale pose, fly camera + inspector + normal UI restored. Pure math (path, caption
    /// fade) is static and unit-tested; Update is input + Tick(deltaTime), so sequencing is
    /// testable without input.
    /// </summary>
    public class BCG_DemoCinematic : MonoBehaviour {

        [Tooltip("Play the intro automatically when the scene starts.")]
        public bool autoPlayOnStart = true;

        [Tooltip("When on, the very first run of the intro on a machine cannot be skipped by input — new users see the whole feature tour once (tracked via PlayerPrefs; the skip hint hides while locked). Later runs and same-session replays skip normally. Direct Skip() calls are never blocked.")]
        public bool firstRunCantSkip = true;

        [Tooltip("Playback speed multiplier — 2 = twice as fast, 0.5 = half speed. Scales the whole tour: shot timing, camera motion, caption fades, and the timelapse cadence.")]
        [Min(0.1f)] public float playbackSpeed = 1f;

        [Tooltip("The shot list, played in order.")]
        public BCG_DemoShot[] shots;

        [Header("Scene wiring")]
        [Tooltip("Camera the director drives. Falls back to Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Fly camera to suspend while playing (with its CharacterController).")]
        public BCG_DemoFlyCamera flyCamera;

        [Tooltip("Building inspector to suspend while playing (a skip click must not select a building). Optional.")]
        public BCG_DemoBuildingInspector inspector;

        [Tooltip("Day/night controller (SetNight beat + canonical night end state). Optional.")]
        public BCG_DemoDayNight dayNight;

        [Tooltip("Timelapse beat on the reserved plot. Optional.")]
        public BCG_DemoTimelapse timelapse;

        [Tooltip("Objects hidden while the intro plays (the normal demo canvas). Restored after.")]
        public GameObject[] hideWhilePlaying;

        [Header("Caption overlay")]
        [Tooltip("Root of the cinematic canvas (letterbox + captions). Activated only while playing.")]
        public GameObject captionCanvasRoot;

        [Tooltip("CanvasGroup carrying the caption labels (faded per shot).")]
        public CanvasGroup captionGroup;

        [Tooltip("Caption title label.")]
        public Text captionTitle;

        [Tooltip("Caption body label.")]
        public Text captionBody;

        [Tooltip("Caption fade in/out seconds inside each shot.")]
        [Min(0f)] public float captionFade = 0.4f;

        [Tooltip("The 'any key — skip intro' hint object. Hidden while input skipping is locked (first run with First Run Cant Skip on). Optional.")]
        public GameObject skipHint;

        [Tooltip("Fullscreen black image used for shot fades and the reveal over restored gameplay. Optional — unwired disables fading.")]
        public Image fadeOverlay;

        [Tooltip("Seconds of a fade to/from black. Which shots fade is chosen per shot (Fade In / Fade Out on each shot); skipping always hands control back under the fade. 0 disables all fading.")]
        [Min(0f)] public float fadeDuration = 0.8f;

        [Header("Feel")]
        [Tooltip("Handheld sway amplitude in degrees (0 disables).")]
        [Min(0f)] public float swayDegrees = 0.3f;

        [Tooltip("Handheld sway frequency in Hz.")]
        [Min(0f)] public float swayFrequency = 0.4f;

        [Tooltip("Look-at damping — higher snaps faster.")]
        [Min(0.1f)] public float lookDamping = 4f;

        /// <summary>True while the tour is running.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Index of the shot currently playing (valid while IsPlaying).</summary>
        public int CurrentShot { get; private set; }

        /// <summary>False while the running tour may not be skipped by input — the first ever run
        /// on this machine with <see cref="firstRunCantSkip"/> on. Direct Skip() calls ignore it.</summary>
        public bool CanSkip { get; private set; } = true;

        //  PlayerPrefs flag set the first time the intro completes (works in builds + WebGL).
        const string kIntroSeenKey = "BCG.BuildingGen.Demo.IntroSeen";

        float shotTime;
        float swayClock;
        float fadeRevealRemaining;
        bool warnedMissing;

        void Start() {

            if (targetCamera == null)
                targetCamera = Camera.main;

            if (autoPlayOnStart)
                Play();

        }

        void Update() {

            float dt = Time.deltaTime * Mathf.Max(0.1f, playbackSpeed);

            if (IsPlaying) {

                if (BCG_DemoInput.AnyInputDown && CanSkip) {
                    Skip();
                    return;
                }

                Tick(dt);

            } else {

                Tick(dt);    //  advances the post-tour reveal; no-op otherwise

                if (BCG_DemoInput.ReplayDown)
                    Replay();

            }

        }

        /// <summary>Starts the tour from the first shot. No-op while already playing or without
        /// shots / a camera.</summary>
        public void Play() {

            if (IsPlaying)
                return;

            if (targetCamera == null)
                targetCamera = Camera.main;

            if (shots == null || shots.Length == 0 || targetCamera == null) {
                WarnOnce("no shots authored or no camera found — intro disabled.");
                return;
            }

            IsPlaying = true;
            CurrentShot = 0;
            shotTime = 0f;
            swayClock = 0f;
            fadeRevealRemaining = 0f;

            SetPlaybackState(true);

            //  Re-activate every overlay element (a previous reveal leaves only the fade image on)
            //  and open on black only when the first shot asks to fade in.
            if (captionCanvasRoot != null)
                foreach (Transform child in captionCanvasRoot.transform)
                    child.gameObject.SetActive(true);

            ApplyOverlayAlpha(fadeOverlay != null && fadeDuration > 0f && shots[0].fadeIn ? 1f : 0f);

            //  First-run lock: input skipping is disabled until the intro has completed once on
            //  this machine. The hint must not promise a skip that is locked.
            CanSkip = !firstRunCantSkip || PlayerPrefs.GetInt(kIntroSeenKey, 0) == 1;

            if (skipHint != null)
                skipHint.SetActive(CanSkip);

            EnterShot(0);

            if (HasPath(shots[0]))
                ApplyPose(shots[0], 0f, 0f);    //  snap to the opening pose, no damp-in from wherever the camera was

        }

        /// <summary>Jumps straight to the canonical end state (any-input skip).</summary>
        public void Skip() {

            if (!IsPlaying)
                return;

            Finish(true);

        }

        /// <summary>Replays the tour after it ended: back to day, timelapse despawned, then Play.</summary>
        public void Replay() {

            if (IsPlaying)
                return;

            if (dayNight != null)
                dayNight.SetNight(false);

            if (timelapse != null)
                timelapse.DespawnAll();

            Play();

        }

        /// <summary>Deterministic playback heartbeat — advances time, fires shot entries, drives
        /// the camera and captions. Public and input-free so EditMode tests can step it.</summary>
        public void Tick(float dt) {

            if (!IsPlaying) {

                //  Post-tour reveal: the overlay fades off the restored gameplay, then the
                //  cinematic canvas deactivates for good (until Play re-activates it).
                if (fadeRevealRemaining > 0f) {

                    fadeRevealRemaining -= dt;
                    ApplyOverlayAlpha(Mathf.Clamp01(fadeRevealRemaining / Mathf.Max(0.01f, fadeDuration)));

                    if (fadeRevealRemaining <= 0f && captionCanvasRoot != null)
                        captionCanvasRoot.SetActive(false);

                }

                return;

            }

            shotTime += dt;
            swayClock += dt;

            //  Advance across as many shot boundaries as dt covered (carrying the remainder).
            while (IsPlaying && shotTime >= CurrentShotDuration()) {

                shotTime -= CurrentShotDuration();

                if (CurrentShot + 1 >= shots.Length) {
                    Finish(false);
                    return;
                }

                CurrentShot++;
                EnterShot(CurrentShot);

            }

            BCG_DemoShot shot = shots[CurrentShot];

            if (HasPath(shot))
                ApplyPose(shot, shotTime / shot.duration, dt);

            if (captionGroup != null)
                captionGroup.alpha = CaptionAlpha(shotTime, shot.duration, captionFade);

            if (fadeOverlay != null) {
                float sinceStart = shot.fadeIn ? shotTime : float.PositiveInfinity;
                float untilEnd = shot.fadeOut ? Mathf.Max(0f, shot.duration - shotTime) : float.PositiveInfinity;
                ApplyOverlayAlpha(OverlayAlpha(sinceStart, untilEnd, fadeDuration));
            }

        }

        void ApplyOverlayAlpha(float alpha) {

            if (fadeOverlay != null)
                fadeOverlay.color = new Color(0f, 0f, 0f, alpha);

        }

        float CurrentShotDuration() {
            return Mathf.Max(0.5f, shots[CurrentShot].duration);
        }

        static bool HasPath(BCG_DemoShot shot) {

            if (shot.waypoints == null || shot.waypoints.Length == 0)
                return false;

            foreach (Transform t in shot.waypoints)
                if (t == null)
                    return false;

            return true;

        }

        void EnterShot(int index) {

            BCG_DemoShot shot = shots[index];

            if (!HasPath(shot))
                WarnOnce("shot " + index + " has missing waypoints — playing it as a static hold.");

            if (captionTitle != null)
                captionTitle.text = shot.title;

            if (captionBody != null)
                captionBody.text = shot.body;

            switch (shot.action) {

                case BCG_DemoShotAction.BeginTimelapse:
                    if (timelapse != null) {
                        timelapse.speedMultiplier = playbackSpeed;    //  the grow-out keeps pace with its shot
                        timelapse.Begin();
                    } else {
                        WarnOnce("shot " + index + " asks for the timelapse but none is wired.");
                    }
                    break;

                case BCG_DemoShotAction.SetNight:
                    if (dayNight != null)
                        dayNight.SetNight(true);
                    else
                        WarnOnce("shot " + index + " asks for night mode but no day/night controller is wired.");
                    break;

                case BCG_DemoShotAction.SetDay:
                    if (dayNight != null)
                        dayNight.SetNight(false);
                    else
                        WarnOnce("shot " + index + " asks for day mode but no day/night controller is wired.");
                    break;

            }

        }

        void ApplyPose(BCG_DemoShot shot, float normalizedTime, float dt) {

            float eased = shot.ease != null && shot.ease.length >= 2 ? shot.ease.Evaluate(normalizedTime) : normalizedTime;

            Vector3[] points = new Vector3[shot.waypoints.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = shot.waypoints[i].position;

            Transform cam = targetCamera.transform;
            cam.position = EvaluatePath(points, eased);

            if (shot.lookTarget != null) {

                Vector3 to = shot.lookTarget.position - cam.position;

                if (to.sqrMagnitude > 0.0001f) {

                    Quaternion look = Quaternion.LookRotation(to);

                    //  dt = 0 (the Play() snap) sets the rotation outright.
                    cam.rotation = dt <= 0f ? look
                        : Quaternion.Slerp(cam.rotation, look, 1f - Mathf.Exp(-lookDamping * dt));

                }

            }

            if (swayDegrees > 0f) {

                float sp = (Mathf.PerlinNoise(swayClock * swayFrequency, 0.37f) - 0.5f) * 2f * swayDegrees;
                float sy = (Mathf.PerlinNoise(swayClock * swayFrequency, 7.91f) - 0.5f) * 2f * swayDegrees;
                cam.rotation = cam.rotation * Quaternion.Euler(sp, sy, 0f);

            }

        }

        /// <summary>Applies the canonical end state and returns control (natural finish + skip).</summary>
        void Finish(bool viaSkip) {

            IsPlaying = false;

            //  The tour has been seen — later runs (and same-session replays) may input-skip.
            PlayerPrefs.SetInt(kIntroSeenKey, 1);
            PlayerPrefs.Save();
            CanSkip = true;

            if (timelapse != null)
                timelapse.CompleteInstantly();

            //  The demo always hands back the scene exactly as authored: day. The night beats are
            //  mid-tour only (skipping during one restores day here too).
            if (dayNight != null)
                dayNight.SetNight(false);

            //  Park the camera at the end pose of the last shot that has a usable path.
            for (int i = shots.Length - 1; i >= 0; i--) {

                if (!HasPath(shots[i]))
                    continue;

                Vector3[] points = new Vector3[shots[i].waypoints.Length];
                for (int w = 0; w < points.Length; w++)
                    points[w] = shots[i].waypoints[w].position;

                Transform cam = targetCamera.transform;
                cam.position = EvaluatePath(points, 1f);

                if (shots[i].lookTarget != null) {
                    Vector3 to = shots[i].lookTarget.position - cam.position;
                    if (to.sqrMagnitude > 0.0001f)
                        cam.rotation = Quaternion.LookRotation(to);
                }

                break;

            }

            SetPlaybackState(false);

            //  Hand off under black, then reveal the restored gameplay: keep only the fade image
            //  active on the cinematic canvas and let Tick fade it out. Natural finish honors the
            //  last shot's Fade Out flag; skipping always hides the handoff behind the fade.
            bool handOffUnderFade = viaSkip || (shots.Length > 0 && shots[shots.Length - 1].fadeOut);

            if (handOffUnderFade && fadeOverlay != null && fadeDuration > 0f && captionCanvasRoot != null) {

                captionCanvasRoot.SetActive(true);

                foreach (Transform child in captionCanvasRoot.transform)
                    child.gameObject.SetActive(child.gameObject == fadeOverlay.gameObject);

                ApplyOverlayAlpha(1f);
                fadeRevealRemaining = fadeDuration;

            }

            if (flyCamera != null)
                flyCamera.SyncAnglesFromTransform();

        }

        /// <summary>Suspends / restores the interactive demo around playback.</summary>
        void SetPlaybackState(bool playing) {

            if (flyCamera != null) {

                flyCamera.enabled = !playing;

                CharacterController cc = flyCamera.GetComponent<CharacterController>();
                if (cc != null)
                    cc.enabled = !playing;

            }

            if (inspector != null)
                inspector.enabled = !playing;

            if (hideWhilePlaying != null)
                foreach (GameObject go in hideWhilePlaying)
                    if (go != null)
                        go.SetActive(!playing);

            if (captionCanvasRoot != null)
                captionCanvasRoot.SetActive(playing);

            if (captionGroup != null)
                captionGroup.alpha = 0f;

            if (playing) {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

        }

        void WarnOnce(string message) {

            if (warnedMissing)
                return;

            warnedMissing = true;
            Debug.LogWarning("[BCG BuildingGen Demo] Cinematic: " + message, this);

        }

        void OnDrawGizmosSelected() {

            if (shots == null)
                return;

            Gizmos.color = new Color(1f, 0.62f, 0.3f, 0.9f);

            foreach (BCG_DemoShot shot in shots) {

                if (shot == null || !HasPath(shot))
                    continue;

                Vector3[] points = new Vector3[shot.waypoints.Length];
                for (int i = 0; i < points.Length; i++)
                    points[i] = shot.waypoints[i].position;

                Vector3 prev = EvaluatePath(points, 0f);

                for (int s = 1; s <= 32; s++) {
                    Vector3 next = EvaluatePath(points, s / 32f);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }

            }

        }

        /// <summary>
        /// Evaluates a Catmull-Rom spline through the points at t ∈ [0, 1] (clamped). Endpoints are
        /// duplicated (the curve passes through every point, including both ends). Two points are
        /// an exact lerp; one point returns itself; empty returns origin. Pure, unit-tested.
        /// </summary>
        public static Vector3 EvaluatePath(Vector3[] points, float t) {

            if (points == null || points.Length == 0)
                return Vector3.zero;

            if (points.Length == 1)
                return points[0];

            t = Mathf.Clamp01(t);

            if (points.Length == 2)
                return Vector3.Lerp(points[0], points[1], t);

            int segments = points.Length - 1;
            float scaled = t * segments;
            int segment = Mathf.Min(Mathf.FloorToInt(scaled), segments - 1);
            float u = scaled - segment;

            Vector3 p0 = points[Mathf.Max(segment - 1, 0)];
            Vector3 p1 = points[segment];
            Vector3 p2 = points[segment + 1];
            Vector3 p3 = points[Mathf.Min(segment + 2, points.Length - 1)];

            float u2 = u * u;
            float u3 = u2 * u;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * u +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * u3);

        }

        /// <summary>Black-overlay opacity across the whole tour: opaque at t = 0 fading in over
        /// <paramref name="fade"/> seconds, transparent mid-tour, ramping back to opaque over the
        /// final <paramref name="fade"/> seconds (pass <see cref="float.PositiveInfinity"/> as
        /// <paramref name="timeUntilEnd"/> outside the final shot). fade = 0 disables (always
        /// transparent). Pure, unit-tested.</summary>
        public static float OverlayAlpha(float timeSincePlay, float timeUntilEnd, float fade) {

            if (fade <= 0f)
                return 0f;

            float fadeIn = 1f - Mathf.Clamp01(timeSincePlay / fade);
            float fadeOut = 1f - Mathf.Clamp01(timeUntilEnd / fade);
            return Mathf.Max(fadeIn, fadeOut);

        }

        /// <summary>Caption opacity inside one shot: fade in over the first <paramref name="fade"/>
        /// seconds, hold, fade out over the last. fade = 0 means always fully visible. Pure,
        /// unit-tested.</summary>
        public static float CaptionAlpha(float shotTime, float shotDuration, float fade) {

            if (fade <= 0f)
                return 1f;

            float alphaIn = Mathf.Clamp01(shotTime / fade);
            float alphaOut = Mathf.Clamp01((shotDuration - shotTime) / fade);
            return Mathf.Min(alphaIn, alphaOut);

        }

    }

}

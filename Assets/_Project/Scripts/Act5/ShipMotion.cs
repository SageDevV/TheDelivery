using UnityEngine;

namespace TheDelivery.Act5
{
    // The deck, navigation mesh and player collider stay in the ship's frame of reference.
    [RequireComponent(typeof(AmbientOneShots))]
    public sealed class ShipMotion : MonoBehaviour
    {
        [SerializeField] private Transform[] chains = System.Array.Empty<Transform>();
        [SerializeField] private WaterRipples water;
        private Quaternion[] chainRest;
        private Transform viewPivot;
        private AmbientOneShots hull;
        private float joltStart = float.NegativeInfinity;
        private const string Preference = "Act5.CameraMotion";
        public enum Comfort { Normal, Reduced, Off }

        public static Comfort CameraComfort { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadComfort()
        {
            CameraComfort = (Comfort)Mathf.Clamp(PlayerPrefs.GetInt(Preference, 0), 0, 2);
        }

        public static void CycleComfort()
        {
            CameraComfort = (Comfort)(((int)CameraComfort + 1) % 3);
            PlayerPrefs.SetInt(Preference, (int)CameraComfort);
            PlayerPrefs.Save();
        }

        private void Awake()
        {
            hull = GetComponent<AmbientOneShots>();
            chainRest = new Quaternion[chains.Length];
            for (int i = 0; i < chains.Length; i++)
                if (chains[i] != null) chainRest[i] = chains[i].localRotation;
        }

        private void OnEnable() => hull.Played += OnGroan;
        private void OnDisable()
        {
            hull.Played -= OnGroan;
            if (viewPivot != null) viewPivot.localRotation = Quaternion.identity;
            for (int i = 0; i < chains.Length; i++)
                if (chains[i] != null) chains[i].localRotation = chainRest[i];
            if (water != null) water.Slosh = 0f;
        }

        private void OnGroan() => joltStart = Time.time;

        private void Start()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Separate pivot preserves wake poses, hiding, player pitch and the camera's Q/E lean.
            viewPivot = new GameObject("ShipViewPivot").transform;
            viewPivot.SetParent(cam.transform.parent, false);
            cam.transform.SetParent(viewPivot, false);
        }

        private void LateUpdate()
        {
            float phase = Time.timeSinceLevelLoad * (2f * Mathf.PI / 7f);
            float age = Time.time - joltStart;
            float jolt = age < 2.8f ? Mathf.Exp(-2.2f * age) * Mathf.Sin(9f * age) : 0f;
            float roll = 1.25f * Mathf.Sin(phase) + 1.6f * jolt;
            float pitch = 0.32f * Mathf.Sin(phase + 0.7f) + 0.45f * jolt;

            if (viewPivot != null)
            {
                float gain = CameraComfort == Comfort.Normal ? 1f : CameraComfort == Comfort.Reduced ? 0.35f : 0f;
                Quaternion tilt = Quaternion.Euler(pitch * gain, 0f, roll * gain);
                Quaternion parentRotation = viewPivot.parent.rotation;
                viewPivot.localRotation = Quaternion.Inverse(parentRotation) * tilt * parentRotation;
            }

            // Same swell as the deck, with the lag of a suspended load.
            Quaternion swing = Quaternion.Euler(0.9f * Mathf.Sin(phase + 0.4f), 0f,
                -4.2f * Mathf.Sin(phase - 0.35f) - 6f * jolt);
            for (int i = 0; i < chains.Length; i++)
                if (chains[i] != null) chains[i].localRotation = swing * chainRest[i];
            if (water != null) water.Slosh = Mathf.Sin(phase - 0.55f) + 0.35f * jolt;
        }
    }
}

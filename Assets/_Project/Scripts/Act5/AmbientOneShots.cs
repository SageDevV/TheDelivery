using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Sons de ambiente avulsos em intervalos aleatórios, 3D, em volta do jogador (ex.: o casco do navio gemendo).
    /// Cada som sai de um ponto sorteado a 6–16 m da câmera, para nunca vir sempre do mesmo lado.
    /// </summary>
    public sealed class AmbientOneShots : MonoBehaviour
    {
        [SerializeField] private AudioClip[] clips;
        [SerializeField] private Vector2 interval = new Vector2(24f, 48f);
        [SerializeField] private Vector2 distance = new Vector2(6f, 16f);
        [SerializeField] private float volume = 0.8f;
        [SerializeField] private Vector2 pitch = new Vector2(0.85f, 1.05f);

        public event System.Action Played;

        private AudioSource src;
        private float next;

        private void Start()
        {
            var go = new GameObject("AmbientOneShotSource"); go.transform.SetParent(transform, false);
            src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.minDistance = 4f; src.maxDistance = 40f; src.rolloffMode = AudioRolloffMode.Linear; src.playOnAwake = false;
            next = Time.time + Random.Range(interval.x * 0.4f, interval.y * 0.6f);
        }

        private void Update()
        {
            if (Time.time >= next) PlayNow();
        }

        public void PlayNow()
        {
            if (src == null || clips == null || clips.Length == 0) return;
            next = Time.time + Random.Range(interval.x, interval.y);
            var cam = Camera.main; Vector3 c = cam != null ? cam.transform.position : transform.position;
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(distance.x, distance.y);
            src.transform.position = c + new Vector3(d.x, Random.Range(1f, 4f), d.y);
            src.pitch = Random.Range(pitch.x, pitch.y);
            src.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
            Played?.Invoke();
        }
    }
}

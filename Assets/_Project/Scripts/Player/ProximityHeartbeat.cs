using System.Collections.Generic;
using TheDelivery.UI;
using UnityEngine;

namespace TheDelivery.Player
{
    [DisallowMultipleComponent]
    public sealed class ProximityHeartbeat : MonoBehaviour
    {
        [SerializeField] private AudioClip[] heartbeats;
        [SerializeField, Min(0.1f)] private float audibleDistance = 12f;
        [SerializeField, Min(0f)] private float closestDistance = 2f;

        private static readonly List<Transform> threats = new List<Transform>();
        private AudioSource source;
        private float beatPhase = 1f;
        private int nextBeat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetThreats() => threats.Clear();

        // Scripted actors may switch off their AI while their bodies are still present.
        public static void Track(GameObject actor)
        {
            if (actor != null && !threats.Contains(actor.transform))
                threats.Add(actor.transform);
        }

        private void Awake()
        {
            var audioObject = new GameObject("ClearHeartbeat");
            audioObject.transform.SetParent(transform, false);
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            source.priority = 32;
        }

        private void OnDisable()
        {
            if (source != null) source.Stop();
            beatPhase = 1f;
        }

        private void Update()
        {
            if (heartbeats == null || heartbeats.Length == 0) return;
            if (GameOverManager.Instance != null && GameOverManager.Instance.IsActive)
            {
                source.Stop();
                beatPhase = 1f;
                return;
            }
            if (AudioListener.pause || Time.timeScale <= 0f) return;

            float nearestSquared = audibleDistance * audibleDistance;
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                var threat = threats[i];
                if (threat == null) { threats.RemoveAt(i); continue; }
                if (!threat.gameObject.activeInHierarchy || threat.gameObject.scene != gameObject.scene) continue;
                nearestSquared = Mathf.Min(nearestSquared, (threat.position - transform.position).sqrMagnitude);
            }

            float proximity = Mathf.InverseLerp(audibleDistance, closestDistance, Mathf.Sqrt(nearestSquared));
            if (proximity <= 0f)
            {
                beatPhase = 1f;
                return;
            }

            float bpm = Mathf.Lerp(68f, 120f, proximity);
            beatPhase += Time.deltaTime * bpm / 60f;
            if (beatPhase >= 1f)
            {
                beatPhase %= 1f;
                var clip = heartbeats[nextBeat];
                nextBeat = (nextBeat + 1) % heartbeats.Length;
                if (clip != null)
                    source.PlayOneShot(clip, 0.95f * Mathf.Sqrt(proximity));
            }
        }
    }
}

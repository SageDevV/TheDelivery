using System.Collections;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// SOM DA BRIGA NO BECO (Road, beco da D): troca de falas entre o agressor e a vítima, vozes do
    /// ElevenLabs em 3D saindo da cabeça de cada um (só se ouve de perto). Ciclo: o agressor grita →
    /// às vezes uma pancada surda (sintetizada) → a vítima implora → pausa → repete. Sorteia sem repetir
    /// a última fala de cada um. Montado pelo RoadAlleyFightSetup.
    /// </summary>
    public sealed class AlleyFightDialogue : MonoBehaviour
    {
        [SerializeField] private AudioSource thugVoice;
        [SerializeField] private AudioSource victimVoice;
        [SerializeField] private AudioSource impact;
        [SerializeField] private AudioClip[] thugLines;
        [SerializeField] private AudioClip[] victimLines;
        [SerializeField] private Vector2 pauseRange = new Vector2(1.4f, 3.2f);
        [Range(0f, 1f)] [SerializeField] private float hitChance = 0.45f;

        private static AudioClip thudClip;
        private int lastThug = -1, lastVictim = -1;

        private void OnEnable()
        {
            if (impact != null) impact.clip = thudClip != null ? thudClip : (thudClip = BuildThud());
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(Random.Range(0.3f, 1.2f));
            while (true)
            {
                if (thugLines.Length > 0)
                {
                    float len = Say(thugVoice, thugLines, ref lastThug);
                    yield return new WaitForSeconds(len * 0.85f);
                    if (impact != null && Random.value < hitChance)
                    {
                        impact.pitch = Random.Range(0.85f, 1.1f);
                        impact.PlayOneShot(impact.clip, Random.Range(0.7f, 1f));
                        yield return new WaitForSeconds(0.25f);
                    }
                    yield return new WaitForSeconds(Random.Range(0.25f, 0.6f));
                }
                if (victimLines.Length > 0)
                {
                    float len = Say(victimVoice, victimLines, ref lastVictim);
                    yield return new WaitForSeconds(len);
                }
                yield return new WaitForSeconds(Random.Range(pauseRange.x, pauseRange.y));
            }
        }

        private static float Say(AudioSource src, AudioClip[] lines, ref int last)
        {
            if (src == null) return 0f;
            int i = Random.Range(0, lines.Length);
            if (lines.Length > 1 && i == last) i = (i + 1 + Random.Range(0, lines.Length - 1)) % lines.Length;
            last = i;
            src.pitch = Random.Range(0.97f, 1.03f);
            src.clip = lines[i];
            src.Play();
            return lines[i].length / src.pitch;
        }

        /// <summary>Pancada surda num corpo: grave (~65 Hz caindo) com decaimento curto + estalo de
        /// ruído abafado no ataque (roupa/pele). 0,3 s.</summary>
        private static AudioClip BuildThud()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.3f);
            var data = new float[n];
            var rnd = new System.Random(77);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = Mathf.Lerp(95f, 55f, Mathf.Clamp01(t / 0.12f));
                phase += 2f * Mathf.PI * f / rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t / 0.07f);
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.18f;
                float slap = lp * Mathf.Exp(-t / 0.012f) * 1.6f;
                data[i] = Mathf.Clamp(body * 0.9f + slap, -1f, 1f);
            }
            var clip = AudioClip.Create("AlleyFightThud", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

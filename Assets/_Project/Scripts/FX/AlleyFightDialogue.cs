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

        [Tooltip("Pancada no corpo (ElevenLabs: AlleyFightThud.wav).")]
        [SerializeField] private AudioClip thudClip;
        private int lastThug = -1, lastVictim = -1;

        private void OnEnable()
        {
            if (impact != null && thudClip != null) impact.clip = thudClip;
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
                    if (impact != null && impact.clip != null && Random.value < hitChance)
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
    }
}

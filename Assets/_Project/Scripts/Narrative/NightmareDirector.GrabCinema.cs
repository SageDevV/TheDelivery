using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheDelivery.Narrative
{
    // Beat 5 - ACABAMENTO DE CINEMA dos planos de fora (o close do riso e o take do agarrão).
    // Parte do NightmareDirector (partial): os campos serializados ficam em NightmareDirector.cs.
    //
    // REFINO (2026-11-19, "nível profissional"):
    //   - LUZ DE CENA: uma luz principal fria, do lado da câmera, e um CONTORNO avermelhado por trás
    //     dos personagens — sem elas o take de fora era um borrão azul-escuro.
    //   - FOCO: o volume do sonho (NightmareBlurryVision) desfoca tudo além de 1,6 m — certo para os
    //     olhos dela, errado para uma câmera a 7 m do assunto. Um volume de prioridade alta põe o
    //     foco NO ASSUNTO e desfoca só o que está atrás dele.
    //   - CÂMERA NA MÃO: um tremor sutil e contínuo + SOLAVANCOS que decaem (a mão fechando, o
    //     arremesso).
    //   - CÂMERA LENTA no instante da soltura e um golpe de ar sintetizado.
    public sealed partial class NightmareDirector
    {
        private Light cinemaKey, cinemaRim;
        private Volume cinemaVolume;
        private VolumeProfile cinemaProfile;
        private DepthOfField cinemaDof;
        private float cinemaShake;          // solavanco atual (decai)
        private float cinemaSeed;
        private float cinemaKeyLevel, cinemaRimLevel; // iluminação desejada NO ASSUNTO (a intensidade sai de nível × distância²)
        private static AudioClip whooshClip;
        private bool slowMoActive;

        /// <summary>Liga luzes e foco de cinema para <paramref name="cam"/>. Os níveis são a luz que chega
        /// ao assunto (principal e contorno). Idempotente.</summary>
        private void CinemaBegin(Camera cam, float keyIntensity, float rimIntensity)
        {
            cinemaSeed = Random.value * 50f;
            if (cinemaKey == null)
            {
                cinemaKey = NewCinemaLight("CinemaKey", new Color(0.78f, 0.84f, 1f), 40f);
                cinemaRim = NewCinemaLight("CinemaRim", new Color(1f, 0.32f, 0.22f), 50f);
            }
            cinemaKeyLevel = keyIntensity;
            cinemaRimLevel = rimIntensity;
            cinemaKey.enabled = cinemaRim.enabled = true;

            if (cinemaVolume == null)
            {
                var go = new GameObject("CinemaFocusVolume");
                var dreamVolume = FindAnyObjectByType<Volume>();
                if (dreamVolume != null) go.layer = dreamVolume.gameObject.layer;
                cinemaVolume = go.AddComponent<Volume>();
                cinemaVolume.isGlobal = true;
                cinemaVolume.priority = 1000f;
                cinemaProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                cinemaDof = cinemaProfile.Add<DepthOfField>(true);
                cinemaDof.mode.Override(DepthOfFieldMode.Gaussian);
                cinemaDof.gaussianMaxRadius.Override(1.2f);
                cinemaVolume.sharedProfile = cinemaProfile;
            }
            cinemaVolume.enabled = true;
        }

        private Light NewCinemaLight(string name, Color color, float angle)
        {
            var go = new GameObject(name);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.spotAngle = angle;
            l.range = 32f;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        /// <summary>
        /// Um quadro do acabamento: posiciona as luzes em volta do <paramref name="subject"/> (principal
        /// do lado da câmera, acima; contorno atrás, do lado oposto), põe o foco na distância dele e
        /// devolve o tremor de câmera (posição e giro) a somar na pose da câmera.
        /// </summary>
        private void CinemaUpdate(Camera cam, Vector3 subject, out Vector3 shakePos, out Quaternion shakeRot)
        {
            shakePos = Vector3.zero; shakeRot = Quaternion.identity;
            if (cam == null) return;
            Vector3 toSubject = subject - cam.transform.position;
            float dist = toSubject.magnitude;
            Vector3 dir = dist > 1e-3f ? toSubject / dist : cam.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;

            if (cinemaKey != null)
            {
                Vector3 keyPos = cam.transform.position + right * 1.6f + Vector3.up * 1.2f;
                cinemaKey.transform.SetPositionAndRotation(keyPos, Quaternion.LookRotation(subject - keyPos));
                Vector3 rimPos = subject + dir * 2f - right * 1.8f + Vector3.up * 1.6f;
                cinemaRim.transform.SetPositionAndRotation(rimPos, Quaternion.LookRotation(subject - rimPos));
                // Luz pontual cai com o quadrado da distância: a intensidade compensa, e o assunto recebe
                // sempre a mesma luz, perto (close do riso) ou longe (take a 7 m).
                cinemaKey.intensity = cinemaKeyLevel * (keyPos - subject).sqrMagnitude;
                cinemaRim.intensity = cinemaRimLevel * (rimPos - subject).sqrMagnitude;
            }
            if (cinemaDof != null)
            {
                // Nítido do olho até um pouco depois do assunto; desfoca o que fica ATRÁS dele.
                cinemaDof.gaussianStart.Override(dist + 1.5f);
                cinemaDof.gaussianEnd.Override(dist + 9f);
            }

            float t = Time.unscaledTime + cinemaSeed;
            float hand = 0.012f + cinemaShake * 0.09f;
            shakePos = new Vector3(Mathf.PerlinNoise(t * 0.9f, 1.1f) - 0.5f,
                                   Mathf.PerlinNoise(2.3f, t * 0.8f) - 0.5f,
                                   Mathf.PerlinNoise(t * 0.7f, 5.7f) - 0.5f) * 2f * hand;
            float rot = 0.25f + cinemaShake * 2.5f;
            float fast = cinemaShake > 0.01f ? 18f : 1.2f;
            shakeRot = Quaternion.Euler((Mathf.PerlinNoise(t * fast, 9.1f) - 0.5f) * 2f * rot,
                                        (Mathf.PerlinNoise(3.3f, t * fast) - 0.5f) * 2f * rot,
                                        (Mathf.PerlinNoise(t * fast * 0.8f, 7.7f) - 0.5f) * rot);
            cinemaShake = Mathf.MoveTowards(cinemaShake, 0f, Time.unscaledDeltaTime * 1.8f);
        }

        // A PAUSA NO ALTO viva: respiração no tronco e tremor de força no braço que segura.
        private bool holdTremor;
        private float holdTremorWeight;
        private Transform holdSpine, holdArm, holdForearm;

        /// <summary>
        /// Chamado no LateUpdate ANTES do aperto (o tremor do braço move a palma, e o pescoço dela tem
        /// que ir junto). Entra e sai suave (0,4 s). Respiração lenta e funda no tronco (0,45 Hz, ~2°) e
        /// um tremor rápido e irregular no braço erguido (Perlin ~9 Hz, ~1,5°) — força segurando peso.
        /// </summary>
        private void UpdateHoldTremor()
        {
            holdTremorWeight = Mathf.MoveTowards(holdTremorWeight, holdTremor ? 1f : 0f, Time.deltaTime / 0.4f);
            if (holdTremorWeight <= 0f || creatureGrabObject == null)
                return;
            if (holdArm == null)
            {
                Transform[] bones = creatureGrabObject.GetComponentsInChildren<Transform>(true);
                holdSpine = FindBone(bones, "spine02") ?? FindBone(bones, "spine01") ?? FindBone(bones, "spine");
                holdArm = FindBone(bones, "rightarm");
                holdForearm = FindBone(bones, "rightforearm");
            }
            float t = Time.time;
            float w = holdTremorWeight;
            if (holdSpine != null)
                holdSpine.localRotation *= Quaternion.Euler(Mathf.Sin(t * 2f * Mathf.PI * 0.45f) * 2f * w, 0f, 0f);
            float n1 = (Mathf.PerlinNoise(t * 9f, 0.3f) - 0.5f) * 3f * w;
            float n2 = (Mathf.PerlinNoise(1.7f, t * 11f) - 0.5f) * 3f * w;
            if (holdArm != null) holdArm.localRotation *= Quaternion.Euler(n1, n2 * 0.5f, n2);
            if (holdForearm != null) holdForearm.localRotation *= Quaternion.Euler(n2, 0f, n1 * 0.6f);
        }

        /// <summary>Um SOLAVANCO de câmera (0–1), que decai sozinho em ~0,5 s.</summary>
        private void CinemaKick(float amount) => cinemaShake = Mathf.Max(cinemaShake, amount);

        private void CinemaEnd()
        {
            if (cinemaKey != null) cinemaKey.enabled = false;
            if (cinemaRim != null) cinemaRim.enabled = false;
            if (cinemaVolume != null) cinemaVolume.enabled = false;
            cinemaShake = 0f;
        }

        /// <summary>
        /// CÂMERA LENTA NA SOLTURA: o tempo cai para 30% em 0,08 s, segura 0,35 s (de tempo real) e
        /// volta ao normal em 0,3 s. O instante em que a mão abre ganha o peso que faltava. Medido em
        /// tempo NÃO escalado — o tempo escalado é justamente o que está sendo freado.
        /// </summary>
        private IEnumerator SlowMoRelease()
        {
            slowMoActive = true;
            float[] dur = { 0.08f, 0.35f, 0.3f };
            for (int phase = 0; phase < 3; phase++)
            {
                for (float t = 0f; t < dur[phase]; t += Time.unscaledDeltaTime)
                {
                    float k = t / dur[phase];
                    Time.timeScale = phase == 0 ? Mathf.Lerp(1f, 0.3f, k) : phase == 1 ? 0.3f : Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, k));
                    yield return null;
                }
            }
            Time.timeScale = 1f;
            slowMoActive = false;
        }

        /// <summary>Devolve o tempo normal (fim do beat, salto de debug) se a câmera lenta ficou no meio.</summary>
        private void EndSlowMo()
        {
            if (slowMoActive) Time.timeScale = 1f;
            slowMoActive = false;
        }

        /// <summary>
        /// O GOLPE DE AR do arremesso, sintetizado: um baque grave (55→35 Hz) e um SOPRO de ruído com
        /// filtro que varre de grave a agudo e some — o braço cortando o ar com ela. 0,9 s.
        /// </summary>
        private void PlayThrowWhoosh()
        {
            if (whooshClip == null)
            {
                const int rate = 44100;
                int n = (int)(rate * 0.9f);
                var data = new float[n];
                var rnd = new System.Random(911);
                float lp1 = 0f, lp2 = 0f, phase = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    float cut = Mathf.Lerp(0.02f, 0.35f, Mathf.Clamp01(t / 0.35f));
                    lp1 += (w - lp1) * cut;
                    lp2 += (lp1 - lp2) * cut;
                    float swoosh = (lp1 - lp2) * 3.2f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.7f));
                    phase += 2f * Mathf.PI * Mathf.Lerp(55f, 35f, Mathf.Clamp01(t / 0.4f)) / rate;
                    float thump = Mathf.Sin(phase) * Mathf.Exp(-t / 0.16f) * 0.9f;
                    data[i] = Mathf.Clamp(swoosh + thump, -1f, 1f);
                }
                whooshClip = AudioClip.Create("GrabThrowWhoosh", n, 1, rate, false);
                whooshClip.SetData(data, 0);
            }
            PlaySfx(whooshClip, 0.9f);
        }
    }
}

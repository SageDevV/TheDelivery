using TheDelivery.Characters;
using UnityEngine;

namespace TheDelivery.Narrative
{
    // A CLEAR SE DEBATENDO NA MÃO DA CRIATURA (v10, 2026-11-23). Quem posa o corpo é o ClearStruggle
    // (no modelo do take); aqui mora a DRAMATURGIA: a fase do clipe da criatura vira intensidade,
    // aperto das mãos e quanto o corpo é chicoteado — e os alvos das mãos dela no pulso dele.
    public sealed partial class NightmareDirector
    {
        private ClearStruggle grabStruggle;
        private Transform grabOtherHand; // a outra mão da criatura (a que entra no enforcamento)
        private bool grabOtherHandResolved;
        private Vector3 struggleOtherBlendPoint; private float struggleOtherBlend;

        /// <summary>Liga a luta no modelo do take recém-criado (BeginGrabTake).</summary>
        private void SetupGrabStruggle()
        {
            grabStruggle = null;
            if (!grabTakeStruggle || grabTakeActor == null) return;
            grabStruggle = grabTakeActor.AddComponent<ClearStruggle>();
            if (!grabStruggle.Bind())
            {
                Destroy(grabStruggle);
                grabStruggle = null;
                return;
            }
            struggleOtherBlend = 0f;
        }

        /// <summary>
        /// A FASE DO CLIPE VIRA ATUAÇÃO. Os tempos vêm dos mesmos marcos que a coroutine do agarrão usa
        /// (contato, ápice, enforcamento, sacudida, gargalhada, encarada, armar), então um clipe novo
        /// medido pelo rebuild arrasta a atuação junto.
        /// </summary>
        private void UpdateGrabStruggleBody()
        {
            if (grabStruggle == null || grabAnimator == null) return;
            AnimatorStateInfo st = grabAnimator.GetCurrentAnimatorStateInfo(0);
            float len = Mathf.Max(0.01f, st.length);
            float t = st.normalizedTime * len;
            float tc = grabContactNormalizedTime * len, th = grabHoldNormalizedTime * len, tk = grabChokeNormalizedTime * len;
            float ts = grabShakeNormalizedTime * len, tl = grabHeldLaughNormalizedTime * len, te = grabLaughEndNormalizedTime * len;
            float tw = grabWindupNormalizedTime * len;
            float s0 = ts - 0.15f, s1 = ts + 0.67f; // a sacudida no clipe: rampa 3,80-3,98 e 4,40-4,62 em volta do marco 3,95

            float I, G, P;
            if (t < tc + 0.45f) { I = 1f; G = Mathf.Clamp01((t - tc) / 0.3f); P = 0.35f; }   // o susto: as mãos voam para o pescoço
            else if (t < th) { I = 0.85f; G = 1f; P = 0.35f; }                                // subindo: chuta, puxa a mão dele
            else if (t < tk) { I = 0.7f; G = 1f; P = 0.3f; }                                  // no alto: luta, sem ar
            else if (t < s0) { I = 1f; G = 1f; P = 0.3f; }                                    // ENFORCADA: desespero
            // SEGURAR NUNCA FICA PELA METADE: entre "agarrada" e "solta" a IK põe as mãos no meio do
            // peito (parecia uma reza). Ela se agarra AINDA MAIS ao pulso dele quando é sacudida; a
            // fraqueza aparece nas pernas e no tronco, e as mãos só largam na soltura.
            else if (t < s1) { I = 0.25f; G = 1f; P = 1f; }                                   // sacudida: o corpo é chicoteado, as mãos não largam
            else if (t < tl) { I = 0.8f; G = 1f; P = 0.35f; }                                 // volta a lutar
            else if (t < te)                                                                  // a gargalhada: cada vez mais fraca
            {
                float k = Mathf.InverseLerp(tl, te, t);
                I = Mathf.Lerp(0.65f, 0.28f, k); G = 1f; P = 0.4f;
            }
            else if (t < tw) { I = 0.22f; G = 1f; P = 0.45f; }                                // a encarada: quase sem forças
            else { I = 0.12f; G = 1f; P = 0.75f; }                                            // armando: pende, as mãos ainda no pulso
            grabStruggle.Intensity = I;
            grabStruggle.Grip = G;
            grabStruggle.Passive = P;
            grabStruggle.PoseBody(Time.deltaTime);
        }

        /// <summary>As mãos dela no pulso dele — no enforcamento, a direita vai na outra mão dele.</summary>
        private void UpdateGrabStruggleArms()
        {
            if (grabStruggle == null || grabHand == null) return;
            Transform actor = grabTakeActor.transform;
            Vector3 wrist = grabHand.position;
            Vector3 palm = Palm();
            Vector3 toHer = palm - wrist;
            Vector3 along = toHer.sqrMagnitude > 1e-6f ? -toHer.normalized : -actor.forward; // do pulso para o cotovelo dele
            Vector3 side = Vector3.Cross(along, Vector3.up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : actor.right;
            if (Vector3.Dot(side, -actor.right) < 0f) side = -side; // "side" aponta para a ESQUERDA dela
            // POR CIMA do antebraço dele, um palmo à frente do pescoço: braços esticados para a frente,
            // puxando — e não as mãos juntas no queixo (lia como reza)
            // pontos no EIXO do antebraço dele; o ClearStruggle encosta o punho dela ao lado e fecha a mão por cima
            Vector3 left = wrist + along * 0.09f;
            Vector3 right = wrist + along * 0.17f;

            // a outra mão dele no pescoço dela (enforcamento): a mão dela daquele lado vai nela
            ResolveOtherHand();
            float want = 0f; Vector3 otherGrip = right;
            if (grabOtherHand != null && grabOtherHand.parent != null)
            {
                Vector3 ow = grabOtherHand.position;
                Vector3 oAlong = (grabOtherHand.parent.position - ow).normalized;
                otherGrip = ow + oAlong * 0.10f; // no eixo do antebraço (a pegada de bastão é montada no ClearStruggle)
                float d = Vector3.Distance(ow + (ow - grabOtherHand.parent.position).normalized * grabPalmReach, palm);
                want = Mathf.InverseLerp(0.55f, 0.3f, d);
            }
            struggleOtherBlend = Mathf.MoveTowards(struggleOtherBlend, want, Time.deltaTime / 0.35f);
            float b = Mathf.SmoothStep(0f, 1f, struggleOtherBlend);
            // DE QUE LADO DELA está a outra mão dele: pela ANATOMIA, não pela posição. Frente a frente, a mão
            // esquerda dele fica do lado direito dela. Comparar a posição lateral dos dois pulsos (como era)
            // virava de um quadro para o outro — os pulsos ficam quase alinhados no pescoço — e as mãos dela
            // trocavam de alvo quadro sim, quadro não: piscavam.
            bool otherOnHerRight = grabHand.name.ToLowerInvariant().Contains("right");
            if (otherOnHerRight) right = Vector3.Lerp(right, otherGrip, b);
            else left = Vector3.Lerp(left, otherGrip, b);
            grabStruggle.PoseArms(left, right, along);
        }

        private void ResolveOtherHand()
        {
            if (grabOtherHandResolved || creatureGrabObject == null || grabHand == null) return;
            grabOtherHandResolved = true;
            string want = grabHand.name.ToLowerInvariant().Contains("right") ? "lefthand" : "righthand";
            foreach (var t in creatureGrabObject.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                int c = n.LastIndexOf(':');
                if (c >= 0) n = n.Substring(c + 1);
                if (n == want) { grabOtherHand = t; break; }
            }
        }

        /// <summary>Um tranco no corpo dela (o aperto fechando, o susto da gargalhada).</summary>
        private void StruggleJolt(float strength)
        {
            if (grabStruggle != null) grabStruggle.Jolt(strength);
        }

        /// <summary>A mão abriu: ela solta o pulso dele e se debate no ar.</summary>
        private void StruggleFlight()
        {
            if (grabStruggle != null) grabStruggle.BeginFlight();
        }
    }
}

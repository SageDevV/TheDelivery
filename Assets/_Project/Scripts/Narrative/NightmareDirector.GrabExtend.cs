using System.Collections;
using UnityEngine;

namespace TheDelivery.Narrative
{
    // Beat 5 - O BRAÇO ESTENDIDO À FRENTE (2026-11-20): a criatura tira a Clear do chão e estica o braço
    // direito RETO À FRENTE do corpo, na altura do ombro, com ela suspensa pelo pescoço sobre o abismo —
    // e então abre a mão. Pose feita por código sobre a animação congelada (a animação do Meshy só tem o
    // braço subindo por cima da cabeça). Parte do NightmareDirector (partial).
    public sealed partial class NightmareDirector
    {
        private float armExtendWeight;
        private Transform extendUpper, extendFore;

        /// <summary>Tira do chão, estende o braço à frente e sustenta. Volta com ela ainda na mão.</summary>
        private IEnumerator ExtendArmAndHold()
        {
            // a animação a tira do chão por um instante (o tranco do agarrão)
            yield return new WaitForSeconds(0.35f);
            if (grabAnimator != null) grabAnimator.speed = 0f; // o corpo congela; o braço é nosso daqui em diante
            // o braço sobe para a frente em 1,1 s, levando-a junto
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                armExtendWeight = Mathf.SmoothStep(0f, 1f, t / 1.1f);
                yield return null;
            }
            armExtendWeight = 1f;
            // suspensa na frente dela, sobre o abismo: força no braço, respiração no tronco
            holdTremor = true;
            yield return new WaitForSeconds(Mathf.Max(0.5f, grabHoldDuration));
            holdTremor = false;
        }

        /// <summary>
        /// LateUpdate, antes do tremor e do aperto: aponta braço e antebraço direitos para um ponto à
        /// frente da criatura, na altura do ombro (8° acima), com o braço reto. Por cima da pose congelada.
        /// </summary>
        private void UpdateArmExtend()
        {
            if (armExtendWeight <= 0f || creatureGrabObject == null || grabHand == null) return;
            if (extendUpper == null)
            {
                Transform[] bones = creatureGrabObject.GetComponentsInChildren<Transform>(true);
                extendUpper = FindBone(bones, "rightarm");
                extendFore = FindBone(bones, "rightforearm");
                if (extendUpper == null || extendFore == null) { armExtendWeight = 0f; return; }
            }
            Vector3 fwd = creatureGrabObject.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            // à frente, um pouco para dentro (a mão do braço direito vem para a linha do peito) e 8° acima
            Vector3 dir = (Quaternion.AngleAxis(-8f, right) * (fwd * 1f - right * 0.12f)).normalized;

            Vector3 shoulder = extendUpper.position;
            Vector3 upperDir = (extendFore.position - shoulder).normalized;
            extendUpper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(upperDir, dir), armExtendWeight) * extendUpper.rotation;
            Vector3 elbow = extendFore.position;
            Vector3 foreDir = (grabHand.position - elbow).normalized;
            extendFore.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(foreDir, dir), armExtendWeight) * extendFore.rotation;
        }

        private void EndArmExtend()
        {
            armExtendWeight = 0f;
        }
    }
}

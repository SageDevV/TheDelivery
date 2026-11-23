using UnityEngine;

namespace TheDelivery.Narrative
{
    // DECUPAGEM DO AGARRÃO (v16, 2026-11-23 — "vai variando os ângulos da câmera em foco da animação que está
    // ocorrendo"). A animação não muda; a câmera do take corta para o plano que enquadra o que está
    // acontecendo, na batida da ação:
    //
    //   bote e subida      → PLANO GERAL de perfil (o take original, FrameGrabTake)
    //   enforcamento       → PLANO A DOIS: a cara dele, o braço e a outra mão chegando no pescoço dela
    //   sacudida           → CONTRA-PLONGÉE ABERTO de frente-lado: a violência inteira em quadro
    //   gargalhada         → CONTRA-PLONGÉE em 3/4 na cara dele (a cabeça jogada para trás, os dentes)
    //   encarada           → POR CIMA DO OMBRO dele: o rosto dela sufocando, visto por ele
    //   arremesso e voo    → volta ao PLANO GERAL de perfil (o ângulo já ajustado para o giro) e segue a Clear
    //
    // Os planos foram pré-visualizados no Blender com o clipe (scratchpad motion2/shotpreview.py) — câmera
    // fora dos corpos, rosto de quem importa sem nada na frente. Cada plano: posição calculada NO CORTE
    // (congelada — a câmera não nada junto com cada tremor de osso) + um avanço lento até o fim do plano;
    // o OLHAR segue o assunto vivo, suavizado; luz de recorte e foco (CinemaUpdate) no assunto do plano.
    public sealed partial class NightmareDirector
    {
        private enum GrabShot { Master = 0, Choke = 1, Shake = 2, Laugh = 3, Stare = 4, Throw = 5 }

        private GrabShot grabShot = GrabShot.Master;
        private bool grabShotActive;
        private Vector3 grabShotPos0, grabShotLook;
        private Transform grabHeadEnd;
        private bool grabHeadEndResolved;

        /// <summary>
        /// Se o plano atual é de cobertura (não o geral), devolve a pose da câmera, o FOV e o assunto (foco e
        /// luz). Falso = o take segue no plano geral (bote, subida, arremesso, voo).
        /// </summary>
        private bool UpdateGrabShot(out Vector3 pos, out Quaternion rot, out float fov, out Vector3 subject)
        {
            pos = default; rot = Quaternion.identity; fov = grabTakeFov; subject = default;
            if (!grabTakeShots || grabTakeThrown || grabAnimator == null || grabTakeActorNeck == null || creatureGrabObject == null)
            {
                grabShotActive = false;
                return false;
            }

            float t = ClipTimeSeconds(out float len);
            float th = grabHoldNormalizedTime * len;
            float ts = grabShakeNormalizedTime * len - 0.15f;   // a sacudida começa a crescer 0,15 s antes do marco
            float tl = grabHeldLaughNormalizedTime * len;
            float te = grabLaughEndNormalizedTime * len;
            float tw = grabWindupNormalizedTime * len - 0.35f;  // a armação começa antes do marco do rugido

            GrabShot shot; float s0 = 0f, s1 = 1f;
            if (t < th) shot = GrabShot.Master;
            else if (t < ts) { shot = GrabShot.Choke; s0 = th; s1 = ts; }
            else if (t < tl) { shot = GrabShot.Shake; s0 = ts; s1 = tl; }
            else if (t < te) { shot = GrabShot.Laugh; s0 = tl; s1 = te; }
            else if (t < tw) { shot = GrabShot.Stare; s0 = te; s1 = tw; }
            else shot = GrabShot.Throw;

            bool cut = shot != grabShot;
            grabShot = shot;
            if (shot == GrabShot.Master || shot == GrabShot.Throw)
            {
                // de volta ao geral: o olhar do take pula para o lugar certo (corte, não panorâmica)
                if (cut && grabShotActive) grabTakeLookPrimed = false;
                grabShotActive = false;
                return false;
            }

            Vector3 U = Vector3.up;
            Vector3 F = creatureGrabObject.transform.forward; F.y = 0f;
            F = F.sqrMagnitude > 1e-4f ? F.normalized : Vector3.forward;
            Vector3 R = Vector3.Cross(U, F); // a direita da criatura
            Vector3 root = creatureGrabObject.transform.position;
            Vector3 herNeck = grabTakeActorNeck.position;
            Vector3 herHead = grabTakeActorHead != null ? grabTakeActorHead.position : herNeck + U * 0.16f;
            Vector3 hisHead = HisHeadPoint();

            Vector3 camAt, look; float push;
            switch (shot)
            {
                case GrabShot.Choke:
                    camAt = herNeck + R * 1.75f - F * 0.6f;
                    look = Vector3.Lerp(herHead, hisHead, 0.4f) - U * 0.1f;
                    subject = herHead; fov = 42f; push = 0.10f;
                    break;
                case GrabShot.Shake:
                    camAt = root + F * 2.9f + R * 2.0f + U * 1.5f;
                    look = Vector3.Lerp(hisHead, herNeck, 0.5f) - U * 0.35f;
                    subject = Vector3.Lerp(hisHead, herNeck, 0.5f); fov = 44f; push = 0.06f;
                    break;
                case GrabShot.Laugh:
                    camAt = hisHead + F * 1.0f + R * 1.9f - U * 1.1f;
                    look = hisHead + U * 0.1f;
                    subject = hisHead; fov = 46f; push = 0.08f;
                    break;
                default: // Stare
                    camAt = hisHead - F * 1.1f - R * 0.8f + U * 0.45f;
                    look = herHead;
                    subject = herHead; fov = 42f; push = 0.05f;
                    break;
            }

            if (cut || !grabShotActive)
            {
                grabShotPos0 = camAt;   // a posição é do instante do corte
                grabShotLook = look;    // e o olhar entra já no lugar
            }
            else
            {
                grabShotLook = Vector3.Lerp(grabShotLook, look, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
            grabShotActive = true;

            float u = Mathf.Clamp01(Mathf.InverseLerp(s0, s1, t));
            pos = Vector3.Lerp(grabShotPos0, grabShotLook, push * Mathf.SmoothStep(0f, 1f, u)); // avanço lento
            Vector3 dir = grabShotLook - pos;
            rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir, U) : grabTakeCamera.transform.rotation;
            return true;
        }

        /// <summary>A cara da criatura: um terço do caminho do osso da cabeça até a ponta (o osso fica na base do crânio).</summary>
        private Vector3 HisHeadPoint()
        {
            if (grabHead == null) return creatureGrabObject.transform.position + Vector3.up * 3.4f;
            if (!grabHeadEndResolved)
            {
                grabHeadEndResolved = true;
                foreach (var tr in grabHead.GetComponentsInChildren<Transform>(true))
                    if (tr.name.ToLowerInvariant().EndsWith("head_end")) { grabHeadEnd = tr; break; }
            }
            return grabHeadEnd != null ? Vector3.Lerp(grabHead.position, grabHeadEnd.position, 0.35f) : grabHead.position + Vector3.up * 0.25f;
        }
    }
}

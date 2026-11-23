using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TheDelivery.Narrative
{
    // Beat 5 - o TAKE EM TERCEIRA PESSOA do agarrão: da mão fechar até a Clear sumir no abismo.
    // Parte do NightmareDirector (partial): os campos serializados ficam em NightmareDirector.cs.
    //
    // REFEITO DO ZERO (2026-11-19) junto com o agarrão: a Clear do take fica presa à PALMA da
    // criatura pelo pescoço (UpdateGrip), é arremessada com a velocidade do golpe (ThrownFall) e a
    // câmera fica SOBRE O ABISMO, olhando de volta para o corredor — enquadra a criatura inteira
    // com ela no alto e depois a acompanha passando pela borda.
    public sealed partial class NightmareDirector
    {
        private Camera grabTakeCamera;
        private Camera grabTakeHiddenCamera;
        private GameObject grabTakeActor;
        private Transform grabTakeActorHead;
        private Transform grabTakeActorNeck;
        private bool grabTakeThrown;
        private Vector3 grabTakeFrom, grabTakeTo, grabTakeLook;
        private float grabTakeStart;
        private Vector3 grabTakeLookNow;
        private bool grabTakeLookPrimed;
        // A NÉVOA DO SONHO (DreamFog) segue o jogador — que no take está pendurado com a Clear — e
        // vira bolhas brancas entre a câmera de fora e a cena. Escondida só durante o take.
        private readonly System.Collections.Generic.List<ParticleSystemRenderer> grabTakeHiddenFog = new System.Collections.Generic.List<ParticleSystemRenderer>();

        /// <summary>
        /// CORTA PARA FORA no quadro do contato. Uma câmera nova copia a do sonho (pós-processo,
        /// volume) e a do player é apagada. A Clear do take é o modelo inteiro (com cabeça),
        /// instanciado onde ela está; o primeiro UpdateGrip já a pendura na palma.
        /// </summary>
        private void BeginGrabTake()
        {
            if (!grabThirdPerson || grabTakeModel == null || grabTakeCamera != null || playerController == null)
                return;

            Camera dream = ResolveDreamCamera();
            Transform body = playerController.transform;

            grabTakeActor = Instantiate(grabTakeModel, body.position, body.rotation);
            grabTakeActor.name = "ClearGrabTake";
            foreach (var c in grabTakeActor.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            grabTakeActorHead = FindHeadBone(grabTakeActor.transform);
            grabTakeActorNeck = grabTakeActorHead != null ? grabTakeActorHead.parent : null;
            grabTakeThrown = false;
            grabTakeLookPrimed = false;
            grabShot = GrabShot.Master; grabShotActive = false; // v16: a decupagem começa no plano geral
            ResetHang();
            SetupGrabStruggle(); // v10: ela luta na mão dele (GrabStruggle.cs)

            var go = new GameObject("GrabTakeCamera");
            grabTakeCamera = go.AddComponent<Camera>();
            if (dream != null)
            {
                grabTakeCamera.CopyFrom(dream);
                var src = dream.GetUniversalAdditionalCameraData();
                var dst = grabTakeCamera.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = go.transform;
                grabTakeCamera.depth = dream.depth + 5f;
            }
            grabTakeCamera.fieldOfView = grabTakeFov;
            grabTakeCamera.nearClipPlane = 0.05f;
            grabTakeCamera.farClipPlane = Mathf.Max(grabTakeCamera.farClipPlane, 200f);

            grabTakeHiddenFog.Clear();
            foreach (var fog in FindObjectsByType<TheDelivery.FX.DreamFog>(FindObjectsInactive.Exclude))
                foreach (var r in fog.GetComponentsInChildren<ParticleSystemRenderer>())
                    if (r.enabled) { r.enabled = false; grabTakeHiddenFog.Add(r); }

            FrameGrabTake();
            CinemaBegin(grabTakeCamera, 1.6f, 3f);
            CinemaKick(0.6f); // a mão fechando no pescoço dela
            grabTakeCamera.transform.SetPositionAndRotation(grabTakeFrom, Quaternion.LookRotation(grabTakeLook - grabTakeFrom, Vector3.up));

            if (dream != null)
            {
                dream.enabled = false;
                grabTakeHiddenCamera = dream;
            }
        }

        /// <summary>
        /// O ENQUADRAMENTO, fixado no contato. A criatura olha para a Clear e para o abismo atrás
        /// dela; a câmera fica do outro lado da Clear, SOBRE O ABISMO (sem parede no caminho),
        /// um pouco de lado e baixa — a criatura de 4 m e a Clear erguida a mais de 4 m cabem
        /// inteiras, vistas de baixo. Se algo bloquear (um cenário diferente), testa o outro lado
        /// e ângulos menores.
        /// </summary>
        private void FrameGrabTake()
        {
            Transform body = playerController.transform;
            Transform creature = creatureGrabObject != null ? creatureGrabObject.transform : body;
            Vector3 forward = creature.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = -body.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float floorY = body.position.y;

            // Meio do que precisa caber: entre a criatura e a Clear, a meia altura da subida.
            Vector3 clear = body.position;
            Vector3 mid = Vector3.Lerp(new Vector3(creature.position.x, floorY, creature.position.z), new Vector3(clear.x, floorY, clear.z), 0.6f);
            grabTakeLook = mid + Vector3.up * grabTakeLookHeight;

            Vector3 best = Vector3.zero; float bestFree = -1f;
            foreach (float side in new[] { 1f, -1f })
            // DE PERFIL (2026-11-20): com o braço estendido à frente, de frente ele apontava para a lente e
            // sumia em perspectiva; de lado ele aparece reto, com a Clear pendurada na ponta.
            foreach (float yaw in new[] { 80f, 70f, 55f })
            {
                Vector3 dir = Quaternion.AngleAxis(side * yaw, Vector3.up) * forward;
                Vector3 target = new Vector3(mid.x, floorY + grabTakeHeight, mid.z) + dir * grabTakeDistance;
                // no mundo vermelho o cenário está invisível: paredes não atrapalham a vista
                float free = redWorldActive ? grabTakeDistance : FreeDistance(grabTakeLook, target);
                if (free > bestFree) { bestFree = free; best = dir; }
                if (free >= grabTakeDistance * 0.95f) goto Found;
            }
            Found:
            // de perfil, mais perto (a 7 m a ação ficava pequena na tela)
            float dist = Mathf.Max(2f, Mathf.Min(grabTakeDistance * 0.68f, bestFree));
            grabTakeFrom = new Vector3(mid.x, floorY + grabTakeHeight, mid.z) + best * dist;
            grabTakeTo = grabTakeFrom - best * Mathf.Min(grabTakePush, dist * 0.15f);
            grabTakeStart = Time.time;
        }

        /// <summary>
        /// Um quadro da câmera do take (LateUpdate, depois do aperto). Durante o aperto ela avança
        /// devagar mirando o meio da cena; no arremesso o olhar desce atrás da Clear, que passa
        /// pela borda e some no escuro.
        /// </summary>
        private void UpdateGrabTake()
        {
            if (grabTakeCamera == null) return;
            // v16: a DECUPAGEM — no enforcamento, na sacudida, na gargalhada e na encarada a câmera corta para o
            // plano daquela ação (GrabShots.cs). Fora deles segue o plano geral abaixo.
            if (UpdateGrabShot(out Vector3 shotPos, out Quaternion shotRot, out float shotFov, out Vector3 shotSubject))
            {
                CinemaUpdate(grabTakeCamera, shotSubject, out Vector3 sp, out Quaternion sr);
                grabTakeCamera.fieldOfView = shotFov;
                grabTakeCamera.transform.SetPositionAndRotation(shotPos + sp, shotRot * sr);
                return;
            }
            grabTakeCamera.fieldOfView = grabTakeFov;
            // A aproximação dura a subida inteira MAIS a pausa dramática: a câmera chega perto enquanto
            // a criatura a segura no alto.
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - grabTakeStart) / (2f + Mathf.Max(0f, grabHoldDuration))));
            Vector3 pos = Vector3.Lerp(grabTakeFrom, grabTakeTo, k);
            // A CÂMERA ACOMPANHA A CLEAR (refino): o olhar puxa 40% na direção dela enquanto ela sobe e
            // fica no alto, e 75% quando ela voa — sem perder a criatura do quadro.
            Vector3 her = grabTakeActorNeck != null ? grabTakeActorNeck.position : grabTakeLook;
            Vector3 wantLook = Vector3.Lerp(grabTakeLook, her, grabTakeThrown ? 0.75f : 0.4f);
            grabTakeLookNow = grabTakeLookPrimed ? Vector3.Lerp(grabTakeLookNow, wantLook, 1f - Mathf.Exp(-(grabTakeThrown ? 8f : 3f) * Time.deltaTime)) : wantLook;
            grabTakeLookPrimed = true;
            Vector3 subject = Vector3.Lerp(grabHead != null ? grabHead.position : grabTakeLook, her, 0.5f);
            CinemaUpdate(grabTakeCamera, subject, out Vector3 shakePos, out Quaternion shakeRot);
            Quaternion want = Quaternion.LookRotation(grabTakeLookNow - pos, Vector3.up);
            grabTakeCamera.transform.SetPositionAndRotation(pos + shakePos, want * shakeRot);
        }

        /// <summary>VOLTA PARA OS OLHOS DELA (e desmonta o take em qualquer saída do beat).</summary>
        private void EndGrabTake()
        {
            CinemaEnd();
            if (grabTakeHiddenCamera != null)
            {
                grabTakeHiddenCamera.enabled = true;
                grabTakeHiddenCamera = null;
            }
            foreach (var r in grabTakeHiddenFog) if (r != null) r.enabled = true;
            grabTakeHiddenFog.Clear();
            if (grabTakeCamera != null) Destroy(grabTakeCamera.gameObject);
            if (grabTakeActor != null) Destroy(grabTakeActor);
            grabTakeCamera = null;
            grabTakeActor = null;
            grabStruggle = null;
            CleanupGrabBlood();
            grabTakeActorHead = null;
            grabTakeActorNeck = null;
            grabTakeThrown = false;
        }

        /// <summary>Quanto dá para afastar a câmera de <paramref name="from"/> até <paramref name="to"/>
        /// sem atravessar geometria (ignora a criatura, a Clear e gatilhos), menos 0,35 m de folga.</summary>
        private float FreeDistance(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return 0f;
            float free = len;
            foreach (var h in Physics.RaycastAll(from, d / len, len, ~0, QueryTriggerInteraction.Ignore))
            {
                Transform t = h.collider.transform;
                if (creatureGrabObject != null && t.IsChildOf(creatureGrabObject.transform)) continue;
                if (playerController != null && t.IsChildOf(playerController.transform)) continue;
                free = Mathf.Min(free, h.distance - 0.35f);
            }
            return Mathf.Max(0f, free);
        }

        private static Transform FindHeadBone(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n == "head" || n.EndsWith(":head") || n.EndsWith("_head")) return t;
            }
            return null;
        }
    }
}

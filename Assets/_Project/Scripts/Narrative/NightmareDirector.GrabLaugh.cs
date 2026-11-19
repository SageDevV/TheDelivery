using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TheDelivery.Narrative
{
    // Beat 5 - O RISO NA CARA DELA: entre o giro (ela encara a criatura) e o bote.
    // Parte do NightmareDirector (partial): os campos serializados ficam em NightmareDirector.cs.
    //
    // TRÊS TEMPOS (pedido 2026-11-19):
    //   1. A CRIATURA OLHA PARA BAIXO, para o rosto dela — ainda nos olhos da Clear, que a vê
    //      de baixo abaixar a cabeça até encará-la.
    //   2. O ZOOM VEM DE BAIXO: a câmera sai dos olhos dela e SOBE pela linha do olhar das duas
    //      até a cara da criatura. Como a criatura está olhando para ela, o close chega de frente.
    //   3. O RISO: close na cara, tela piscando em vermelho, risada diabólica, cabeça sacudindo.
    //   Depois corta de volta para os olhos dela; a criatura ergue a cabeça e o bote começa.
    public sealed partial class NightmareDirector
    {
        private const float LaughLookDownTime = 0.9f;
        private const float LaughZoomTime = 0.85f;

        private Canvas redFlashCanvas;
        private Image redFlashImage;
        private bool laughShaking;
        private float laughStart;
        private float laughLookWeight;   // 0..1: quanto a criatura está olhando para baixo, para ela
        private float laughZoom;         // 0..1: posição da câmera entre os olhos dela e o close
        private Transform laughFace, laughFaceTop;
        private Camera laughCloseCamera;
        private Camera laughHiddenCamera;
        private Vector3 laughCamFrom;
        private Quaternion laughCamFromRot;
        private float laughCloseFovFrom = 60f;
        private readonly List<ParticleSystemRenderer> laughHiddenFog = new List<ParticleSystemRenderer>();

        private IEnumerator LaughInHerFace()
        {
            if (!grabLaughFocus || creatureGrabObject == null || grabHead == null)
                yield break;

            Transform[] bones = creatureGrabObject.GetComponentsInChildren<Transform>(true);
            laughFace = FindBone(bones, "headfront") ?? grabHead;
            laughFaceTop = FindBone(bones, "head_end");
            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;

            // --- 1. A criatura abaixa a cabeça e olha para ela (nos olhos da Clear) ---
            Quaternion eyes = holder != null ? holder.rotation : Quaternion.identity;
            for (float t = 0f; t < LaughLookDownTime; t += Time.deltaTime)
            {
                laughLookWeight = Mathf.SmoothStep(0f, 1f, t / LaughLookDownTime);
                if (holder != null)
                {
                    Vector3 dir = FaceCenter() - holder.position;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        eyes = Quaternion.Slerp(eyes, Quaternion.LookRotation(dir.normalized, Vector3.up), 1f - Mathf.Exp(-8f * Time.deltaTime));
                        ApplyEyeRotation(body, holder, eyes);
                        gripLastLook = eyes;
                    }
                }
                yield return null;
            }
            laughLookWeight = 1f;

            // --- 2. O zoom de baixo: a câmera sobe dos olhos dela até a cara; o riso começa ---
            BeginLaughClose();
            float laughLength = grabLaughDuration > 0f ? grabLaughDuration
                : grabLaughSound != null ? grabLaughSound.length : 3f;
            PlaySfx(grabLaughSound, grabLaughVolume);
            laughShaking = true;
            laughStart = Time.time;
            EnsureRedFlashOverlay();
            float total = Mathf.Max(LaughZoomTime + 1.2f, laughLength);
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                laughZoom = Mathf.SmoothStep(0f, 1f, t / LaughZoomTime);
                SetRedFlash(RedFlashAmount(t));
                yield return null;
            }

            // --- 3. Corta de volta para os olhos dela ---
            // A CRIATURA NÃO VOLTA A OLHAR PARA A FRENTE (pedido 2026-11-19): o olhar fica preso
            // nela pelo resto do agarrão — o bote, a subida, a pausa no alto, o arremesso e a queda.
            // Quem desliga é o EndLaugh, no fim do beat.
            EndLaughClose();
            laughLookWeight = 1f;
        }

        /// <summary>O CENTRO DA CARA: entre o "headfront" (fica no queixo) e o "head_end" (topo).</summary>
        private Vector3 FaceCenter()
        {
            if (laughFace == null) return grabHead != null ? grabHead.position : Vector3.zero;
            return laughFaceTop != null ? Vector3.Lerp(laughFace.position, laughFaceTop.position, 0.5f) : laughFace.position;
        }

        /// <summary>
        /// UM QUADRO DO RISO, no LateUpdate (depois do Animator, que reescreve a pose congelada todo
        /// quadro): vira o pescoço e a cabeça da criatura para o rosto da Clear, sacode a cabeça
        /// enquanto ela ri e posiciona a câmera do zoom/close.
        /// </summary>
        private void UpdateLaughHead()
        {
            if (grabHead == null || playerController == null)
                return;
            Transform holder = playerController.CameraHolder;
            Vector3 eyesOfHer = holder != null ? holder.position : playerController.transform.position + Vector3.up * 1.6f;
            // Para onde a criatura olha: o rosto dela. No take em 3ª pessoa, a cabeça do modelo
            // inteiro da Clear (pendurada na mão, depois voando); fora dele, os olhos do jogador.
            Vector3 herFace = grabTakeActorHead != null ? grabTakeActorHead.position : eyesOfHer;

            // OLHAR PARA ELA: metade do giro no pescoço, o resto na cabeça — só na cabeça, um giro
            // desse tamanho quebra o pescoço do modelo. Com teto de LaughLookMaxAngle sobre a pose da
            // animação: com ela atrás do ombro (no recuo do arremesso) o giro inteiro torceria a cabeça
            // para trás.
            if (laughLookWeight > 0f && laughFace != null)
            {
                Transform neck = grabHead.parent;
                if (neck != null)
                    neck.rotation = Quaternion.Slerp(Quaternion.identity, LookDelta(herFace), 0.5f * laughLookWeight) * neck.rotation;
                grabHead.rotation = Quaternion.Slerp(Quaternion.identity, LookDelta(herFace), laughLookWeight) * grabHead.rotation;
            }

            if (laughShaking)
            {
                float t = Time.time - laughStart;
                float a = grabLaughHeadShake;
                grabHead.localRotation *= Quaternion.Euler(
                    -a * 0.6f + Mathf.Sin(t * 23f) * a * 0.5f,
                    Mathf.Sin(t * 9f) * a * 0.4f,
                    Mathf.Sin(t * 13f + 1f) * a * 0.3f);
            }

            // A CÂMERA: dos olhos dela (pose de partida) até o close, que fica na LINHA entre a cara e
            // os olhos dela, a grabLaughCloseDistance da cara — de frente, porque a criatura olha para lá.
            if (laughCloseCamera != null)
            {
                Vector3 center = FaceCenter();
                Vector3 toHer = herFace - center;
                if (toHer.sqrMagnitude < 0.01f) toHer = creatureGrabObject.transform.forward;
                Vector3 end = center + toHer.normalized * grabLaughCloseDistance;
                Vector3 pos = Vector3.Lerp(laughCamFrom, end, laughZoom);
                Quaternion lookFace = Quaternion.LookRotation(center - pos, Vector3.up);
                Quaternion rot = Quaternion.Slerp(laughCamFromRot, lookFace, Mathf.Clamp01(laughZoom * 3f));
                laughCloseCamera.transform.SetPositionAndRotation(pos, rot);
                laughCloseCamera.fieldOfView = Mathf.Lerp(laughCloseFovFrom, grabLaughFov, laughZoom);
            }
        }

        private const float LaughLookMaxAngle = 80f;

        /// <summary>O giro que leva a cara da criatura a apontar para <paramref name="target"/>, limitado a
        /// <see cref="LaughLookMaxAngle"/> graus.</summary>
        private Quaternion LookDelta(Vector3 target)
        {
            Quaternion d = Quaternion.FromToRotation(FaceDirection(), target - grabHead.position);
            float angle = Quaternion.Angle(Quaternion.identity, d);
            return angle > LaughLookMaxAngle ? Quaternion.Slerp(Quaternion.identity, d, LaughLookMaxAngle / angle) : d;
        }

        /// <summary>Para onde a cara da criatura aponta agora: da cabeça para a frente do rosto.</summary>
        private Vector3 FaceDirection()
        {
            Vector3 d = laughFace != null && laughFace != grabHead ? laughFace.position - grabHead.position : grabHead.forward;
            return d.sqrMagnitude < 1e-6f ? grabHead.forward : d;
        }

        /// <summary>A câmera do zoom nasce EXATAMENTE na pose da câmera dos olhos dela (cópia, com o
        /// mesmo pós-processo) — o zoom começa sem corte — e a do player é apagada. A névoa que segue
        /// o player sai do caminho.</summary>
        private void BeginLaughClose()
        {
            Camera dream = ResolveDreamCamera();
            var go = new GameObject("LaughCloseCamera");
            laughCloseCamera = go.AddComponent<Camera>();
            if (dream != null)
            {
                laughCloseCamera.CopyFrom(dream);
                var src = dream.GetUniversalAdditionalCameraData();
                var dst = laughCloseCamera.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = go.transform;
                laughCloseCamera.depth = dream.depth + 6f;
                laughCamFrom = dream.transform.position;
                laughCamFromRot = dream.transform.rotation;
                laughCloseFovFrom = dream.fieldOfView;
                dream.enabled = false;
                laughHiddenCamera = dream;
            }
            laughCloseCamera.nearClipPlane = 0.03f;
            go.transform.SetPositionAndRotation(laughCamFrom, laughCamFromRot);
            laughZoom = 0f;

            laughHiddenFog.Clear();
            foreach (var fog in FindObjectsByType<TheDelivery.FX.DreamFog>(FindObjectsInactive.Exclude))
                foreach (var r in fog.GetComponentsInChildren<ParticleSystemRenderer>())
                    if (r.enabled) { r.enabled = false; laughHiddenFog.Add(r); }
        }

        private void EndLaughClose()
        {
            laughShaking = false;
            SetRedFlash(0f);
            if (laughHiddenCamera != null)
            {
                laughHiddenCamera.enabled = true;
                laughHiddenCamera = null;
            }
            if (laughCloseCamera != null)
                Destroy(laughCloseCamera.gameObject);
            laughCloseCamera = null;
            laughZoom = 0f;
            foreach (var r in laughHiddenFog) if (r != null) r.enabled = true;
            laughHiddenFog.Clear();
        }

        /// <summary>
        /// A PISCADA VERMELHA: onda quadrada em <see cref="grabRedFlashRate"/> Hz (aceso/apagado,
        /// sem fade — é estroboscópio, não pulsação), com a intensidade de cada piscada sorteada
        /// por ruído para não ficar mecânica, e um fundo vermelho fraco que nunca apaga de todo.
        /// </summary>
        private float RedFlashAmount(float t)
        {
            float phase = t * Mathf.Max(0.1f, grabRedFlashRate);
            bool on = phase - Mathf.Floor(phase) < 0.5f;
            float strength = Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(Mathf.Floor(phase) * 0.73f, 3.1f));
            return on ? strength : 0.18f;
        }

        private void SetRedFlash(float amount)
        {
            if (amount <= 0f)
            {
                if (redFlashCanvas != null && redFlashCanvas.gameObject.activeSelf)
                    redFlashCanvas.gameObject.SetActive(false);
                return;
            }
            EnsureRedFlashOverlay();
            if (!redFlashCanvas.gameObject.activeSelf)
                redFlashCanvas.gameObject.SetActive(true);
            Color c = grabRedFlashColor;
            c.a = grabRedFlashColor.a * Mathf.Clamp01(amount);
            redFlashImage.color = c;
        }

        /// <summary>Canvas de tela cheia vermelho, montado em código como o das pálpebras (abaixo delas).</summary>
        private void EnsureRedFlashOverlay()
        {
            if (redFlashCanvas != null)
                return;
            var root = new GameObject("PesadeloRedFlash");
            root.transform.SetParent(transform, false);
            redFlashCanvas = root.AddComponent<Canvas>();
            redFlashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            redFlashCanvas.sortingOrder = 490;
            var img = new GameObject("Red");
            img.transform.SetParent(root.transform, false);
            var rt = img.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            redFlashImage = img.AddComponent<Image>();
            redFlashImage.raycastTarget = false;
            root.SetActive(false);
        }

        /// <summary>Desmonta o riso inteiro (salto de debug / fim do beat).</summary>
        private void EndLaugh()
        {
            EndLaughClose();
            laughLookWeight = 0f;
        }
    }
}

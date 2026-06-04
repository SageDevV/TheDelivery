using System.Collections;
using UnityEngine;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats da sequência do Ato 4 (clímax). A ordem do enum é a ordem
    /// cronológica da experiência. Os beats 3-9 ainda não estão implementados —
    /// existem aqui para travar o contrato do enum e permitir pular para eles
    /// via <c>startBeat</c> conforme forem ganhando corpo nas próximas semanas.
    /// </summary>
    public enum Act4Beat
    {
        None,
        Awakening,      // Beat 1: acorda com som
        Investigation,  // Beat 2: investiga o apartamento
        Discovery,      // Beat 3: descobre que ele está dentro (futuro)
        DeadPhone,      // Beat 4: celular morto (futuro)
        RunToLandline,  // Beat 5: corre pro fixo (futuro)
        TheCall,        // Beat 6: a ligação (futuro)
        FinalHiding,    // Beat 7: esconde no banheiro (futuro)
        Death,          // Beat 8: a morte (futuro)
        Epilogue        // Beat 9: epílogo (futuro)
    }

    /// <summary>
    /// "Maestro" do Ato 4. Conduz a sequência narrativa beat a beat por meio de
    /// coroutines sequenciais — cada beat é uma coroutine isolada
    /// (<see cref="BeatAwakening"/>, <see cref="BeatInvestigation"/>, ...) e o
    /// avanço é explícito via <see cref="AdvanceToBeat"/>. Não desenha UI nem
    /// gerencia o antagonista diretamente; coordena referências atribuídas no
    /// Inspector (player, tela preta, áudio placeholder) e dispara pensamentos
    /// pelo <see cref="ThoughtSystem"/>.
    ///
    /// Expansão: para adicionar um beat futuro, implemente a coroutine
    /// <c>BeatXxx()</c> correspondente, encadeie <see cref="AdvanceToBeat"/> ao
    /// final dela e registre o case no switch de <see cref="AdvanceToBeat"/>.
    /// </summary>
    public sealed class Act4Director : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("PlayerController travado/liberado ao longo dos beats.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("Overlay preto fullscreen (CanvasGroup). alpha=1 = tela preta. Pode reusar o da cutscene de abertura.")]
        [SerializeField] private CanvasGroup blackScreen;
        [Tooltip("AudioSource para sons scriptados (placeholder por enquanto).")]
        [SerializeField] private AudioSource audioSource;

        [Header("Beat 1 - Awakening")]
        [Tooltip("Som de passos abafados na cozinha (placeholder, opcional).")]
        [SerializeField] private AudioClip footstepsSound;
        [Tooltip("Pensamento ao abrir os olhos: \"O que foi isso?\"")]
        [SerializeField] private ThoughtData awakeningThought;
        [Tooltip("Tempo no preto antes do som de passos.")]
        [SerializeField] private float blackScreenDuration = 2f;
        [Tooltip("Duração do fade da tela preta para visível (abrir os olhos).")]
        [SerializeField] private float fadeInDuration = 2f;
        [Tooltip("Espera após disparar o pensamento, antes de liberar o player.")]
        [SerializeField] private float awakeningThoughtHold = 3f;

        [Header("Beat 1 - Wake Up (deitado → em pé)")]
        [Tooltip("Empty na cama: posição (XZ + Y dos pés) e yaw do corpo ao acordar. Mantenha o Empty em pé (sem inclinar) — quem 'deita' é só a câmera.")]
        [SerializeField] private Transform bedPosition;
        [Tooltip("Pitch da câmera deitada, olhando para o teto. Negativo = para cima (convenção do PlayerController).")]
        [SerializeField] private float lyingCameraPitch = -80f;
        [Tooltip("Altura local da câmera deitada (altura do travesseiro).")]
        [SerializeField] private float lyingCameraHeight = 0.5f;
        [Tooltip("Altura local da câmera em pé ao fim da transição. Ideal ~ standEyeHeight do PlayerController.")]
        [SerializeField] private float standingCameraHeight = 1.6f;
        [Tooltip("UI \"Pressione Espaço para levantar\". Ativado após o pensamento, escondido ao apertar Espaço.")]
        [SerializeField] private GameObject standUpPrompt;

        [Header("Beat 1 - Stand Up Waypoints")]
        [Tooltip("Câmera sentada na cama, olhando pra frente (fim da fase 1 - sentar).")]
        [SerializeField] private Transform standUpWaypointSit;
        [Tooltip("Câmera sentada na borda, corpo virado pro lado livre (fim da fase 2 - virar).")]
        [SerializeField] private Transform standUpWaypointEdge;
        [Tooltip("Câmera de pé, no chão ao lado da cama (fim da fase 3 - levantar). Posição mundial dos OLHOS em pé.")]
        [SerializeField] private Transform standUpWaypointStand;
        [Tooltip("Duração (s) da fase 1: deitado → sentado.")]
        [SerializeField] private float phaseSitDuration = 1.5f;
        [Tooltip("Duração (s) da fase 2: sentado → borda (virar o corpo).")]
        [SerializeField] private float phaseTurnDuration = 1.5f;
        [Tooltip("Duração (s) da fase 3: borda → de pé no chão.")]
        [SerializeField] private float phaseStandDuration = 1.5f;

        [Header("Beat 2 - Investigation")]
        [Tooltip("Ponto de referência do corredor.")]
        [SerializeField] private Transform hallwayZone;
        [Tooltip("Ponto de referência da sala/cozinha.")]
        [SerializeField] private Transform kitchenZone;
        [Tooltip("Raio (m) em torno do ponto que conta como \"entrou na zona\".")]
        [SerializeField] private float zoneTriggerRadius = 2f;
        [Tooltip("Pensamento ao chegar no corredor: \"Tem alguém aí?\"")]
        [SerializeField] private ThoughtData investigationThought1;
        [Tooltip("Pensamento ao chegar na cozinha: \"Eu tranquei a porta. Eu sei que tranquei.\"")]
        [SerializeField] private ThoughtData investigationThought2;
        [Tooltip("Espera após o segundo pensamento antes de avançar para o Beat 3.")]
        [SerializeField] private float investigationThought2Hold = 3f;

        [Header("Debug")]
        [Tooltip("Beat em que a sequência começa. Permite testar um beat específico sem jogar do início.")]
        [SerializeField] private Act4Beat startBeat = Act4Beat.Awakening;
        [Tooltip("Habilita as teclas numéricas (1-9) para pular entre beats durante o Play.")]
        [SerializeField] private bool debugMode = false;

        /// <summary>Beat em execução no momento.</summary>
        public Act4Beat CurrentBeat { get; private set; } = Act4Beat.None;

        // Coroutine do beat atual — guardada para que um salto (debug) ou um
        // avanço cancele limpa o beat anterior antes de iniciar o próximo.
        private Coroutine beatRoutine;

        // CharacterController do player — desabilitado durante o despertar para
        // teleportar e congelar a gravidade (a câmera deitada não pode derivar).
        private CharacterController characterController;

        private void Start()
        {
            if (!HasRequiredReferences())
                return;

            characterController = playerController.GetComponent<CharacterController>();

            AdvanceToBeat(startBeat);
        }

        private void Update()
        {
            if (debugMode)
                HandleDebugKeys();
        }

        /// <summary>
        /// Referências realmente obrigatórias para o fluxo básico. Itens opcionais
        /// (áudio, clips, alguns pensamentos) são tratados com null-check no uso.
        /// </summary>
        private bool HasRequiredReferences()
        {
            bool ok = true;

            if (playerController == null)
            {
                Debug.LogError("[Act4Director] playerController não atribuído no Inspector.", this);
                ok = false;
            }
            if (blackScreen == null)
            {
                Debug.LogError("[Act4Director] blackScreen não atribuído no Inspector.", this);
                ok = false;
            }

            return ok;
        }

        // --- Avanço de beats ----------------------------------------------

        /// <summary>
        /// Define o beat atual e inicia a coroutine correspondente, cancelando
        /// qualquer beat em andamento. Ponto único de transição entre beats.
        /// </summary>
        public void AdvanceToBeat(Act4Beat beat)
        {
            if (beatRoutine != null)
            {
                StopCoroutine(beatRoutine);
                beatRoutine = null;
            }

            CurrentBeat = beat;

            switch (beat)
            {
                case Act4Beat.Awakening:
                    beatRoutine = StartCoroutine(BeatAwakening());
                    break;
                case Act4Beat.Investigation:
                    beatRoutine = StartCoroutine(BeatInvestigation());
                    break;

                // Beats 3-9: a implementar nas próximas semanas.
                case Act4Beat.Discovery:
                    Debug.Log("[Act4Director] BEAT 3 - Discovery (a implementar)");
                    break;
                case Act4Beat.DeadPhone:
                    Debug.Log("[Act4Director] BEAT 4 - DeadPhone (a implementar)");
                    break;
                case Act4Beat.RunToLandline:
                    Debug.Log("[Act4Director] BEAT 5 - RunToLandline (a implementar)");
                    break;
                case Act4Beat.TheCall:
                    Debug.Log("[Act4Director] BEAT 6 - TheCall (a implementar)");
                    break;
                case Act4Beat.FinalHiding:
                    Debug.Log("[Act4Director] BEAT 7 - FinalHiding (a implementar)");
                    break;
                case Act4Beat.Death:
                    Debug.Log("[Act4Director] BEAT 8 - Death (a implementar)");
                    break;
                case Act4Beat.Epilogue:
                    Debug.Log("[Act4Director] BEAT 9 - Epilogue (a implementar)");
                    break;

                case Act4Beat.None:
                default:
                    Debug.LogWarning($"[Act4Director] AdvanceToBeat chamado com beat sem rotina: {beat}", this);
                    break;
            }
        }

        // --- BEAT 1: Awakening --------------------------------------------

        /// <summary>
        /// Despertar deitado: posiciona o player na cama (câmera baixa olhando o
        /// teto, física congelada) -> tela preta -> som de passos na cozinha ->
        /// fade para visível (abrir os olhos) -> pensamento "O que foi isso?" ->
        /// aviso "Pressione Espaço para levantar" -> ao apertar Espaço, transição
        /// automática de levantar (câmera sobe e endireita) -> devolve o controle
        /// já em pé e avança para a investigação.
        /// </summary>
        private IEnumerator BeatAwakening()
        {
            // Estado inicial: travado, deitado, olhos fechados.
            playerController.CanMove = false;
            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // Teleporta para a cama e CONGELA a física (controller desabilitado)
            // por toda a sequência roteirizada: sem isso a gravidade puxaria o
            // player e a câmera deitada derivaria. Reativada só no handoff final.
            PlaceOnBed();
            SetCameraLying();

            blackScreen.alpha = 1f;
            blackScreen.blocksRaycasts = true;

            // Dorme no preto.
            yield return new WaitForSeconds(blackScreenDuration);

            // Som que desperta o protagonista (placeholder até a Fase 6).
            PlaySound(footstepsSound);
            Debug.Log("[Act4Director] SOM: passos abafados na cozinha");

            yield return new WaitForSeconds(1f);

            // Abre os olhos: fade da tela preta para visível (vendo o teto).
            yield return FadeBlackScreen(1f, 0f, fadeInDuration);
            blackScreen.blocksRaycasts = false;

            // "O que foi isso?"
            ShowThought(awakeningThought);

            // Espera 1 frame pra garantir que o runner do ThoughtSystem iniciou,
            // depois aguarda o pensamento sumir completamente da tela.
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // Aviso e espera o jogador apertar Espaço para levantar.
            if (standUpPrompt != null)
                standUpPrompt.SetActive(true);
            yield return new WaitUntil(SpacePressed);
            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // Movimento de levantar em 3 fases (sem controle do jogador, pois
            // CanMove continua false). O StandUpRoutine já faz, ao final, a
            // reconexão câmera→corpo: reposiciona o corpo, reparenta a câmera,
            // reabilita o CharacterController e sincroniza o estado interno.
            yield return StandUpRoutine();

            // Devolve o controle total em pé.
            playerController.CanMove = true;

            AdvanceToBeat(Act4Beat.Investigation);
        }

        /// <summary>
        /// Teleporta o player para <see cref="bedPosition"/> com o controller
        /// desabilitado (CharacterController resiste a setar position direto e a
        /// gravidade derivaria a câmera). Mantém a cápsula em pé: só o yaw do
        /// Empty orienta o corpo — quem "deita" é a câmera, não o capsule.
        /// O controller é reativado apenas no fim do <see cref="BeatAwakening"/>.
        /// </summary>
        private void PlaceOnBed()
        {
            if (characterController != null)
                characterController.enabled = false;

            if (bedPosition == null)
            {
                Debug.LogWarning("[Act4Director] bedPosition não atribuído; player não será teleportado para a cama.", this);
                return;
            }

            Transform body = playerController.transform;
            body.position = bedPosition.position;
            float yaw = bedPosition.rotation.eulerAngles.y;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Coloca a câmera no estado deitado: baixa (altura do travesseiro) e com
        /// o pitch apontado para o teto. Seguro só porque CanMove == false (o
        /// PlayerController não sobrescreve a câmera nesse estado).
        /// </summary>
        private void SetCameraLying()
        {
            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[Act4Director] CameraHolder nulo; não foi possível posicionar a câmera deitada.", this);
                return;
            }

            cam.localPosition = new Vector3(0f, lyingCameraHeight, 0f);
            cam.localRotation = Quaternion.Euler(lyingCameraPitch, 0f, 0f);
        }

        /// <summary>
        /// Movimento de levantar em 3 fases via waypoints mundiais: deitado →
        /// sentado (Sit) → borda virado (Edge) → em pé no chão (Stand). A câmera
        /// é DESPARENTADA do Player (igual ao modo cameraOnly do PlayerHiding)
        /// para ser movida por posições/rotações mundiais; ao final,
        /// <see cref="ReconnectCameraToBody"/> reposiciona o corpo, reparenta a
        /// câmera, reabilita a física e sincroniza o estado interno — garantindo
        /// que o player termine em pé e controlável, sem ir para o limbo.
        /// Se algum waypoint não estiver atribuído, cai num fallback simples
        /// (sobe + endireita em local space) para nunca travar o player deitado.
        /// </summary>
        private IEnumerator StandUpRoutine()
        {
            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[Act4Director] CameraHolder nulo; pulando transição de levantar.", this);
                yield break;
            }

            if (standUpWaypointSit == null || standUpWaypointEdge == null || standUpWaypointStand == null)
            {
                Debug.LogWarning("[Act4Director] Waypoints de levantar não atribuídos; usando fallback simples (sem 3 fases).", this);
                yield return SimpleStandFallback(cam);
                yield break;
            }

            // Desparenta para mover a câmera por posições mundiais dos waypoints,
            // sem eye height/head bob/local do Player interferindo.
            Transform originalParent = cam.parent;
            cam.SetParent(null, worldPositionStays: true);

            Vector3 startPos = cam.position;
            Quaternion startRot = cam.rotation;

            // Fase 1: deitado → sentado na cama.
            yield return LerpCameraWorld(cam, startPos, startRot,
                standUpWaypointSit.position, standUpWaypointSit.rotation, phaseSitDuration);
            // Fase 2: sentado → borda, corpo virado pro lado livre.
            yield return LerpCameraWorld(cam, standUpWaypointSit.position, standUpWaypointSit.rotation,
                standUpWaypointEdge.position, standUpWaypointEdge.rotation, phaseTurnDuration);
            // Fase 3: borda → de pé no chão ao lado da cama.
            yield return LerpCameraWorld(cam, standUpWaypointEdge.position, standUpWaypointEdge.rotation,
                standUpWaypointStand.position, standUpWaypointStand.rotation, phaseStandDuration);

            // Reconecta a câmera ao corpo do player (ponto crítico).
            ReconnectCameraToBody(cam, originalParent, standUpWaypointStand);
        }

        /// <summary>
        /// Interpola posição e rotação MUNDIAIS da câmera (desparentada) de um
        /// estado ao outro com SmoothStep (ease-in-out), garantindo o destino
        /// exato ao final.
        /// </summary>
        private IEnumerator LerpCameraWorld(Transform cam, Vector3 fromPos, Quaternion fromRot,
            Vector3 toPos, Quaternion toRot, float duration)
        {
            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                    cam.position = Vector3.Lerp(fromPos, toPos, t);
                    cam.rotation = Quaternion.Slerp(fromRot, toRot, t);
                    yield return null;
                }
            }

            cam.position = toPos;
            cam.rotation = toRot;
        }

        /// <summary>
        /// Reconexão câmera→corpo ao fim do levantar. Ordem importa:
        /// posiciona o corpo ANTES de reabilitar o CharacterController (senão a
        /// física resiste ao teleporte). O corpo vai para o XZ do waypoint Stand
        /// com Y = (Y dos olhos do waypoint − altura em pé), de modo que a câmera,
        /// de volta em <c>standingCameraHeight</c> local, coincida exatamente com
        /// o waypoint — sem o player ir para o limbo. O yaw do corpo vem da
        /// direção horizontal para onde a câmera olhava.
        /// </summary>
        private void ReconnectCameraToBody(Transform cam, Transform originalParent, Transform standWaypoint)
        {
            Transform body = playerController.transform;

            // Yaw do corpo = direção horizontal do waypoint Stand.
            Vector3 flatForward = standWaypoint.forward;
            flatForward.y = 0f;
            float yaw = flatForward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flatForward, Vector3.up).eulerAngles.y
                : body.eulerAngles.y;

            // Posição dos pés: XZ do waypoint, Y descontando a altura dos olhos.
            Vector3 bodyPos = new Vector3(
                standWaypoint.position.x,
                standWaypoint.position.y - standingCameraHeight,
                standWaypoint.position.z);

            // 1) Corpo no lugar ANTES de religar a física.
            body.SetPositionAndRotation(bodyPos, Quaternion.Euler(0f, yaw, 0f));

            // 2) Reparenta; worldPositionStays:false porque vamos forçar a pose
            //    local em pé logo abaixo (não importa o world intermediário).
            cam.SetParent(originalParent, worldPositionStays: false);
            cam.localPosition = new Vector3(0f, standingCameraHeight, 0f);
            cam.localRotation = Quaternion.identity;

            // 3) Religa a física agora que o corpo já está posicionado.
            if (characterController != null)
                characterController.enabled = true;

            // 4) Alinha o estado interno da câmera do PlayerController (pitch 0,
            //    altura em pé) para o handoff não dar "snap".
            playerController.SyncCameraState(0f, standingCameraHeight);
        }

        /// <summary>
        /// Fallback usado quando os waypoints não estão atribuídos: sobe a câmera
        /// (deitada → em pé) e endireita o pitch em local space, sem desparentar,
        /// depois religa a física e sincroniza o estado. Degrada para o player de
        /// pé sobre a cama, mas nunca o deixa preso deitado.
        /// </summary>
        private IEnumerator SimpleStandFallback(Transform cam)
        {
            float fromHeight = cam.localPosition.y;
            float fromPitch = lyingCameraPitch;
            float dur = phaseSitDuration + phaseTurnDuration + phaseStandDuration;

            if (dur > 0f)
            {
                float elapsed = 0f;
                while (elapsed < dur)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                    cam.localPosition = new Vector3(0f, Mathf.Lerp(fromHeight, standingCameraHeight, t), 0f);
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, 0f, t), 0f, 0f);
                    yield return null;
                }
            }

            cam.localPosition = new Vector3(0f, standingCameraHeight, 0f);
            cam.localRotation = Quaternion.identity;

            if (characterController != null)
                characterController.enabled = true;
            playerController.SyncCameraState(0f, standingCameraHeight);
        }

        /// <summary>True no frame em que a barra de Espaço é pressionada.</summary>
        private static bool SpacePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        // --- BEAT 2: Investigation ----------------------------------------

        /// <summary>
        /// Investigação: o player anda livre. Ao entrar na zona do corredor,
        /// dispara "Tem alguém aí?"; ao chegar na cozinha, dispara "Eu tranquei
        /// a porta..." e avança para o Beat 3.
        /// </summary>
        private IEnumerator BeatInvestigation()
        {
            // Garante que o player está livre (importante ao pular direto para cá).
            playerController.CanMove = true;

            // Espera o player entrar na zona do corredor.
            if (hallwayZone != null)
            {
                yield return new WaitUntil(() => PlayerInZone(hallwayZone));
                ShowThought(investigationThought1); // "Tem alguém aí?"
            }
            else
            {
                Debug.LogWarning("[Act4Director] hallwayZone não atribuída; pulando pensamento do corredor.", this);
            }

            // Espera o player chegar na sala/cozinha.
            if (kitchenZone != null)
            {
                yield return new WaitUntil(() => PlayerInZone(kitchenZone));
                ShowThought(investigationThought2); // "Eu tranquei a porta. Eu sei que tranquei."
            }
            else
            {
                Debug.LogWarning("[Act4Director] kitchenZone não atribuída; pulando pensamento da cozinha.", this);
            }

            yield return new WaitForSeconds(investigationThought2Hold);

            AdvanceToBeat(Act4Beat.Discovery);
        }

        /// <summary>
        /// Verdadeiro quando o player está dentro do <see cref="zoneTriggerRadius"/>
        /// do ponto de referência. Distância no plano XZ — a altura não conta,
        /// já que o ponto pode estar no chão e a câmera/player na altura dos olhos.
        /// </summary>
        private bool PlayerInZone(Transform zone)
        {
            Vector3 p = playerController.transform.position;
            Vector3 z = zone.position;
            float dx = p.x - z.x;
            float dz = p.z - z.z;
            return (dx * dx + dz * dz) <= zoneTriggerRadius * zoneTriggerRadius;
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>Dispara um pensamento via ThoughtSystem, se ambos existirem.</summary>
        private void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>Toca um clip via PlayOneShot apenas se source e clip existirem.</summary>
        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        /// <summary>
        /// Interpola o alpha da tela preta de <paramref name="from"/> até
        /// <paramref name="to"/> ao longo de <paramref name="duration"/> segundos,
        /// garantindo o alpha final exato.
        /// </summary>
        private IEnumerator FadeBlackScreen(float from, float to, float duration)
        {
            if (blackScreen == null)
                yield break;

            if (duration <= 0f)
            {
                blackScreen.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            blackScreen.alpha = from;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                blackScreen.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            blackScreen.alpha = to;
        }

        /// <summary>
        /// Teclas 1-9 pulam diretamente para o beat correspondente (atrás de
        /// <see cref="debugMode"/>). Útil para testar um beat isolado em Play.
        /// </summary>
        private void HandleDebugKeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Awakening);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Investigation);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Discovery);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.DeadPhone);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.RunToLandline);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.TheCall);
            else if (kb.digit7Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.FinalHiding);
            else if (kb.digit8Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Death);
            else if (kb.digit9Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Epilogue);
        }

#if UNITY_EDITOR
        // Visualiza as zonas de investigação no Editor para facilitar o setup.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (hallwayZone != null)
                Gizmos.DrawWireSphere(hallwayZone.position, zoneTriggerRadius);
            Gizmos.color = Color.yellow;
            if (kitchenZone != null)
                Gizmos.DrawWireSphere(kitchenZone.position, zoneTriggerRadius);
        }
#endif
    }
}

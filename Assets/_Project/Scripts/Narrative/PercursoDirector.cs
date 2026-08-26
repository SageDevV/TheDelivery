using UnityEngine;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Interaction;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// "Maestro" do PERCURSO: o ato intermediário entre a cafeteria (Ato 1) e a
    /// recepção do prédio (Ato 2). É a caminhada pela rua — e só isso: a Clear
    /// spawna na frente da cafeteria, anda até a entrada do prédio e, ao chegar,
    /// o ato avança para o Ato 2 e a cena troca para a Recepção.
    ///
    /// Deliberadamente MUITO mais simples que os outros diretores: sem beats, sem
    /// NavMesh, sem NPCs, sem diálogo. Não há nada para coreografar, então não há
    /// máquina de beats — só um estado inicial garantido e a vigia da chegada no
    /// <see cref="Update"/>. A dramaturgia é toda ambiental e mora FORA daqui, em
    /// componentes reutilizáveis que o diretor só aciona na hora certa: o
    /// <see cref="NightfallController"/> anoitece a rua durante a caminhada e, quando
    /// a noite fecha, o <see cref="AmbientMusic"/> desce até o silêncio absoluto —
    /// a Clear chega ao prédio sem nada no ouvido. O que se mantém do padrão dos outros: a checagem do
    /// <c>GameManager.CurrentAct</c> no <see cref="Start"/> (fica INERTE se não for
    /// a vez do percurso, salvo <see cref="autoStartForDebug"/>), o
    /// <c>EnsurePlayerFree</c> (para não herdar travas da cena anterior) e a
    /// delegação da troca de cena ao <see cref="GameManager"/> persistente.
    ///
    /// Fluxo: Cafeteria (Act1) -> <b>Percurso (ActPercurso)</b> -> Recepcao (Act2).
    /// </summary>
    public sealed class PercursoDirector : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("PlayerController da cena. Garantido LIVRE (anda e olha) ao assumir o percurso.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("PlayerInteraction do player (garantido habilitado). Opcional.")]
        [SerializeField] private PlayerInteraction playerInteraction;
        [Tooltip("UI \"Pressione Espaço para levantar\" (se a cena herdar uma do prefab do player). Garantida desativada. Opcional.")]
        [SerializeField] private GameObject standUpPrompt;

        [Header("Spawn e destino")]
        [Tooltip("Onde a Clear começa: a calçada em frente à cafeteria. Posicione ao " +
                 "nível do CHÃO (a origem do Player é nos pés) e num ponto LIVRE — se " +
                 "ficar dentro de geometria, o CharacterController a ejeta ao religar. " +
                 "O yaw orienta o corpo (aponte para o caminho até o prédio).")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Destino: a entrada do prédio. Chegar aqui troca para a Recepção (Ato 2).")]
        [SerializeField] private Transform destinationPoint;
        [Tooltip("Raio (m), no plano XZ, em torno do destino que conta como \"chegou\".")]
        [SerializeField] private float destinationRadius = 2f;
        [Tooltip("Camadas consideradas \"chão\" ao apoiar o player no spawnPoint. " +
                 "Um raycast para baixo evita que ele spawne flutuando (e caia) ou " +
                 "afundado. Exclua a layer do Player.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Anoitecer")]
        [Tooltip("NightfallController da cena: comprime o fim de tarde na duração da caminhada. Disparado quando este diretor assume. Opcional — sem ele a rua só fica na luz autorada.")]
        [SerializeField] private NightfallController nightfall;
        [Tooltip("AmbientMusic da rua. Quando a noite FECHA, a trilha desce até o silêncio absoluto e o source para. " +
                 "Opcional — sem ele a trilha simplesmente continua.")]
        [SerializeField] private AmbientMusic ambientMusic;
        [Tooltip("Segundos do fadeout da trilha ao fechar a noite. Longo de propósito: o silêncio tem que CHEGAR sem ser " +
                 "percebido saindo, senão o corte da música vira o evento em vez do que ele deveria anunciar.")]
        [SerializeField] private float silenceFadeDuration = 5f;

        [Header("Narrativa (opcional)")]
        [Tooltip("Pensamento ao começar a caminhada (ex.: \"Melhor ir pra casa antes que escureça\"). Não bloqueia: a Clear já anda enquanto ele aparece.")]
        [SerializeField] private ThoughtData startThought;

        [Header("Debug")]
        [Tooltip("TESTAR ESTA CENA SOZINHA: marque para dar Play direto na Estrada, sem passar pela Boot. " +
                 "Sem isto o director fica INERTE ao abrir a cena avulsa — não existe GameManager (ele vem da Boot), " +
                 "então CurrentAct nunca é ActPercurso e nada acontece: não spawna, não anoitece, não transiciona. " +
                 "Diferente do Act3/Act4Director (que dividem o apartamento e brigariam entre si), aqui é SEGURO deixar " +
                 "marcado no fluxo real: este é o único director da cena, e quando o Ato 1 carrega a Estrada já é a vez dele. " +
                 "Único efeito colateral do teste avulso: ao chegar no destino não há GameManager para trocar de cena, " +
                 "então a chegada só loga um erro em vez de ir para a Recepção.")]
        [SerializeField] private bool autoStartForDebug = false;

        /// <summary>True quando este diretor assumiu a cena (não está inerte).</summary>
        public bool IsRunning { get; private set; }

        // CharacterController do player: desabilitado durante o teleporte para o
        // spawn (o CC resiste a setar position direto) e religado logo em seguida.
        private CharacterController characterController;

        // Trava de disparo único: a chegada é vigiada por distância no Update, que
        // continua rodando durante o fade da transição — sem isto, a troca de cena
        // seria pedida a cada frame enquanto o player estiver dentro do raio.
        private bool arrived;

        // Trava de disparo único do fadeout da trilha: IsComplete do anoitecer
        // permanece true depois que a noite fecha, então sem isto o FadeOut seria
        // reiniciado a cada frame e a trilha ficaria congelada no volume inicial.
        private bool silenced;

        private void Start()
        {
            if (playerController == null)
            {
                Debug.LogError("[PercursoDirector] playerController não atribuído no Inspector.", this);
                return;
            }

            characterController = playerController.GetComponent<CharacterController>();

            ValidateStandUpPrompt();

            // Mesmo padrão dos outros diretores: só assume se for a vez deste ato
            // (ou em teste isolado). Senão fica inerte e não mexe no player.
            bool isPercurso = GameManager.Instance != null && GameManager.Instance.CurrentAct == GameAct.ActPercurso;
            if (!isPercurso && !autoStartForDebug)
            {
                Debug.Log($"[PercursoDirector] Inerte: CurrentAct não é ActPercurso e autoStartForDebug=false. " +
                          $"(GameManager.Instance {(GameManager.Instance == null ? "NULO — dando Play direto nesta cena? Ligue autoStartForDebug" : $"ok, CurrentAct={GameManager.Instance.CurrentAct}")})", this);
                return;
            }

            IsRunning = true;

            if (destinationPoint == null)
                Debug.LogWarning("[PercursoDirector] destinationPoint não atribuído; a transição para a Recepção nunca será disparada.", this);

            PlaceAtSpawn();
            EnsurePlayerFree();

            // O anoitecer começa JUNTO com a caminhada: a Clear sai da cafeteria no
            // dourado e chega ao prédio no azul. Calibre a duração dele para ser um
            // pouco menor que o tempo do trajeto (ver NightfallController.duration).
            if (nightfall != null)
                nightfall.Play();
            else
                Debug.LogWarning("[PercursoDirector] nightfall não atribuído no Inspector; a rua fica na luz autorada (não anoitece).", this);

            // Cue de partida. Não bloqueia — a Clear já pode andar.
            if (startThought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(startThought);

            Debug.Log("[PercursoDirector] Assumindo o Percurso: andar até a entrada do prédio.", this);
        }

        private void Update()
        {
            if (!IsRunning)
                return;

            // As duas vigias são INDEPENDENTES: a noite pode fechar antes ou depois
            // da chegada, e uma não pode curto-circuitar a outra (foi por isso que a
            // checagem de chegada saiu do topo do Update para um método próprio).
            WatchNightfall();
            WatchDestination();
        }

        /// <summary>
        /// A noite fechou: a trilha da rua desce até o SILÊNCIO ABSOLUTO (o
        /// <see cref="AmbientMusic.FadeOut"/> zera o volume e para o source). O
        /// silêncio é o ponto — a Clear termina a caminhada sem nada no ouvido, e o
        /// prédio a recebe sem trilha nenhuma para se apoiar.
        /// </summary>
        private void WatchNightfall()
        {
            if (silenced || nightfall == null || !nightfall.IsComplete)
                return;

            silenced = true;

            if (ambientMusic == null)
                return;

            ambientMusic.FadeOut(Mathf.Max(0f, silenceFadeDuration));
            Debug.Log($"[PercursoDirector] Noite fechada: trilha em fadeout de {silenceFadeDuration:0.#}s até o silêncio.", this);
        }

        /// <summary>
        /// Chegou na entrada do prédio? Dispara a transição UMA vez — o
        /// <see cref="Update"/> continua rodando durante o fade da troca de cena.
        /// </summary>
        private void WatchDestination()
        {
            if (arrived || destinationPoint == null)
                return;

            if (!PlayerInZone(destinationPoint, destinationRadius))
                return;

            arrived = true;
            GoToRecepcao();
        }

        /// <summary>
        /// Chegou na entrada do prédio: marca o Ato 2 e delega a troca de cena ao
        /// <see cref="GameManager"/> (persistente) — a coroutine roda NELE para
        /// sobreviver ao unload desta cena.
        /// </summary>
        private void GoToRecepcao()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("[PercursoDirector] GameManager.Instance nulo; impossível transicionar para a Recepção.", this);
                return;
            }

            GameManager.Instance.SetAct(GameAct.Act2);
            Debug.Log($"[Percurso->Act2] SetAct(Act2). CurrentAct agora = {GameManager.Instance.CurrentAct}", this);
            GameManager.Instance.StartCoroutine(
                GameManager.Instance.TransitionToScene(GameScene.Recepcao));
        }

        /// <summary>
        /// Teleporta o player para o <see cref="spawnPoint"/> com o
        /// CharacterController desabilitado (o CC resiste a setar position direto),
        /// apoiando-o no chão antes de religar — assim ele não spawna flutuando (e
        /// cai no primeiro frame) nem afundado na calçada (e é ejetado pela
        /// depenetração do CC). Só o yaw do ponto orienta o corpo; pitch/roll do
        /// spawnPoint são ignorados para a cápsula não nascer tombada.
        /// </summary>
        private void PlaceAtSpawn()
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning("[PercursoDirector] spawnPoint não atribuído; o player fica onde estiver na cena.", this);
                return;
            }

            if (characterController != null)
                characterController.enabled = false;

            Transform body = playerController.transform;
            body.SetPositionAndRotation(
                SnapToGround(spawnPoint.position),
                Quaternion.Euler(0f, spawnPoint.rotation.eulerAngles.y, 0f));

            // Religa já: o percurso é só andar, não há cutscene com o CC congelado.
            if (characterController != null)
                characterController.enabled = true;
        }

        /// <summary>
        /// Rejeita um <see cref="standUpPrompt"/> que seja o próprio Player (ou um
        /// ancestral dele). O campo espera um objeto de UI, e o
        /// <see cref="EnsurePlayerFree"/> o DESATIVA — apontá-lo para o Player
        /// desligaria o jogador inteiro (câmera, movimento, colisão) no primeiro
        /// frame do ato. O sintoma disso é uma cena onde "nada acontece", que não
        /// sugere em nada uma referência trocada no Inspector; então a checagem é
        /// feita aqui, uma vez, com a referência descartada em vez de obedecida.
        /// </summary>
        private void ValidateStandUpPrompt()
        {
            if (standUpPrompt == null)
                return;

            // IsChildOf é true também quando os transforms são o MESMO — que é
            // exatamente o caso de apontar o campo para o Player.
            if (!playerController.transform.IsChildOf(standUpPrompt.transform))
                return;

            Debug.LogError($"[PercursoDirector] standUpPrompt ('{standUpPrompt.name}') é o próprio Player ou um pai dele. " +
                           "Desativá-lo desligaria o jogador inteiro, então a referência foi IGNORADA. " +
                           "Aponte o campo para o objeto de UI do aviso, ou deixe-o vazio (esta cena não tem despertar).", this);
            standUpPrompt = null;
        }

        /// <summary>
        /// Garante o estado inicial LIVRE do player: pode andar e olhar, sem trava
        /// de olhar herdada da cena anterior, CharacterController habilitado,
        /// interação ligada e o standUpPrompt (se houver) escondido. Espelha o
        /// <c>EnsurePlayerFree</c> do <see cref="Act2Director"/> — a Clear chega
        /// andando, não há despertar aqui.
        /// </summary>
        private void EnsurePlayerFree()
        {
            if (characterController != null && !characterController.enabled)
                characterController.enabled = true;

            playerController.CanLookOverride = false;
            playerController.CanMove = true;

            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = true;

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);
        }

        /// <summary>True se o player está dentro do raio (XZ) da zona.</summary>
        private bool PlayerInZone(Transform zone, float radius)
        {
            Vector3 p = playerController.transform.position;
            Vector3 z = zone.position;
            float dx = p.x - z.x;
            float dz = p.z - z.z;
            return (dx * dx + dz * dz) <= radius * radius;
        }

        /// <summary>
        /// Ajusta a Y de uma posição-alvo para o chão (raycast para baixo em
        /// <see cref="groundMask"/>), partindo de 1 m acima. Se nada for atingido,
        /// devolve a posição original. Mesmo helper do <see cref="Act1Director"/>.
        /// </summary>
        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * 1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
                return new Vector3(position.x, hit.point.y, position.z);
            return position;
        }

#if UNITY_EDITOR
        // Visualiza spawn e destino no Editor para facilitar o setup da rua.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            if (spawnPoint != null)
            {
                Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
                // Seta curta indicando o yaw (para onde a Clear olha ao spawnar).
                Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + spawnPoint.forward * 1f);
            }

            Gizmos.color = Color.cyan;
            if (destinationPoint != null)
                Gizmos.DrawWireSphere(destinationPoint.position, destinationRadius);
        }
#endif
    }
}

using System.Collections;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEngine;

namespace TheDelivery.AI
{
    /// <summary>
    /// CAPTURA PELO KILLER → GAME OVER. Mora no antagonista (Atos 3/4 no Apartment, Ato 5 na
    /// Escape) e transforma "ele alcançou o jogador" na tela de morte (<see cref="GameOverManager"/>).
    ///
    /// Três vias de detecção (qualquer uma dispara; a primeira vence):
    /// <list type="number">
    /// <item><b>Evento da IA</b> (padrão, recomendado): <see cref="AntagonistAI.PlayerCaught"/> — o
    /// estado Attack da FSM. Já respeita as regras do jogo: escondido e não visto, ele NÃO
    /// pega; esconderijo comprometido (visto entrando ou barulho no microfone), pega.</item>
    /// <item><b>Trigger</b> (opcional): <c>OnTriggerEnter</c> com a tag "Player" num Collider
    /// marcado como Is Trigger neste objeto.</item>
    /// <item><b>Distância</b> (opcional): jogador a menos de <see cref="captureDistance"/>.</item>
    /// </list>
    /// As vias de trigger/distância IGNORAM o jogador escondido (senão furariam o esconderijo).
    ///
    /// <see cref="isDead"/> garante UMA morte por tentativa. Se a captura veio de um esconderijo,
    /// espera a cena de "abrir e puxar" (<see cref="HideSpotPullOut"/>) terminar antes da tela.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KillerCapture : MonoBehaviour
    {
        [Header("Detecção")]
        [Tooltip("Usa o evento de captura da IA (estado Attack). Recomendado.")]
        [SerializeField] private bool useAIEvent = true;
        [Tooltip("Usa OnTriggerEnter com a tag \"Player\" (precisa de um Collider Is Trigger neste objeto).")]
        [SerializeField] private bool useTrigger = false;
        [Tooltip("Usa a distância até o jogador.")]
        [SerializeField] private bool useDistance = false;
        [Tooltip("Distância (m, no plano) que conta como alcançado, se useDistance.")]
        [Min(0.1f)] [SerializeField] private float captureDistance = 1.2f;

        [Header("Referências (vazio = resolve sozinho)")]
        [SerializeField] private AntagonistAI antagonistAI;
        [SerializeField] private HideSpotPullOut pullOut;
        [Tooltip("Tela de morte. Vazio = a da cena (GameOverManager.Instance).")]
        [SerializeField] private GameOverManager gameOver;

        private const string PlayerTag = "Player";

        /// <summary>Já morreu nesta tentativa (a morte dispara uma vez só).</summary>
        public bool isDead { get; private set; }

        private Transform player;

        private void Awake()
        {
            if (antagonistAI == null) antagonistAI = GetComponent<AntagonistAI>();
            if (pullOut == null) pullOut = GetComponent<HideSpotPullOut>();
        }

        private void OnEnable()
        {
            if (useAIEvent && antagonistAI != null)
                antagonistAI.PlayerCaught += OnAICaught;
        }

        private void OnDisable()
        {
            if (antagonistAI != null)
                antagonistAI.PlayerCaught -= OnAICaught;
        }

        private void Start()
        {
            GameObject p = GameObject.FindGameObjectWithTag(PlayerTag);
            player = p != null ? p.transform : null;
        }

        private void Update()
        {
            if (!useDistance || isDead || player == null || PlayerIsHidden())
                return;
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= captureDistance * captureDistance)
                Capture("distância", null);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!useTrigger || isDead || !other.CompareTag(PlayerTag) || PlayerIsHidden())
                return;
            Capture("trigger", null);
        }

        private void OnAICaught(HideSpot fromHideSpot) => Capture("IA (Attack)", fromHideSpot);

        /// <summary>Dispara a morte (uma vez por tentativa). Também pode ser chamado por roteiro.</summary>
        public void Capture(string reason, HideSpot fromHideSpot)
        {
            if (isDead)
                return;
            isDead = true;
            Debug.Log($"[KillerCapture] Jogador alcançado por {name} ({reason}{(fromHideSpot != null ? $", no esconderijo {fromHideSpot.name}" : "")}).", this);
            StartCoroutine(CaptureRoutine(fromHideSpot));
        }

        private IEnumerator CaptureRoutine(HideSpot fromHideSpot)
        {
            // Pego no esconderijo: deixa a cena de "abrir e puxar" acontecer antes da tela de morte.
            yield return null; // o PullOut liga o IsPlaying no mesmo evento
            if (fromHideSpot != null && pullOut != null)
                while (pullOut.IsPlaying)
                    yield return null;

            GameOverManager go = gameOver != null ? gameOver : GameOverManager.Instance;
            if (go != null)
                go.TriggerGameOver(this);
            else
                Debug.LogWarning("[KillerCapture] Nenhum GameOverManager na cena — arraste o prefab GameOver para ela.", this);
        }

        private static bool PlayerIsHidden() => PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden;
    }
}

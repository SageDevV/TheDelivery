using System.Collections;
using TheDelivery.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// TELA DE INÍCIO (cena MainMenu). Só a camada de UI: quem começa a partida é o
    /// <see cref="GameManager"/> (persistente, vindo da Boot) — este script apenas pede.
    /// Assim a lógica de "nova partida" (reset de estado + fade + load do primeiro ato)
    /// fica num lugar só, e o menu pode ser refeito sem tocar nela.
    ///
    /// Os três métodos públicos são ligados nos OnClick dos botões (feito pela tool
    /// Tools/The Delivery/Main Menu - Montar cena).
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Botões")]
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button loadGameButton;
        [SerializeField] private Button quitButton;

        [Header("Aviso \"em breve\" (Carregar Jogo)")]
        [Tooltip("CanvasGroup do aviso temporário. Opcional: sem ele, só o Debug.Log.")]
        [SerializeField] private CanvasGroup notice;
        [SerializeField] private TMP_Text noticeLabel;
        [SerializeField] private string loadNotImplementedText = "Carregar Jogo: em breve";
        [Min(0.1f)]
        [SerializeField] private float noticeDuration = 2f;
        [Min(0.1f)]
        [SerializeField] private float noticeFadeSpeed = 4f;

        [Header("Fallback sem Boot")]
        [Tooltip("Só para teste: dando Play direto nesta cena não existe GameManager, e o Novo Jogo carrega esta " +
                 "cena direto, sem fade. No fluxo real (Play pela Boot) o GameManager conduz e isto é ignorado.")]
        [SerializeField] private GameScene fallbackFirstScene = GameScene.Nightmare;

        private Coroutine noticeRoutine;
        private bool starting;

        private void OnEnable()
        {
            ReleaseCursor();

            // Volta de uma pausa/partida anterior em estado consistente.
            Time.timeScale = 1f;
            AudioListener.pause = false;

            if (notice != null)
            {
                notice.alpha = 0f;
                notice.blocksRaycasts = false;
            }
        }

        private void Start()
        {
            // Foco inicial: teclado/gamepad navegam sem precisar do mouse.
            if (newGameButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(newGameButton.gameObject);
        }

        private void Update()
        {
            // Algo pode travar o cursor por fora (um PlayerController que sobrou, o
            // Editor ao clicar no Game View). No menu ele fica SEMPRE livre.
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                ReleaseCursor();
        }

        // --- OnClick -------------------------------------------------------

        /// <summary>Novo Jogo: reseta o estado global e carrega o primeiro ato (via GameManager).</summary>
        public void OnNewGame()
        {
            if (starting)
                return; // duplo clique durante o fade não pode pedir duas partidas
            starting = true;
            SetButtonsInteractable(false);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartNewGame();
                return;
            }

            Debug.LogWarning("[MainMenu] GameManager ausente (Play direto no MainMenu?). Carregando " +
                             $"\"{fallbackFirstScene}\" sem fade. No fluxo real, dê Play pela Boot.", this);
            SceneManager.LoadScene(fallbackFirstScene.ToString());
        }

        /// <summary>Carregar Jogo: ainda não implementado — só avisa.</summary>
        public void OnLoadGame()
        {
            Debug.Log("[MainMenu] Load Game ainda não implementado.", this);

            if (notice == null)
                return;
            if (noticeLabel != null)
                noticeLabel.text = loadNotImplementedText;
            if (noticeRoutine != null)
                StopCoroutine(noticeRoutine);
            noticeRoutine = StartCoroutine(ShowNotice());
        }

        /// <summary>Sair do Jogo: fecha o build; no Editor, sai do Play Mode.</summary>
        public void OnQuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // --- Internos -------------------------------------------------------

        private IEnumerator ShowNotice()
        {
            // Tempo NÃO escalado: o menu não pode depender de um timeScale que alguém zerou.
            while (notice.alpha < 1f)
            {
                notice.alpha = Mathf.MoveTowards(notice.alpha, 1f, noticeFadeSpeed * Time.unscaledDeltaTime);
                yield return null;
            }

            float t = 0f;
            while (t < noticeDuration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            while (notice.alpha > 0f)
            {
                notice.alpha = Mathf.MoveTowards(notice.alpha, 0f, noticeFadeSpeed * Time.unscaledDeltaTime);
                yield return null;
            }

            noticeRoutine = null;
        }

        private void SetButtonsInteractable(bool value)
        {
            if (newGameButton != null) newGameButton.interactable = value;
            if (loadGameButton != null) loadGameButton.interactable = value;
            if (quitButton != null) quitButton.interactable = value;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

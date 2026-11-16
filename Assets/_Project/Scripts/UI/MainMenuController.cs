using System.Collections;
using System.IO;
using TheDelivery.Core;
using TheDelivery.Persistence;
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
        [Tooltip("\"Atos\": abre a Seleção de Atos.")]
        [SerializeField] private Button actsButton;

        [Header("Seleção de Atos")]
        [Tooltip("Painel da Seleção de Atos (começa escondido).")]
        [SerializeField] private ActSelectUI actSelect;
        [Tooltip("Coluna do menu principal (carimbo + botões), escondida enquanto a Seleção de Atos está aberta.")]
        [SerializeField] private GameObject mainColumn;

        [Header("Aviso (Carregar Jogo)")]
        [Tooltip("CanvasGroup do aviso temporário. Opcional: sem ele, só o Debug.Log.")]
        [SerializeField] private CanvasGroup notice;
        [SerializeField] private TMP_Text noticeLabel;
        [Tooltip("Aviso quando não há save (o botão já fica desabilitado; isto cobre um clique por teclado/atalho).")]
        [SerializeField] private string noSaveText = "Nenhum jogo salvo ainda";
        [Tooltip("Aviso quando o save existe mas não pôde ser lido (corrompido/adulterado).")]
        [SerializeField] private string invalidSaveText = "O jogo salvo não pôde ser carregado";
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
            RefreshLoadButton();

            // Foco inicial: com save, "Carregar Jogo" (continuar é o mais provável); sem, "Novo Jogo".
            Button first = loadGameButton != null && loadGameButton.interactable ? loadGameButton : newGameButton;
            if (first != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        /// <summary>
        /// "Carregar Jogo" só fica clicável com um save VÁLIDO no disco (existe, assinatura
        /// confere, cena/ato conhecidos). Desabilitado, o MenuItemStyle o mostra apagado e a
        /// navegação por teclado/controle pula ele.
        /// </summary>
        public void RefreshLoadButton()
        {
            if (loadGameButton == null)
                return;
            bool valid = SaveManager.HasValidSave();
            loadGameButton.interactable = valid;
            // O MenuItemStyle só redesenha quando o foco muda; o "apagado" tem que aparecer já.
            loadGameButton.GetComponent<MenuItemStyle>()?.Refresh();
            Debug.Log(valid
                ? "[MainMenu] Save válido encontrado — \"Carregar Jogo\" habilitado."
                : "[MainMenu] Sem save válido — \"Carregar Jogo\" desabilitado.", this);
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

        /// <summary>
        /// Carregar Jogo: lê o save e retoma (cena + ato + beat, depois estamina/itens/pose)
        /// pelo GameManager. Sem save válido, avisa e reavalia o botão.
        /// </summary>
        public void OnLoadGame()
        {
            if (starting)
                return;

            if (!SaveManager.TryLoad(out _, out string error))
            {
                ShowNoticeText(File.Exists(SaveManager.SavePath) ? invalidSaveText : noSaveText);
                Debug.LogWarning($"[MainMenu] Carregar Jogo indisponível: {error}.", this);
                RefreshLoadButton();
                return;
            }

            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[MainMenu] GameManager ausente (Play direto no MainMenu?). Dê Play pela Boot para carregar.", this);
                ShowNoticeText("Inicie pela cena Boot para carregar");
                return;
            }

            starting = true;
            SetButtonsInteractable(false);
            if (!GameManager.Instance.LoadSavedGame(out error))
            {
                starting = false;
                SetButtonsInteractable(true);
                RefreshLoadButton();
                ShowNoticeText(invalidSaveText);
            }
        }

        private void ShowNoticeText(string text)
        {
            if (notice == null)
                return;
            if (noticeLabel != null)
                noticeLabel.text = text;
            if (noticeRoutine != null)
                StopCoroutine(noticeRoutine);
            noticeRoutine = StartCoroutine(ShowNotice());
        }

        /// <summary>"Atos": troca a coluna principal pela Seleção de Atos.</summary>
        public void OnActs()
        {
            if (starting || actSelect == null)
                return;
            actSelect.ActChosen -= OnActChosen;
            actSelect.BackRequested -= OnActSelectBack;
            actSelect.ActChosen += OnActChosen;
            actSelect.BackRequested += OnActSelectBack;

            if (mainColumn != null) mainColumn.SetActive(false);
            actSelect.Open();
        }

        /// <summary>Volta da Seleção de Atos para o menu principal (foco no "Atos").</summary>
        public void OnActSelectBack()
        {
            if (actSelect == null || !actSelect.gameObject.activeSelf)
                return;
            actSelect.Close();
            if (mainColumn != null) mainColumn.SetActive(true);
            RefreshLoadButton();
            if (actsButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(actsButton.gameObject);
        }

        /// <summary>Ato liberado escolhido: começa nele (estado de sessão novo) pelo GameManager.</summary>
        private void OnActChosen(GameAct act)
        {
            if (starting)
                return;
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[MainMenu] GameManager ausente (Play direto no MainMenu?). Dê Play pela Boot.", this);
                ShowNoticeText("Inicie pela cena Boot para escolher um ato");
                return;
            }

            starting = true;
            SetButtonsInteractable(false);
            if (!GameManager.Instance.StartAtAct(act))
            {
                starting = false;
                SetButtonsInteractable(true);
                RefreshLoadButton();
                actSelect.Refresh();
            }
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
            if (actsButton != null) actsButton.interactable = value;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

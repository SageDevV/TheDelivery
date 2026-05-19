using System.Collections;
using UnityEngine;
using TheDelivery.Narrative;
using TheDelivery.Player;

/// <summary>
/// Orquestra a sequência cinematográfica de abertura do jogo: tela preta com
/// texto de tempo/local, sons de chave e porta (placeholders), fade-in da cena
/// e disparo do pensamento inicial. Mantém o player travado durante toda a
/// sequência e devolve o controle ao final. Responsabilidade única: timing da
/// abertura. Não gerencia estado de jogo nem desenha UI — apenas coordena
/// referências atribuídas no Inspector.
/// </summary>
public sealed class OpeningCutscene : MonoBehaviour
{
    [Header("Referências de UI")]
    [Tooltip("Overlay preto fullscreen (CanvasGroup). alpha=1 = tela preta.")]
    [SerializeField] private CanvasGroup fadeOverlay;
    [Tooltip("Grupo do texto \"Sexta-feira. 19:43.\" (CanvasGroup).")]
    [SerializeField] private CanvasGroup openingTextGroup;

    [Header("Áudio")]
    [Tooltip("AudioSource usado para os placeholders de chave e porta.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Som da chave girando na fechadura. Pode ficar vazio.")]
    [SerializeField] private AudioClip keySound;
    [Tooltip("Som da porta abrindo. Pode ficar vazio.")]
    [SerializeField] private AudioClip doorOpenSound;

    [Header("Pensamento de abertura")]
    [Tooltip("Pensamento disparado quando a cena fica visível (ex: Thought_Finalmente).")]
    [SerializeField] private ThoughtData openingThought;

    [Header("Timings")]
    [Tooltip("Duração do fade-in do texto inicial.")]
    [SerializeField] private float textFadeInDuration = 0.8f;
    [Tooltip("Tempo que o texto permanece totalmente visível.")]
    [SerializeField] private float textVisibleDuration = 2.0f;
    [Tooltip("Espera entre o som da chave e o som da porta.")]
    [SerializeField] private float keyToDoorDelay = 1.0f;
    [Tooltip("Duração do fade-in da cena (overlay preto 1 -> 0).")]
    [SerializeField] private float sceneFadeInDuration = 1.5f;
    [Tooltip("Duração do fade-out do texto (simultâneo ao fade-in da cena).")]
    [SerializeField] private float textFadeOutDuration = 0.8f;
    [Tooltip("Espera entre a cena ficar visível e o disparo do pensamento.")]
    [SerializeField] private float thoughtDelay = 1.5f;
    [Tooltip("Espera entre o pensamento e a liberação do controle do player.")]
    [SerializeField] private float playerUnlockDelay = 3.0f;

    [Header("Referência do Player")]
    [Tooltip("PlayerController travado durante a cutscene.")]
    [SerializeField] private PlayerController playerController;

    private void Start()
    {
        if (!HasRequiredReferences())
            return;

        // Estado inicial: tela preta, texto invisível, player travado.
        fadeOverlay.alpha = 1f;
        fadeOverlay.blocksRaycasts = true;
        openingTextGroup.alpha = 0f;
        playerController.CanMove = false;

        StartCoroutine(PlayOpening());
    }

    /// <summary>
    /// Verifica as referências obrigatórias (UI e player). Opcionais (áudio,
    /// pensamento) não entram aqui — são tratadas com null-check no uso.
    /// </summary>
    private bool HasRequiredReferences()
    {
        bool ok = true;

        if (fadeOverlay == null)
        {
            Debug.LogError("[OpeningCutscene] fadeOverlay não atribuído no Inspector.", this);
            ok = false;
        }
        if (openingTextGroup == null)
        {
            Debug.LogError("[OpeningCutscene] openingTextGroup não atribuído no Inspector.", this);
            ok = false;
        }
        if (playerController == null)
        {
            Debug.LogError("[OpeningCutscene] playerController não atribuído no Inspector.", this);
            ok = false;
        }

        return ok;
    }

    /// <summary>
    /// Sequência cinematográfica completa da abertura, com timing configurável.
    /// Ordem: fade-in do texto -> hold -> som da chave -> som da porta ->
    /// (fade-out do texto + fade-in da cena, simultâneos) -> pensamento ->
    /// liberação do controle do player.
    /// </summary>
    private IEnumerator PlayOpening()
    {
        // Guarda defensiva: se algo obrigatório sumiu, aborta sem crashar.
        if (fadeOverlay == null || openingTextGroup == null || playerController == null)
            yield break;

        // T=0.0 -> ~0.8 : fade-in do texto "Sexta-feira. 19:43."
        yield return FadeCanvasGroup(openingTextGroup, 0f, 1f, textFadeInDuration);

        // T=0.8 -> 2.8 : texto totalmente visível.
        yield return new WaitForSeconds(textVisibleDuration);

        // T=2.8 : som da chave na fechadura (placeholder, opcional).
        PlaySound(keySound);

        // T=2.8 -> 3.8 : espera até a porta abrir.
        yield return new WaitForSeconds(keyToDoorDelay);

        // T=3.8 : som da porta abrindo (placeholder, opcional).
        PlaySound(doorOpenSound);

        // T=3.8 : fade-out do texto e fade-in da cena começam juntos.
        // O fade-out do texto roda em paralelo; o yield acompanha o fade-in
        // da cena (geralmente o mais longo) e define o T=5.3.
        StartCoroutine(FadeCanvasGroup(openingTextGroup, openingTextGroup.alpha, 0f, textFadeOutDuration));
        yield return FadeCanvasGroup(fadeOverlay, fadeOverlay.alpha, 0f, sceneFadeInDuration);

        // T=5.3 : cena totalmente visível. Espera antes do pensamento.
        yield return new WaitForSeconds(thoughtDelay);

        // T=6.8 : dispara o pensamento "Finalmente." (opcional).
        if (openingThought != null && ThoughtSystem.Instance != null)
            ThoughtSystem.Instance.Show(openingThought);

        // T=6.8 -> 9.8 : espera antes de devolver o controle.
        yield return new WaitForSeconds(playerUnlockDelay);

        // T=9.8 : libera o player e desbloqueia o raycast do overlay.
        playerController.CanMove = true;
        fadeOverlay.blocksRaycasts = false;
    }

    /// <summary>
    /// Interpola linearmente o alpha de um <see cref="CanvasGroup"/> de
    /// <paramref name="from"/> até <paramref name="to"/> ao longo de
    /// <paramref name="duration"/> segundos. Garante o alpha final exato.
    /// </summary>
    private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        group.alpha = from;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        // Evita resíduos tipo 0.999 / 0.001.
        group.alpha = to;
    }

    /// <summary>Toca um clip via PlayOneShot apenas se source e clip existirem.</summary>
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}

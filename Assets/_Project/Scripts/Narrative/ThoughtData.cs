using UnityEngine;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Uma linha de pensamento: o texto, quanto tempo fica visível e
    /// quanto espera antes de aparecer.
    /// </summary>
    [System.Serializable]
    public struct ThoughtLine
    {
        [TextArea(2, 4)]
        public string text;

        [Tooltip("Tempo visível em segundos (após o fade in). Se <= 0, é calculado pelo tamanho do texto.")]
        public float duration;

        [Tooltip("Espera em segundos antes desta linha começar a aparecer.")]
        public float delay;
    }

    /// <summary>
    /// Pensamento(s) do protagonista. Uma frase simples = uma linha;
    /// um pensamento encadeado = várias linhas que aparecem em sequência.
    /// Disparado via <see cref="ThoughtSystem"/>.
    /// </summary>
    [CreateAssetMenu(
        menuName = "The Delivery/Thought",
        fileName = "Thought_")]
    public sealed class ThoughtData : ScriptableObject
    {
        [Tooltip("Linhas exibidas em sequência, uma após a outra.")]
        [SerializeField] private ThoughtLine[] lines = new ThoughtLine[1];

        public ThoughtLine[] Lines => lines;
    }
}

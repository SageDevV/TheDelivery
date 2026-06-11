using UnityEngine;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Uma conversa: sequência de falas entre personagens. Espelha o
    /// <see cref="ThoughtData"/>, mas cada linha tem um locutor (nome) além
    /// do texto. Disparado via <see cref="DialogueSystem"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Dialogue", menuName = "The Delivery/Dialogue Data")]
    public sealed class DialogueData : ScriptableObject
    {
        [System.Serializable]
        public struct Line
        {
            [Tooltip("Nome de quem fala (ex: Marina, Você). String livre.")]
            public string speaker;

            [TextArea(2, 4)]
            [Tooltip("O texto da fala.")]
            public string text;

            [Tooltip("Tempo extra (s) somado ao tempo proporcional desta fala (0 = nenhum).")]
            public float extraHold;
        }

        [Tooltip("As falas da conversa, em ordem.")]
        public Line[] lines;
    }
}

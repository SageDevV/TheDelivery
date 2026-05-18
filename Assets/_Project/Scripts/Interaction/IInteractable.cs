namespace TheDelivery.Interaction
{
    /// <summary>
    /// Contrato de qualquer objeto interativo do jogo (portas, geladeira, celular, TV...).
    /// Implementado por MonoBehaviours; o PlayerInteraction descobre via GetComponentInParent,
    /// então o collider pode estar em um filho enquanto o script fica na raiz do prefab.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Verbo/ação exibido no prompt (ex: "Abrir", "Ligar TV"). Pode ser dinâmico
        /// conforme o estado do objeto (porta fechada -> "Abrir", aberta -> "Fechar").
        /// </summary>
        string InteractionPrompt { get; }

        /// <summary>
        /// Se o objeto deve responder ao input AGORA. False = ignorar completamente
        /// (cooldown, animação em andamento, desabilitado por roteiro).
        /// NÃO use para "trancado" — isso é estado interno; trate dentro de Interact()
        /// para preservar o feedback (chacoalhar maçaneta, etc).
        /// </summary>
        bool CanInteract { get; }

        /// <summary>
        /// Executa a interação. Só é chamado pelo PlayerInteraction quando
        /// CanInteract == true. 'source' permite ao objeto consultar/afetar o player.
        /// </summary>
        void Interact(PlayerInteraction source);
    }
}

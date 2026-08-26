namespace TheDelivery.Core
{
    /// <summary>
    /// Atos do jogo. O <see cref="GameManager"/> guarda apenas o ATO atual
    /// (estado de progresso de alto nível). O beat DENTRO de cada ato é
    /// responsabilidade do diretor daquele ato (ex.: <c>Act4Director</c> com seu
    /// próprio enum de beats) — o GameManager não conhece beats.
    /// </summary>
    public enum GameAct
    {
        None,
        Act1,
        Act2,
        Act3,
        Act4,

        // Ato INTERMEDIÁRIO entre o Ato 1 (cafeteria) e o Ato 2 (recepção): a
        // caminhada pela rua até o prédio. Fica no FIM do enum (e não entre Act1 e
        // Act2, sua posição cronológica) de propósito: os valores existentes já
        // estão serializados em campos do Inspector, e inseri-lo no meio
        // deslocaria os índices de Act2/Act3/Act4, remapeando silenciosamente o
        // que já foi autorado nas cenas.
        ActPercurso,

        // COLD OPEN: o pesadelo que abre o jogo, ANTES do Ato 1. Cronologicamente é o
        // primeiro ato de todos, mas fica no fim do enum pela mesma razão que o
        // ActPercurso — a ordem aqui é histórico de quando o valor foi criado, não a
        // ordem da narrativa. Quem conta a cronologia é o fluxo de transições.
        ActPesadelo
    }
}

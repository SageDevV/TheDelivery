namespace TheDelivery.Core
{
    /// <summary>
    /// Atos do jogo. O <see cref="GameManager"/> guarda apenas o ATO atual
    /// (estado de progresso de alto nível). O beat DENTRO de cada ato é
    /// responsabilidade do diretor daquele ato (ex.: <c>Act4Director</c> com seu
    /// próprio enum de beats) — o GameManager não conhece beats.
    ///
    /// VALORES EXPLÍCITOS: o enum é salvo como NÚMERO nos campos do Inspector, então o
    /// número de cada ato nunca muda. A ordem abaixo é a da HISTÓRIA; um ato novo ganha o
    /// próximo número livre (8) e entra na posição cronológica dele, sem remapear nada do
    /// que já está autorado nas cenas.
    /// </summary>
    public enum GameAct
    {
        None = 0,
        ActNightmare = 6, // cold open: o pesadelo que abre o jogo
        Act1 = 1,         // a cafeteria
        ActJourney = 5,   // a caminhada pela rua até o prédio
        Act2 = 2,         // a recepção
        Act3 = 3,         // o apartamento: a escalada
        Act4 = 4,         // o apartamento: a caçada
        ActEscape = 7     // a fuga, depois do sequestro
    }
}

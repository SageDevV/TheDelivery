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
        Act4
    }
}

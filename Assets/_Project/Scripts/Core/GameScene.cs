namespace TheDelivery.Core
{
    /// <summary>
    /// Cenas do jogo, identificadas por enum (não por string solta) para evitar
    /// erros de digitação ao carregar. ATENÇÃO: o nome de cada valor deve
    /// corresponder EXATAMENTE ao nome do arquivo .unity registrado no Build
    /// Settings — o <see cref="GameManager"/> carrega via <c>scene.ToString()</c>,
    /// então renomear um valor aqui exige renomear o .unity (e vice-versa).
    ///
    /// VALORES EXPLÍCITOS, pelo mesmo motivo do <see cref="GameAct"/>: o número salvo nos
    /// campos do Inspector nunca muda, e a ordem abaixo é a do fluxo do jogo. Cena nova
    /// ganha o próximo número livre (7).
    /// </summary>
    public enum GameScene
    {
        Boot = 0,      // inicialização: só o GameManager e a tela preta persistente
        Nightmare = 5, // ActNightmare (cold open)
        Cafeteria = 1, // Act1
        Road = 4,      // ActJourney — o ATO se chama Journey; a CENA (o arquivo) é Road
        Reception = 2, // Act2
        Apartment = 3, // Act3 e Act4
        Escape = 6     // ActEscape
    }
}

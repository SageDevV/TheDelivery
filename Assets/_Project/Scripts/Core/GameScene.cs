namespace TheDelivery.Core
{
    /// <summary>
    /// Cenas do jogo, identificadas por enum (não por string solta) para evitar
    /// erros de digitação ao carregar. ATENÇÃO: o nome de cada valor deve
    /// corresponder EXATAMENTE ao nome do arquivo .unity registrado no Build
    /// Settings — o <see cref="GameManager"/> carrega via <c>scene.ToString()</c>,
    /// então renomear um valor aqui exige renomear o .unity (e vice-versa).
    /// </summary>
    public enum GameScene
    {
        Boot,        // cena de inicialização (só o GameManager + tela preta persistente)
        Cafeteria,   // Ato 1
        Reception,   // Ato 2 (início)
        Apartment,   // Ato 4 (a cena atual renomeada); Atos 2-3 também acontecem aqui

        // A rua entre a cafeteria e o prédio (ActJourney). O ATO se chama Journey
        // (GameAct.ActJourney, JourneyDirector), mas o valor aqui é Road porque
        // este enum espelha NOME DE ARQUIVO, e a cena no disco é Road.unity —
        // renomear a cena exige renomear este valor junto. No FIM do enum, e não na
        // sua posição cronológica (entre Cafeteria e Reception), para não deslocar os
        // índices dos valores já serializados no Inspector.
        Road,

        // O pesadelo do cold open (ActNightmare). O arquivo tem que se chamar
        // Nightmare.unity e estar no Build Settings — este enum é carregado por NOME.
        Nightmare,

        // A fuga (ActEscape), depois do sequestro no fim do Ato 4. O arquivo tem que se
        // chamar Escape.unity e estar no Build Settings — este enum é carregado por NOME.
        Escape
    }
}

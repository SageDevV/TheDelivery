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
        Recepcao,    // Ato 2 (início)
        Apartamento, // Ato 4 (a cena atual renomeada); Atos 2-3 também acontecem aqui

        // A rua entre a cafeteria e o prédio (ActPercurso). O ATO se chama Percurso
        // (GameAct.ActPercurso, PercursoDirector), mas o valor aqui é Estrada porque
        // este enum espelha NOME DE ARQUIVO, e a cena no disco é Estrada.unity —
        // renomear a cena exige renomear este valor junto. No FIM do enum, e não na
        // sua posição cronológica (entre Cafeteria e Recepcao), para não deslocar os
        // índices dos valores já serializados no Inspector.
        Estrada,

        // O pesadelo do cold open (ActPesadelo). O arquivo tem que se chamar
        // Pesadelo.unity e estar no Build Settings — este enum é carregado por NOME.
        Pesadelo
    }
}

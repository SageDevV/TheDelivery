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
        Apartamento  // Ato 4 (a cena atual renomeada); Atos 2-3 também acontecem aqui
    }
}

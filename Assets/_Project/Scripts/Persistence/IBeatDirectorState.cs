namespace TheDelivery.Persistence
{
    /// <summary>
    /// Visão NÃO-genérica de um diretor de ato (BeatDirector&lt;TBeat&gt;), para o save saber
    /// qual diretor está ativo e em que beat — sem conhecer o enum de cada ato.
    /// </summary>
    public interface IBeatDirectorState
    {
        /// <summary>Identificador estável do diretor (o nome do tipo, ex.: "Act4Director").</summary>
        string DirectorId { get; }

        /// <summary>Beat atual como número (0 = None = o ato não começou).</summary>
        int CurrentBeatValue { get; }
    }
}

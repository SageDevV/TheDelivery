namespace TheDelivery.AI
{
    /// <summary>
    /// A luz que a jogadora carrega (Ato 5: a lanterna do celular). Acesa, ela é vista de longe pela
    /// <see cref="AIVision"/> (lightSpotRadius) — no breu, a luz a denuncia e o antagonista entra em perseguição.
    /// </summary>
    public static class PlayerLight
    {
        public static bool On { get; set; }
    }
}

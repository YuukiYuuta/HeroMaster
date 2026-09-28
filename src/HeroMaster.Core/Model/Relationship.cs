namespace HeroMaster.Core.Model
{
    /// <summary>
    /// Направленное отношение: что From думает о To. Все оси 0–100.
    /// To может быть id героя или <see cref="Ids.Master"/>.
    /// </summary>
    public sealed class Relationship
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public int Trust { get; set; }
        public int Respect { get; set; }
        public int Affection { get; set; }
        public int Fear { get; set; }
        public int Rivalry { get; set; }
    }

    public static class Ids
    {
        public const string Master = "master";
    }
}

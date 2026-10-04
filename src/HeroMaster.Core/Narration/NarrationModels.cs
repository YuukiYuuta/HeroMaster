namespace HeroMaster.Core.Narration
{
    /// <summary>Откуда текст: из шаблона (работает всегда) или от языковой модели.</summary>
    public static class TextSource
    {
        public const string Template = "template";
        public const string Ai = "ai";
    }

    /// <summary>Запись в личном дневнике героя за один день.</summary>
    public sealed class DiaryEntry
    {
        public int Day { get; set; }
        public string Text { get; set; } = "";
        public string Source { get; set; } = TextSource.Template;
    }

    /// <summary>Текст для мастера: утренняя сводка или отчёт о бое.</summary>
    public sealed class DayReport
    {
        public const string Morning = "morning";
        public const string Battle = "battle";

        public int Day { get; set; }
        public string Kind { get; set; } = Morning;
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public string Source { get; set; } = TextSource.Template;
    }
}

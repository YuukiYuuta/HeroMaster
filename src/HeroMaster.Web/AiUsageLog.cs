using System.Text.Json;

namespace HeroMaster.Web;

/// <summary>
/// Учёт вызовов языковой модели: сколько раз звали, сколько неудач, сколько токенов ушло.
/// Хранится отдельно от партии (saves/ai_usage.json): новая партия счётчик не сбрасывает.
/// </summary>
public sealed class AiUsageLog
{
    private readonly string _path;
    private readonly object _lock = new();
    private Totals _totals;

    public AiUsageLog(string path)
    {
        _path = path;
        _totals = Load(path);
    }

    public void Success(AiUsage usage, int day)
    {
        lock (_lock)
        {
            _totals.Calls++;
            Add(usage);
            _totals.LastDay = day;
            _totals.LastError = null;
            _totals.LastCallUtc = DateTime.UtcNow;
            Save();
        }
    }

    public void Failure(string error, AiUsage? usage, int day)
    {
        lock (_lock)
        {
            _totals.Calls++;
            _totals.Failures++;
            if (usage != null)
                Add(usage);
            _totals.LastDay = day;
            _totals.LastError = error;
            _totals.LastCallUtc = DateTime.UtcNow;
            Save();
        }
    }

    public Totals Snapshot()
    {
        lock (_lock)
            return (Totals)_totals.Clone();
    }

    private void Add(AiUsage u)
    {
        _totals.InputTokens += u.InputTokens;
        _totals.OutputTokens += u.OutputTokens;
        _totals.CacheReadTokens += u.CacheReadTokens;
        _totals.CacheWriteTokens += u.CacheWriteTokens;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_totals, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // Учёт — не игра: если файл занят, просто попробуем в следующий раз.
        }
    }

    private static Totals Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Totals>(File.ReadAllText(path)) ?? new Totals() : new Totals();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Totals();
        }
    }

    public sealed class Totals : ICloneable
    {
        public int Calls { get; set; }
        public int Failures { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long CacheReadTokens { get; set; }
        public long CacheWriteTokens { get; set; }
        public int LastDay { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastCallUtc { get; set; }

        public object Clone() => MemberwiseClone();
    }
}

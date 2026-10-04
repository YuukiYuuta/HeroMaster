using System.Text.Encodings.Web;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using HeroMaster.Core.Narration;

namespace HeroMaster.Web;

/// <summary>
/// Рассказчик на языковой модели Claude: переписывает шаблонные тексты дня живее.
/// Ничего не решает — получает только факты (<see cref="DayBrief"/>) и возвращает тексты.
/// Нет ключа, ошибка или таймаут — остаются шаблоны, игра идёт как шла.
/// </summary>
public sealed class AiNarrator
{
    public const string DefaultModel = "claude-opus-5-5";
    // Если модель откажется писать (сработал фильтр), сервер сам попробует запасную модель.
    private const string FallbackBeta = "server-side-fallback-2026-07-01";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    private readonly AnthropicClient? _client;

    public string Model { get; }
    public string Effort { get; }
    public bool Enabled => _client != null;
    /// <summary>Почему рассказчик выключен (для панели); null — включён.</summary>
    public string? DisabledReason { get; }

    public AiNarrator(IConfiguration config)
    {
        Model = config["Narrator:Model"] is { Length: > 0 } m ? m : DefaultModel;
        Effort = config["Narrator:Effort"] is { Length: > 0 } e ? e : "low";

        if (string.Equals(config["Narrator:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
            DisabledReason = "выключен в настройках (appsettings.json → Narrator:Enabled)";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            DisabledReason = "нет ключа: переменная окружения ANTHROPIC_API_KEY не задана";
        else
            _client = new AnthropicClient(); // ключ берётся из ANTHROPIC_API_KEY
    }

    public async Task<AiTexts> Rewrite(DayBrief brief, CancellationToken cancel)
    {
        if (_client == null)
            throw new InvalidOperationException(DisabledReason);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Timeout);

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 12000,
            Betas = [FallbackBeta],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort,
                Format = new BetaJsonOutputFormat { Schema = Schema() }
            },
            System = new List<BetaTextBlockParam>
            {
                new() { Text = SystemPrompt, CacheControl = new BetaCacheControlEphemeral() }
            },
            Messages = [new() { Role = Role.User, Content = JsonSerializer.Serialize(brief, BriefJson) }]
        };

        var message = await _client.Beta.Messages.Create(parameters, timeout.Token);

        var usage = new AiUsage
        {
            InputTokens = message.Usage.InputTokens,
            OutputTokens = message.Usage.OutputTokens,
            CacheReadTokens = message.Usage.CacheReadInputTokens ?? 0,
            CacheWriteTokens = message.Usage.CacheCreationInputTokens ?? 0
        };

        string stop = message.StopReason?.ToString() ?? "";
        if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            throw new AiNarrationException("модель отказалась писать тексты этого дня", usage);
        if (stop.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
            throw new AiNarrationException("ответ модели обрезан (не хватило max_tokens)", usage);

        var json = string.Concat(message.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        try
        {
            var parsed = JsonSerializer.Deserialize<AiTextsJson>(json, BriefJson)
                         ?? throw new JsonException("пустой ответ");
            return new AiTexts
            {
                Diaries = parsed.Diaries.Where(d => !string.IsNullOrWhiteSpace(d.HeroId))
                    .GroupBy(d => d.HeroId).ToDictionary(g => g.Key, g => g.First().Text),
                Morning = parsed.Morning,
                Battle = brief.BattleDraft == null ? null : parsed.Battle,
                Usage = usage
            };
        }
        catch (JsonException ex)
        {
            throw new AiNarrationException("не удалось разобрать ответ модели: " + ex.Message, usage);
        }
    }

    private static readonly JsonSerializerOptions BriefJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static Dictionary<string, JsonElement> Schema() => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            diaries = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { heroId = new { type = "string" }, text = new { type = "string" } },
                    required = new[] { "heroId", "text" },
                    additionalProperties = false
                }
            },
            morning = new { type = "string" },
            battle = new { type = "string" }
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "diaries", "morning", "battle" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
    };

    private const string SystemPrompt = """
        Ты — рассказчик в игре «Мастер героев». Обычных людей (деревенских парней, кухарок, ремесленников) против воли призвали в Башню, где они сражаются с чудовищами под началом мастера — это игрок. Мастер не разговаривает с героями: он влияет на них только поступками — кого взять на вылазку, как поделить добычу, что подарить, какой совет дать в бою.

        Тебе дают факты одного игрового дня в JSON и черновики текстов, собранные по шаблонам. Перепиши их живым языком:

        1. diaries — запись в личном дневнике каждого героя из списка heroes (heroId — как во входных данных). От первого лица, 2–5 предложений, голосом именно этого человека: его профессия, характер, ценности, настроение, мечта. Сохраняй манеру его прошлых записей (recentDiary), чтобы голос не менялся от дня ко дню. Глаголы — в роде, соответствующем полу героя.
        2. morning — утренняя сводка для мастера о вчерашнем дне: сдержанно и по делу, как доклад, 2–6 предложений, в третьем лице.
        3. battle — отчёт о бое для мастера: 3–8 предложений, в третьем лице, наглядно — кто как держался, кто отличился, кто пострадал. Если battleDraft равен null, верни пустую строку.

        Строгие правила:
        - Пиши только о том, что есть во входных данных (today, beliefs, черновики). Не придумывай события, раны, смерти, предметы, места, имена и числа. Можно передать ощущения и мысли героя, если они следуют из фактов и его характера.
        - Убеждения (beliefs) окрашивают взгляд героя; пометка «не забудет никогда» — то, что болит постоянно.
        - Герои ничего не знают об игровых числах и механиках: не упоминай доверие, звёзды, решимость, статусы, проценты.
        - Простой живой русский язык, без пафоса и канцелярита, без эмодзи и разметки.
        - Не обращайся к игроку на «вы». Каждый герой из списка должен получить запись.
        """;

    private sealed class AiTextsJson
    {
        public List<AiDiaryJson> Diaries { get; set; } = new();
        public string Morning { get; set; } = "";
        public string Battle { get; set; } = "";
    }

    private sealed class AiDiaryJson
    {
        public string HeroId { get; set; } = "";
        public string Text { get; set; } = "";
    }
}

public sealed class AiTexts
{
    public Dictionary<string, string> Diaries { get; set; } = new();
    public string? Morning { get; set; }
    public string? Battle { get; set; }
    public AiUsage Usage { get; set; } = new();
}

public sealed class AiUsage
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
}

/// <summary>Сбой рассказчика, после которого всё же известен расход токенов.</summary>
public sealed class AiNarrationException : Exception
{
    public AiNarrationException(string message, AiUsage usage) : base(message) => Usage = usage;
    public AiUsage Usage { get; }
}

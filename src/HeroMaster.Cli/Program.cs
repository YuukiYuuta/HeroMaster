using System.Text;
using HeroMaster.Core.Content;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.World;

Console.OutputEncoding = Encoding.UTF8;

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var savePath = Path.Combine(Environment.CurrentDirectory, "saves", "world.json");

var command = args.Length > 0 ? args[0] : "help";

try
{
    switch (command)
    {
        case "new":
            ulong seed = args.Length > 1 && ulong.TryParse(args[1], out var s) ? s : (ulong)DateTime.UtcNow.Ticks;
            var catalog = GameJson.LoadCatalog(Path.Combine(dataDir, "heroes.json"));
            var config = GameJson.LoadConfig(Path.Combine(dataDir, "balance.json"));
            var created = WorldFactory.Create(catalog, config, seed);
            GameJson.SaveWorld(created, savePath);
            Console.WriteLine($"Новая партия, зерно {seed}. Сохранено: {savePath}");
            Console.WriteLine();
            PrintRoster(created, catalog);
            break;

        case "roster":
            PrintRoster(GameJson.LoadWorld(savePath), GameJson.LoadCatalog(Path.Combine(dataDir, "heroes.json")));
            break;

        case "log":
            foreach (var e in GameJson.LoadWorld(savePath).Log.Events)
                Console.WriteLine($"[день {e.Day}, #{e.Id}] {e.Summary}");
            break;

        default:
            Console.WriteLine("Мастер героев — прототип, этап 0");
            Console.WriteLine();
            Console.WriteLine("  new [зерно]   начать новую партию (одно зерно — одинаковое начало)");
            Console.WriteLine("  roster        показать отряд из сохранения");
            Console.WriteLine("  log           показать лог событий");
            break;
    }
    return 0;
}
catch (FileNotFoundException ex) when (ex.FileName == savePath)
{
    Console.Error.WriteLine("Сохранения нет. Начните партию командой: new");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Ошибка: " + ex.Message);
    return 1;
}

static void PrintRoster(GameWorld world, HeroCatalog catalog)
{
    var valueText = catalog.ValueCatalog.ToDictionary(v => v.Id, v => v.Text);

    foreach (var hero in world.Heroes)
    {
        var toMaster = world.TowardMaster(hero.Id);
        Console.WriteLine($"{new string('★', hero.Stars)} {hero.Name} — {hero.Profession}");
        Console.WriteLine($"  {hero.Bio}");
        Console.WriteLine($"  Мечта: {hero.Dream}");
        Console.WriteLine("  Ценности: " + string.Join("; ", hero.Values.Select(v => valueText[v])));
        Console.WriteLine("  Черты: " + string.Join(", ", hero.Traits.All().Select(t => $"{TraitName(t.Key)} {t.Value}")));
        Console.WriteLine($"  К мастеру: доверие {toMaster.Trust}, уважение {toMaster.Respect}, статус: {StatusName(hero.State.Status)}");

        var notable = world.Relationships
            .Where(r => r.From == hero.Id && r.To != Ids.Master)
            .Select(r => (r, note: Describe(r)))
            .Where(x => x.note != null)
            .ToList();
        foreach (var (r, note) in notable)
            Console.WriteLine($"  → {world.GetHero(r.To).Name}: {note}");
        Console.WriteLine();
    }
}

static string? Describe(Relationship r)
{
    var notes = new List<string>();
    if (r.Rivalry >= 50) notes.Add($"соперничество {r.Rivalry}");
    if (r.Respect >= 65) notes.Add($"уважение {r.Respect}");
    if (r.Affection >= 50) notes.Add($"привязанность {r.Affection}");
    if (r.Trust <= 35) notes.Add($"недоверие (доверие {r.Trust})");
    if (r.Trust >= 65) notes.Add($"доверие {r.Trust}");
    if (r.Fear >= 20) notes.Add($"опасение {r.Fear}");
    return notes.Count > 0 ? string.Join(", ", notes) : null;
}

static string TraitName(string key) => key switch
{
    "courage" => "смелость",
    "pride" => "гордость",
    "empathy" => "эмпатия",
    "discipline" => "дисциплина",
    "ambition" => "амбиции",
    "pragmatism" => "прагматизм",
    _ => key
};

static string StatusName(MoodStatus status) => status switch
{
    MoodStatus.Normal => "в порядке",
    MoodStatus.Discontented => "недоволен",
    MoodStatus.OnEdge => "на грани",
    MoodStatus.Boycott => "бойкот",
    _ => status.ToString()
};

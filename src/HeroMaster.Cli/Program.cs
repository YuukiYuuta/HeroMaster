using System.Text;
using HeroMaster.Core.Content;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var savePath = Path.Combine(Environment.CurrentDirectory, "saves", "world.json");

var command = args.Length > 0 ? args[0] : "help";

try
{
    switch (command)
    {
        case "new":
        {
            ulong seed = args.Length > 1 && ulong.TryParse(args[1], out var s) ? s : (ulong)DateTime.UtcNow.Ticks;
            var rules = LoadRules();
            var created = WorldFactory.Create(rules.Catalog, rules.Config, seed);
            GameJson.SaveWorld(created, savePath);
            Console.WriteLine($"Новая партия, зерно {seed}. Сохранено: {savePath}");
            Console.WriteLine();
            PrintRoster(created, rules.Catalog);
            Console.WriteLine("Играть: play");
            break;
        }

        case "roster":
            PrintRoster(GameJson.LoadWorld(savePath), LoadRules().Catalog);
            break;

        case "log":
            foreach (var e in GameJson.LoadWorld(savePath).Log.Events)
                Console.WriteLine($"[день {e.Day}, #{e.Id}] {e.Summary}");
            break;

        case "play":
            Play(GameJson.LoadWorld(savePath), LoadRules());
            break;

        default:
            Console.WriteLine("Мастер героев — прототип, этап 0");
            Console.WriteLine();
            Console.WriteLine("  new [зерно]   начать новую партию (одно зерно — одинаковое начало)");
            Console.WriteLine("  play          играть: день за днём");
            Console.WriteLine("  roster        показать отряд из сохранения");
            Console.WriteLine("  log           показать весь лог событий");
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

GameRules LoadRules() => new GameRules(
    GameJson.LoadCatalog(Path.Combine(dataDir, "heroes.json")),
    GameJson.LoadConfig(Path.Combine(dataDir, "balance.json")));

void Play(GameWorld world, GameRules rules)
{
    var decisions = new MasterDecisions();
    PrintMorning(world, rules);
    PrintPlayHelp(rules);

    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();
        if (line == null)
            return;
        // Невидимая метка BOM может прийти в начале ввода из другой программы.
        var parts = line.TrimStart('﻿').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            continue;

        try
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "help":
                    PrintPlayHelp(rules);
                    break;

                case "report":
                    PrintMorning(world, rules);
                    break;

                case "team":
                    decisions.Team = parts.Skip(1).ToList();
                    Console.WriteLine(decisions.Team.Count == 0 ? "Вылазки сегодня не будет." : "Команда: " + Names(world, decisions.Team));
                    break;

                case "loot":
                    var shares = new Dictionary<string, int>();
                    for (int i = 1; i + 1 < parts.Length; i += 2)
                        shares[parts[i]] = int.Parse(parts[i + 1]);
                    decisions.LootShares = shares;
                    decisions.KeepLoot = false;
                    Console.WriteLine("Делёж: " + string.Join(", ", shares.Select(p => $"{p.Key} {p.Value}")));
                    break;

                case "keep":
                    decisions.LootShares = null;
                    decisions.KeepLoot = true;
                    Console.WriteLine("Добыча остаётся мастеру.");
                    break;

                case "gift":
                    decisions.Gifts.Add(new GiftOrder { HeroId = parts[1], KindId = parts[2] });
                    Console.WriteLine($"Подарок: {parts[2]} для {parts[1]}.");
                    break;

                case "hint":
                    decisions.Hints[parts[1]] = ParseActivity(parts[2]);
                    Console.WriteLine($"Намёк для {parts[1]}: {ActivityName(decisions.Hints[parts[1]])}.");
                    break;

                case "plan":
                    PrintPlan(world, decisions);
                    break;

                case "clear":
                    decisions = new MasterDecisions();
                    Console.WriteLine("Решения на сегодня сброшены.");
                    break;

                case "go":
                    var errors = decisions.Validate(world, rules);
                    if (errors.Count > 0)
                    {
                        foreach (var e in errors)
                            Console.WriteLine("  ! " + e);
                        break;
                    }
                    var events = DayEngine.RunDay(world, rules, decisions);
                    GameJson.SaveWorld(world, savePath);
                    PrintDay(world.Day, events);
                    decisions = new MasterDecisions();
                    PrintMorning(world, rules);
                    break;

                case "quit":
                case "exit":
                    return;

                default:
                    Console.WriteLine("Неизвестная команда. help — список команд.");
                    break;
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException || ex is FormatException || ex is ArgumentException)
        {
            Console.WriteLine("Не понял команду: " + ex.Message + " (help — примеры)");
        }
    }
}

void PrintPlayHelp(GameRules rules)
{
    var gifts = string.Join(", ", rules.Config.Gifts.Kinds.Select(k => $"{k.Id} — {k.Name}, {k.Cost} зол."));
    Console.WriteLine("Команды мастера (герои по id: tim, marta, pip, gron, ari, vale, anselm):");
    Console.WriteLine("  team tim gron ari vale   отправить на вылазку (пусто — без вылазки)");
    Console.WriteLine("  loot tim 10 gron 12      поделить добычу прошлой вылазки (остаток — мастеру)");
    Console.WriteLine("  keep                     оставить всю добычу себе");
    Console.WriteLine($"  gift ari trinket         подарок ({gifts})");
    Console.WriteLine("  hint pip rest            намёк: rest, train, socialize, work");
    Console.WriteLine("  plan / clear             показать / сбросить решения на сегодня");
    Console.WriteLine("  go                       прожить день");
    Console.WriteLine("  report / help / quit");
}

void PrintMorning(GameWorld world, GameRules rules)
{
    Console.WriteLine();
    Console.WriteLine($"=== Утро дня {world.Day + 1} ===  Золото мастера: {world.Master.Gold}");
    if (world.Master.PendingLoot is { } pool)
        Console.WriteLine($"Неподелённая добыча: {pool.Amount} золота (ходили: {Names(world, pool.Participants)})");
    Console.WriteLine($"{"Герой",-18}{"id",-8}{"★",-4}{"Статус",-14}{"Доверие",-9}{"Решим.",-8}{"Устал.",-8}Стресс");
    foreach (var h in world.Heroes)
    {
        var s = h.State;
        Console.WriteLine($"{h.Name,-18}{h.Id,-8}{h.Stars,-4}{StatusName(s.Status),-14}{world.TowardMaster(h.Id).Trust,-9}{s.Resolve,-8}{s.Fatigue,-8}{s.Stress}");
    }
    Console.WriteLine();
}

void PrintPlan(GameWorld world, MasterDecisions d)
{
    Console.WriteLine("Вылазка: " + (d.Team.Count == 0 ? "нет" : Names(world, d.Team)));
    if (world.Master.PendingLoot != null)
        Console.WriteLine("Добыча: " + (d.KeepLoot ? "оставить себе" : d.LootShares == null ? "не решено" : string.Join(", ", d.LootShares.Select(p => $"{p.Key} {p.Value}"))));
    if (d.Gifts.Count > 0)
        Console.WriteLine("Подарки: " + string.Join(", ", d.Gifts.Select(g => $"{g.KindId} → {g.HeroId}")));
    if (d.Hints.Count > 0)
        Console.WriteLine("Намёки: " + string.Join(", ", d.Hints.Select(h => $"{h.Key}: {ActivityName(h.Value)}")));
}

void PrintDay(int day, List<GameEvent> events)
{
    Console.WriteLine();
    Console.WriteLine($"=== День {day} ===");
    DayPhase? phase = null;
    foreach (var e in events)
    {
        if (e.Phase != phase)
        {
            phase = e.Phase;
            Console.WriteLine($"-- {PhaseName(e.Phase)} --");
        }
        Console.WriteLine("  " + e.Summary);
    }
}

string Names(GameWorld world, IEnumerable<string> ids) =>
    string.Join(", ", ids.Select(id => world.HasHero(id) ? world.GetHero(id).Name : id));

static Activity ParseActivity(string s) => s.ToLowerInvariant() switch
{
    "rest" or "отдых" => Activity.Rest,
    "train" or "тренировка" => Activity.Train,
    "socialize" or "общение" => Activity.Socialize,
    "work" or "работа" => Activity.Work,
    _ => throw new ArgumentException($"неизвестное занятие «{s}»")
};

static string ActivityName(Activity a) => a switch
{
    Activity.Rest => "отдых",
    Activity.Train => "тренировка",
    Activity.Socialize => "общение",
    Activity.Work => "работа",
    _ => "обида"
};

static string PhaseName(DayPhase p) => p switch
{
    DayPhase.MasterDecisions => "Решения мастера",
    DayPhase.Expedition => "Вылазка",
    DayPhase.BaseLife => "Жизнь на базе",
    DayPhase.NightReflection => "Ночь",
    _ => "Утро"
};

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

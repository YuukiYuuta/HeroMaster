using HeroMaster.Core.Events;
using HeroMaster.Core.Memory;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Tests;

public class MemoryTests
{
    private static (GameWorld world, GameRules rules) NewGame(ulong seed = 42)
    {
        var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
        return (WorldFactory.Create(rules.Catalog, rules.Config, seed), rules);
    }

    private static readonly List<string> Party = new() { "tim", "gron", "marta", "pip" };

    /// <summary>День с дележом добычи: keep — мастер всё забирает, иначе — всем поровну.</summary>
    private static void LootDay(GameWorld world, GameRules rules, bool keep)
    {
        world.Master.PendingLoot = new LootPool { Amount = 100, FromDay = world.Day, Participants = Party.ToList() };
        DayEngine.RunDay(world, rules, new MasterDecisions
        {
            KeepLoot = keep,
            LootShares = keep ? null : Party.ToDictionary(id => id, _ => 25)
        });
    }

    private static Belief? Find(Hero hero, string id) => hero.Beliefs.FirstOrDefault(b => b.Id == id);

    [Fact]
    public void Greedy_master_is_remembered_and_fair_shares_slowly_wash_it_out()
    {
        var (world, rules) = NewGame();
        var gron = world.GetHero("gron");

        LootDay(world, rules, keep: true);
        LootDay(world, rules, keep: true);
        var greedy = Find(gron, "master_greedy");
        Assert.NotNull(greedy);
        Assert.Equal(Ids.Master, greedy!.AboutId);
        Assert.Equal("Мастер забирает добычу себе", greedy.Text);
        int strong = greedy.Strength;

        LootDay(world, rules, keep: false);
        Assert.True((Find(gron, "master_greedy")?.Strength ?? 0) < strong);

        for (int i = 0; i < 6; i++)
            LootDay(world, rules, keep: false);
        Assert.Null(Find(gron, "master_greedy"));
    }

    [Fact]
    public void Unconfirmed_beliefs_fade_and_are_forgotten()
    {
        var (world, rules) = NewGame();
        LootDay(world, rules, keep: true);
        var tim = world.GetHero("tim");
        int start = Find(tim, "master_greedy")!.Strength;

        DayEngine.RunDay(world, rules, new MasterDecisions());
        Assert.Equal(start - rules.Config.Memory.DecayPerNight, Find(tim, "master_greedy")!.Strength);

        for (int i = 0; i < start; i++)
            DayEngine.RunDay(world, rules, new MasterDecisions());
        Assert.Null(Find(tim, "master_greedy"));
    }

    [Fact]
    public void Death_of_a_comrade_becomes_a_key_memory_that_never_fades()
    {
        var (world, rules) = NewGame();
        var gron = world.GetHero("gron");
        world.Heroes.Remove(gron);
        world.Fallen.Add(new FallenHero { Id = gron.Id, Name = gron.Name, NameGenitive = gron.NameGenitive, NameInstrumental = gron.NameInstrumental });
        world.Day = 1;
        var died = world.Log.Append(1, DayPhase.Expedition, "hero_died", "Грон погиб.", 10, -5,
            world.Heroes.Select(h => h.Id).Append("gron").Append(Ids.Master).ToArray());
        died.Data["hero"] = "gron";
        Reflection.Run(world, rules);

        var pip = world.GetHero("pip");
        var grief = Find(pip, "grief:gron");
        Assert.NotNull(grief);
        Assert.True(grief!.IsKeyMemory);
        Assert.Equal("Не могу забыть гибель Грона", grief.Text);
        int strength = grief.Strength;

        for (int i = 0; i < 40; i++)
            DayEngine.RunDay(world, rules, new MasterDecisions());
        Assert.Equal(strength, Find(pip, "grief:gron")!.Strength);
    }

    [Fact]
    public void A_quarrel_undermines_the_belief_in_a_good_comrade()
    {
        var (world, rules) = NewGame();
        world.Day = 1;
        for (int i = 0; i < 3; i++)
            world.Log.Append(1, DayPhase.BaseLife, "socialized", "Тим провёл время с Мартой.", 2, 1, "tim", "marta");
        Reflection.Run(world, rules);
        var tim = world.GetHero("tim");
        Assert.Equal("Марта — хороший товарищ", Find(tim, "comrade:marta")!.Text);
        Assert.NotNull(Find(world.GetHero("marta"), "comrade:tim")); // вывод делают оба участника

        world.Day = 2;
        world.Log.Append(2, DayPhase.BaseLife, "quarrel", "Тим и Марта поссорились.", 6, -3, "tim", "marta");
        Reflection.Run(world, rules);
        Assert.Null(Find(tim, "comrade:marta"));
        Assert.Equal("С Мартой невозможно договориться", Find(tim, "rival:marta")!.Text);
    }

    [Fact]
    public void Memory_is_limited_and_the_weakest_beliefs_are_pushed_out()
    {
        var (world, rules) = NewGame();
        rules.Config.Memory.MaxBeliefs = 2;
        world.Day = 1;
        world.Log.Append(1, DayPhase.BaseLife, "socialized", "", 2, 1, "tim", "marta");
        world.Log.Append(1, DayPhase.BaseLife, "socialized", "", 2, 1, "tim", "pip");
        world.Log.Append(1, DayPhase.BaseLife, "socialized", "", 2, 1, "tim", "pip");
        world.Log.Append(1, DayPhase.BaseLife, "quarrel", "", 6, -3, "tim", "gron");
        Reflection.Run(world, rules);

        var ids = world.GetHero("tim").Beliefs.Select(b => b.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "comrade:pip", "rival:gron" }, ids);
    }

    [Fact]
    public void Old_grudges_against_the_master_lower_obedience_even_with_trust_restored()
    {
        var (world, rules) = NewGame();
        rules.Config.Obedience.Jitter = 0;
        var tim = world.GetHero("tim");
        int calm = Obedience.Evaluate(world, rules, tim, Party).Readiness;

        tim.Beliefs.Add(new Belief { Id = "master_greedy", AboutId = Ids.Master, Text = "Мастер забирает добычу себе", Strength = 80, Emotion = -3 });
        int bitter = Obedience.Evaluate(world, rules, tim, Party).Readiness;
        Assert.Equal(-rules.Config.Memory.MasterBeliefMax, bitter - calm);

        tim.Beliefs.Clear();
        tim.Beliefs.Add(new Belief { Id = "master_fair", AboutId = Ids.Master, Text = "Мастер делит добычу честно", Strength = 60, Emotion = 2 });
        Assert.True(Obedience.Evaluate(world, rules, tim, Party).Readiness > calm);
    }

    [Fact]
    public void A_belief_that_becomes_firm_is_noted_in_the_journal()
    {
        var (world, rules) = NewGame();
        LootDay(world, rules, keep: true);
        LootDay(world, rules, keep: true);
        Assert.Contains(world.Log.Events, e => e.Type == "belief_formed" && e.Actors.Contains("tim")
                                               && e.Summary.Contains("уверен") && e.Summary.Contains("Мастер забирает добычу себе"));
    }

    [Fact]
    public void Beliefs_rules_are_validated_with_clear_messages()
    {
        var catalog = new BeliefCatalog
        {
            Rules = new()
            {
                new BeliefRule { Id = "a", Event = "quarrel", Subject = "first", About = "master", Text = "С {otherIns} плохо", Gain = 10, Opposes = new() { "nope" } }
            }
        };
        var errors = catalog.Validate();
        Assert.Contains(errors, e => e.Contains("не о другом герое"));
        Assert.Contains(errors, e => e.Contains("неизвестное убеждение «nope»"));
    }
}

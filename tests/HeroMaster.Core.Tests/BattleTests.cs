using HeroMaster.Core.Battle;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Tests;

public class BattleTests
{
    private static GameRules Rules() => GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));

    private static (GameWorld world, GameRules rules, MissionDefinition mission) Setup(ulong seed = 42, int trust = 70)
    {
        var rules = Rules();
        var world = WorldFactory.Create(rules.Catalog, rules.Config, seed);
        foreach (var h in world.Heroes)
            world.TowardMaster(h.Id).Trust = trust;
        return (world, rules, rules.Mission(rules.Config.Expedition.MissionId));
    }

    private static readonly List<string> Novices = new() { "tim", "gron", "marta", "pip" };

    /// <summary>Сыграть много боёв одним составом: (смертей на бой, ранений на бой, доля вайпов).</summary>
    private static (double deaths, double injuries, double wipes) Simulate(List<string> team, string? forcedZone, int battles = 120)
    {
        int deaths = 0, injuries = 0, wipes = 0;
        for (ulong seed = 1; seed <= (ulong)battles; seed++)
        {
            var (world, rules, mission) = Setup(seed);
            var s = BattleEngine.Start(world, rules, mission, team);
            if (forcedZone != null)
                foreach (var h in s.Heroes) h.TargetZone = forcedZone;
            BattleEngine.RunToEnd(world, rules, s);
            deaths += s.Heroes.Count(h => h.Dead);
            injuries += s.Heroes.Count(h => h.Injured && h.Alive);
            if (s.Outcome == BattleOutcome.Defeat) wipes++;
        }
        return ((double)deaths / battles, (double)injuries / battles, (double)wipes / battles);
    }

    [Fact]
    public void Mission_data_is_valid_and_has_good_and_bad_positions()
    {
        var (_, rules, mission) = Setup();
        Assert.Empty(mission.Validate(rules.Monsters));
        Assert.Equal(2, mission.Zone("chapel").Width);
        Assert.True(Positioning.TrueValue(rules, mission, mission.Zone("chapel")) > Positioning.TrueValue(rules, mission, mission.Zone("square")));
        Assert.Equal(3, mission.Distance("north_gate", "chapel"));
        Assert.Equal("alley", mission.NextStep("square", "chapel"));
    }

    [Fact]
    public void Monster_counts_read_naturally_in_russian()
    {
        var goblin = Rules().Monsters.Get("goblin");
        Assert.Equal("1 гоблин", goblin.CountText(1));
        Assert.Equal("3 гоблина", goblin.CountText(3));
        Assert.Equal("5 гоблинов", goblin.CountText(5));
        Assert.Equal("12 гоблинов", goblin.CountText(12));
        Assert.Equal("21 гоблин", goblin.CountText(21));
        Assert.Equal("2 пещерных волка", Rules().Monsters.Get("wolf").CountText(2));
    }

    [Fact]
    public void Same_seed_gives_the_same_battle()
    {
        string Run()
        {
            var (world, rules, mission) = Setup(9);
            var s = BattleEngine.Start(world, rules, mission, Novices);
            BattleEngine.RunToEnd(world, rules, s);
            return string.Join("\n", s.Feed.Select(f => $"{f.Tick}:{f.Text}"));
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Position_decides_the_fight()
    {
        var open = Simulate(Novices, "square");
        var chapel = Simulate(Novices, "chapel");
        Assert.True(open.injuries > chapel.injuries + 0.5, $"Площадь: {open.injuries:0.00} ранений/бой, часовня: {chapel.injuries:0.00}");
        Assert.True(open.deaths >= chapel.deaths);
    }

    [Fact]
    public void Battles_are_dangerous_but_not_a_bloodbath()
    {
        // Новички сами выбирают позицию — ошибаются, но не гибнут пачками.
        var self = Simulate(Novices, null);
        Assert.True(self.deaths < 0.25, $"Смертей на бой: {self.deaths:0.00}");
        Assert.True(self.wipes < 0.05, $"Доля вайпов: {self.wipes:P0}");

        // Даже толпой на открытой площади: много ранений, но гибель — редкость.
        var open = Simulate(Novices, "square");
        Assert.True(open.deaths < 0.2, $"Смертей на бой на площади: {open.deaths:0.00}");
        Assert.True(open.injuries > 0.5, $"На площади должны быть ранения: {open.injuries:0.00}");
    }

    [Fact]
    public void Experienced_heroes_find_good_positions_more_often_than_novices()
    {
        int GoodPicks(int experience)
        {
            int good = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var (world, rules, mission) = Setup(seed);
                foreach (var h in world.Heroes) h.Experience = experience;
                var s = BattleEngine.Start(world, rules, mission, Novices);
                good += s.Heroes.Count(h => h.TargetZone is "chapel" or "ruins");
            }
            return good;
        }
        Assert.True(GoodPicks(300) > GoodPicks(0));
    }

    [Fact]
    public void Heroes_must_go_but_refusers_fight_back_when_attacked()
    {
        var (world, rules, mission) = Setup(trust: 0);
        var team = new List<string> { "tim", "gron", "vale" };
        world.GetHero("vale").State.Status = MoodStatus.Boycott;

        var s = BattleEngine.Start(world, rules, mission, team);
        Assert.Equal(team, s.Heroes.Select(h => h.Hero.Id).ToList()); // контракт: идут все
        Assert.Contains(s.Heroes, h => h.StartResponse == OrderResponse.Boycott);

        BattleEngine.RunToEnd(world, rules, s);

        // Каждый, на кого напали, начал отбиваться.
        foreach (var h in s.Heroes.Where(h => h.WasAttacked))
            Assert.Contains(s.Feed, f => f.Kind == "self_defense" && f.Actors.Contains(h.Hero.Id));
    }

    [Fact]
    public void Kills_loot_and_mvp_add_up()
    {
        var (world, rules, mission) = Setup(3);
        var s = BattleEngine.Start(world, rules, mission, new List<string> { "tim", "gron", "ari", "anselm" });
        BattleEngine.RunToEnd(world, rules, s);

        Assert.Equal(s.MonstersKilled, s.Heroes.Sum(h => h.Kills));
        Assert.Equal(s.Monsters.Where(m => m.Dead).Sum(m => m.Def.Loot), s.Loot);
        Assert.NotNull(s.Mvp);
        Assert.Equal(s.Heroes.Max(h => h.Score), s.Mvp!.Score);
        if (s.Outcome == BattleOutcome.Victory)
            Assert.All(s.Monsters, m => Assert.True(m.Dead));
    }

    [Fact]
    public void Healer_heals_and_archer_shoots()
    {
        int healed = 0, archerKills = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var (world, rules, mission) = Setup(seed);
            var s = BattleEngine.Start(world, rules, mission, new List<string> { "tim", "gron", "ari", "anselm" });
            foreach (var h in s.Heroes) h.TargetZone = "square";
            BattleEngine.RunToEnd(world, rules, s);
            healed += s.Heroes.Single(h => h.Hero.Id == "anselm").Healed;
            archerKills += s.Heroes.Single(h => h.Hero.Id == "ari").Kills;
        }
        Assert.True(healed > 0);
        Assert.True(archerKills > 0);
    }

    [Fact]
    public void Death_is_permanent_and_the_squad_grieves()
    {
        var (world, rules, mission) = Setup(5);
        rules.Config.Battle.HeroBaseHp = -15; // почти без здоровья — кто-то обязательно падёт
        rules.Config.Battle.BreatherHpPerTick = 0;
        var stressBefore = world.Heroes.ToDictionary(h => h.Id, h => h.State.Stress);

        var day = DayEngine.BeginDay(world, rules, new MasterDecisions { Team = Novices });
        BattleEngine.RunToEnd(world, rules, day.Battle!);
        var dead = day.Battle!.Heroes.Where(h => h.Dead).Select(h => h.Hero.Id).ToList();
        DayEngine.FinishDay(world, rules, day);

        Assert.NotEmpty(dead);
        Assert.All(dead, id => Assert.False(world.HasHero(id)));
        Assert.All(dead, id => Assert.Contains(world.Fallen, f => f.Id == id));
        Assert.Contains(world.Log.Events, e => e.Type == "hero_died");
        // Те, кто остался на базе, тоже скорбят.
        Assert.True(world.GetHero("anselm").State.Stress > stressBefore["anselm"]);
    }

    [Fact]
    public void Day_stops_for_a_live_battle_and_cannot_finish_before_it_ends()
    {
        var (world, rules, _) = Setup();
        var day = DayEngine.BeginDay(world, rules, new MasterDecisions { Team = Novices });
        Assert.NotNull(day.Battle);
        Assert.Throws<InvalidOperationException>(() => DayEngine.FinishDay(world, rules, day));

        while (!day.Battle!.IsOver)
            BattleEngine.Step(world, rules, day.Battle);
        var events = DayEngine.FinishDay(world, rules, day);

        Assert.Contains(events, e => e.Type == "expedition_result");
        Assert.Contains(events, e => e.Type == "mvp");
    }

    [Fact]
    public void Wounds_heal_on_base_but_stress_stays()
    {
        var (world, rules, mission) = Setup(2);
        var s = BattleEngine.Start(world, rules, mission, Novices);
        foreach (var h in s.Heroes) h.TargetZone = "square";
        BattleEngine.RunToEnd(world, rules, s);
        var injured = s.Heroes.Where(h => h.Injured && h.Alive).Select(h => h.Hero).ToList();
        int stressBefore = injured.Sum(h => h.State.Stress);
        BattleAftermath.Apply(world, rules, s);

        // В бою ранен — на базе здоров (никаких дней лечения), но стресс вырос.
        if (injured.Count > 0)
            Assert.True(injured.Sum(h => h.State.Stress) > stressBefore);
    }
}

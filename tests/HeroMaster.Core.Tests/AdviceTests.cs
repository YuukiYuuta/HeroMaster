using HeroMaster.Core.Battle;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Tests;

public class AdviceTests
{
    private static readonly List<string> Novices = new() { "tim", "gron", "marta", "pip" };

    private static (GameWorld world, GameRules rules, BattleState battle) StartBattle(ulong seed = 42, int trust = 80, List<string>? team = null)
    {
        var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
        var world = WorldFactory.Create(rules.Catalog, rules.Config, seed);
        foreach (var h in world.Heroes)
            world.TowardMaster(h.Id).Trust = trust;
        var battle = BattleEngine.Start(world, rules, rules.Mission(rules.Config.Expedition.MissionId), team ?? Novices);
        return (world, rules, battle);
    }

    private static void SetTechnique(Hero hero, string action, int strength) =>
        hero.Techniques.Add(new Technique { Id = $"defense:{action}", Situation = "defense", Action = action, Strength = strength });

    [Fact]
    public void Trusting_heroes_follow_advice()
    {
        var (world, rules, s) = StartBattle(trust: 95);
        foreach (var h in s.Heroes) h.TargetZone = "square";

        var errors = MasterAdvice.Give(world, rules, s, "chapel", null);

        Assert.Empty(errors);
        Assert.All(s.Heroes, h => Assert.Equal("chapel", h.TargetZone));
        Assert.All(s.Heroes, h => Assert.Contains("chapel", h.FollowedAdvice));
    }

    [Fact]
    public void Distrustful_and_boycotting_heroes_ignore_advice()
    {
        var (world, rules, s) = StartBattle(trust: 0, team: new List<string> { "tim", "gron", "vale" });
        var vale = s.Heroes.Single(h => h.Hero.Id == "vale");
        vale.Conduct = Conduct.Abandoned;
        foreach (var h in s.Heroes) h.TargetZone = "square";
        foreach (var h in s.Heroes.Where(h => h != vale)) h.Conduct = Conduct.Steady; // доверия нет, но драться готовы

        MasterAdvice.Give(world, rules, s, "chapel", null);

        Assert.All(s.Heroes, h => Assert.Equal("square", h.TargetZone));
        Assert.Contains(s.Feed, f => f.Kind == "advice_ignored" && f.Actors.Contains("vale") && f.Text.Contains("бойкот"));
    }

    [Fact]
    public void Hero_who_refuses_to_fight_goes_only_where_it_is_safer()
    {
        var (world, rules, s) = StartBattle(trust: 95);
        var pip = s.Heroes.Single(h => h.Hero.Id == "pip");
        pip.Conduct = Conduct.Passive;
        pip.TargetZone = "chapel";

        MasterAdvice.Give(world, rules, s, "square", "pip"); // в открытое поле — нет
        Assert.Equal("chapel", pip.TargetZone);

        pip.TargetZone = "square";
        MasterAdvice.Give(world, rules, s, "ruins", "pip"); // в укрытие — охотно
        Assert.Equal("ruins", pip.TargetZone);
    }

    [Fact]
    public void Spamming_advice_annoys_and_confuses_the_squad()
    {
        var (world, rules, s) = StartBattle(trust: 80);
        int before = world.TowardMaster("tim").Trust;

        for (int i = 0; i < 6; i++)
            MasterAdvice.Give(world, rules, s, "chapel", null);

        Assert.True(world.TowardMaster("tim").Trust < before);
        Assert.Contains(s.Feed, f => f.Kind == "advice_spam");
    }

    [Fact]
    public void Contradicting_yourself_is_called_out()
    {
        var (world, rules, s) = StartBattle(trust: 80);
        MasterAdvice.Give(world, rules, s, "chapel", null);
        MasterAdvice.Give(world, rules, s, "ruins", null);
        Assert.Contains(s.Feed, f => f.Kind == "advice_spam" && f.Text.Contains("противоречит"));
    }

    [Fact]
    public void Experienced_hero_argues_when_advice_goes_against_his_lessons()
    {
        var (world, rules, s) = StartBattle(trust: 100);
        var gron = s.Heroes.Single(h => h.Hero.Id == "gron");
        SetTechnique(gron.Hero, Techniques.Narrow, 100);
        SetTechnique(gron.Hero, Techniques.Cover, 100);
        gron.TargetZone = "chapel";

        MasterAdvice.Give(world, rules, s, "square", "gron");

        Assert.Equal("chapel", gron.TargetZone);
        Assert.Contains(s.Feed, f => f.Kind == "advice_disagree" && f.Actors.Contains("gron"));
    }

    [Fact]
    public void Advice_cannot_send_heroes_into_the_enemy_spawn()
    {
        var (world, rules, s) = StartBattle();
        Assert.NotEmpty(MasterAdvice.Give(world, rules, s, "north_gate", null));
    }

    [Fact]
    public void Good_advice_is_learned_slowly_like_people_learn()
    {
        var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
        var world = WorldFactory.Create(rules.Catalog, rules.Config, 11);
        foreach (var h in world.Heroes) world.TowardMaster(h.Id).Trust = 95;
        var tim = world.GetHero("tim");

        int battlesToLearn = 0;
        for (int i = 1; i <= 15 && Techniques.Strength(tim, Techniques.Narrow) < rules.Config.Learning.LearnedThreshold; i++)
        {
            var team = new[] { "tim", "gron", "marta", "pip", "anselm", "ari", "vale" }.Where(world.HasHero).Take(4).ToList();
            var day = DayEngine.BeginDay(world, rules, new MasterDecisions
            {
                Team = team,
                LootShares = world.Master.PendingLoot is { } p ? p.Participants.ToDictionary(id => id, _ => p.Amount / p.Participants.Count) : null
            });
            MasterAdvice.Give(world, rules, day.Battle!, "chapel", null);
            BattleEngine.RunToEnd(world, rules, day.Battle!);
            DayEngine.FinishDay(world, rules, day);
            foreach (var h in world.Heroes) world.TowardMaster(h.Id).Trust = 95; // доверие не мешает эксперименту
            battlesToLearn = i;
        }

        // Не с первого раза, но и не вечность.
        Assert.InRange(battlesToLearn, 3, 10);
        Assert.Contains(world.Log.Events, e => e.Type == "technique_learned" && e.Actors.Contains("tim"));
    }

    [Fact]
    public void Learned_lessons_change_positions_without_any_advice()
    {
        int GoodPicks(int strength)
        {
            int good = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
                var world = WorldFactory.Create(rules.Catalog, rules.Config, seed);
                foreach (var h in world.Heroes)
                {
                    world.TowardMaster(h.Id).Trust = 80;
                    SetTechnique(h, Techniques.Narrow, strength);
                    SetTechnique(h, Techniques.Cover, strength);
                }
                var s = BattleEngine.Start(world, rules, rules.Mission("defense_courtyard"), Novices);
                good += s.Heroes.Count(h => h.TargetZone is "chapel" or "alley");
            }
            return good;
        }
        Assert.True(GoodPicks(100) > GoodPicks(0) + 20);
    }

    [Fact]
    public void Skills_fade_without_practice()
    {
        var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
        var world = WorldFactory.Create(rules.Catalog, rules.Config, 1);
        var tim = world.GetHero("tim");
        SetTechnique(tim, Techniques.Narrow, 70);

        for (int i = 0; i < 10; i++)
            DayEngine.RunDay(world, rules, new MasterDecisions());

        Assert.Equal(70 - 10 * rules.Config.Learning.DecayPerIdleDay, Techniques.Strength(tim, Techniques.Narrow));
    }

    [Fact]
    public void Heroes_teach_each_other_but_not_their_rivals()
    {
        var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
        rules.Config.Learning.TeachChancePercent = 100;
        var world = WorldFactory.Create(rules.Catalog, rules.Config, 1);
        var gron = world.GetHero("gron");
        var pip = world.GetHero("pip");
        SetTechnique(gron, Techniques.Narrow, 80);

        Assert.True(Techniques.TryTeach(world, rules, gron, pip));
        Assert.True(Techniques.Strength(pip, Techniques.Narrow) > 0);
        // Пип уважает Грона (70) — учится быстрее базовой порции.
        Assert.True(Techniques.Strength(pip, Techniques.Narrow) >= rules.Config.Learning.TeachingStrength * 80 / 100);

        var vale = world.GetHero("vale");
        var anselm = world.GetHero("anselm");
        SetTechnique(anselm, Techniques.Cover, 90);
        world.GetRelationship("vale", "anselm").Rivalry = 90;
        Assert.False(Techniques.TryTeach(world, rules, anselm, vale));
        Assert.Equal(0, Techniques.Strength(vale, Techniques.Cover));
    }
}

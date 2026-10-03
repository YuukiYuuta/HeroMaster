using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Random;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Tests;

public class BaseLifeTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static GameRules Rules() => GameJson.LoadRules(DataDir);

    private static (GameWorld world, GameRules rules) NewGame(ulong seed = 42)
    {
        var rules = Rules();
        return (WorldFactory.Create(rules.Catalog, rules.Config, seed), rules);
    }

    private static void SetTrust(GameWorld world, string heroId, int trust) => world.TowardMaster(heroId).Trust = trust;

    /// <summary>Простая политика мастера для прогонов: вылазка каждый второй день, добыча делится поровну.</summary>
    private static MasterDecisions SimplePolicy(GameWorld world)
    {
        var d = new MasterDecisions();
        if (world.Master.PendingLoot is { } pool)
        {
            int each = pool.Amount / pool.Participants.Count;
            d.LootShares = pool.Participants.ToDictionary(id => id, _ => each);
        }
        // Павшие выбывают — в команду берём живых, предпочитая привычный состав.
        var team = new[] { "tim", "gron", "ari", "vale", "marta", "pip", "anselm" }.Where(world.HasHero).Take(4).ToList();
        if (world.Day % 2 == 0 && team.Count >= 3)
            d.Team = team;
        return d;
    }

    [Fact]
    public void Same_seed_and_same_decisions_give_identical_days()
    {
        var (a, rules) = NewGame(7);
        var (b, _) = NewGame(7);
        for (int i = 0; i < 15; i++)
        {
            DayEngine.RunDay(a, rules, SimplePolicy(a));
            DayEngine.RunDay(b, rules, SimplePolicy(b));
        }
        Assert.Equal(GameJson.Serialize(a), GameJson.Serialize(b));
    }

    [Fact]
    public void Trusting_hero_obeys_and_distrustful_refuses()
    {
        var (world, rules) = NewGame();
        var team = new List<string> { "gron", "vale" };

        SetTrust(world, "gron", 95);
        SetTrust(world, "vale", 5);

        Assert.True(Obedience.Evaluate(world, rules, world.GetHero("gron"), team).Fights);
        Assert.Equal(OrderResponse.Refuses, Obedience.Evaluate(world, rules, world.GetHero("vale"), team).Response);
    }

    [Fact]
    public void Boycotting_hero_ignores_orders()
    {
        var (world, rules) = NewGame();
        var ari = world.GetHero("ari");
        ari.State.Status = MoodStatus.Boycott;
        SetTrust(world, "ari", 100);
        Assert.Equal(OrderResponse.Boycott, Obedience.Evaluate(world, rules, ari, new List<string> { "ari" }).Response);
    }

    [Fact]
    public void Boycott_comes_only_after_nights_on_edge_with_warnings()
    {
        var (world, rules) = NewGame();
        var ari = world.GetHero("ari");
        SetTrust(world, "ari", 5);

        var statuses = new List<MoodStatus>();
        for (int i = 0; i < 3; i++)
        {
            DayEngine.RunDay(world, rules, new MasterDecisions());
            statuses.Add(ari.State.Status);
        }

        // Две ночи «на грани» (время заметить и исправить), на третью — бойкот.
        Assert.Equal(new[] { MoodStatus.OnEdge, MoodStatus.OnEdge, MoodStatus.Boycott }, statuses);
        Assert.Contains(world.Log.Events, e => e.Type == "status_warning" && e.Actors.Contains("ari"));
    }

    [Fact]
    public void Distrustful_hero_starts_discontented_without_any_event()
    {
        var (world, _) = NewGame();
        Assert.Equal(MoodStatus.Discontented, world.GetHero("vale").State.Status); // доверие 25
        Assert.Equal(MoodStatus.Normal, world.GetHero("anselm").State.Status);     // доверие 60
        Assert.DoesNotContain(world.Log.Events, e => e.Type == "status_changed");
    }

    [Fact]
    public void Status_recovers_only_with_a_margin_of_trust()
    {
        var (world, rules) = NewGame();
        var tim = world.GetHero("tim");
        tim.State.Status = MoodStatus.Discontented;
        var t = rules.Config.Trust;

        SetTrust(world, "tim", t.DiscontentedBelow + t.RecoverMargin - 2); // чуть выше порога, но без запаса
        NightPhase.UpdateStatus(world, rules, tim);
        Assert.Equal(MoodStatus.Discontented, tim.State.Status);

        SetTrust(world, "tim", t.DiscontentedBelow + t.RecoverMargin);
        NightPhase.UpdateStatus(world, rules, tim);
        Assert.Equal(MoodStatus.Normal, tim.State.Status);
    }

    [Fact]
    public void Unfair_loot_share_hurts_trust_and_fair_share_does_not()
    {
        var (world, rules) = NewGame();
        world.Master.PendingLoot = new LootPool { Amount = 100, Participants = new List<string> { "tim", "gron", "ari", "marta" } };
        int ariBefore = world.TowardMaster("ari").Trust;
        int timBefore = world.TowardMaster("tim").Trust;

        var d = new MasterDecisions { LootShares = new Dictionary<string, int> { ["tim"] = 70, ["gron"] = 10, ["ari"] = 10, ["marta"] = 10 } };
        MasterPhase.Run(world, rules, d);

        Assert.True(world.TowardMaster("ari").Trust < ariBefore);
        Assert.True(world.TowardMaster("tim").Trust >= timBefore);
        Assert.Equal(70, world.GetHero("tim").Purse);
        Assert.Null(world.Master.PendingLoot);
    }

    [Fact]
    public void Keeping_all_loot_angers_participants_and_fills_the_treasury()
    {
        var (world, rules) = NewGame();
        world.Master.PendingLoot = new LootPool { Amount = 100, Participants = new List<string> { "tim", "gron" } };
        int goldBefore = world.Master.Gold;
        int timBefore = world.TowardMaster("tim").Trust;

        MasterPhase.Run(world, rules, new MasterDecisions { KeepLoot = true });

        Assert.Equal(goldBefore + 100, world.Master.Gold);
        Assert.Equal(timBefore - rules.Config.Loot.KeptAllTrustLoss, world.TowardMaster("tim").Trust);
        Assert.Contains(world.Log.Events, e => e.Type == "loot_kept");
    }

    [Fact]
    public void Gifts_raise_trust_but_frequent_gifts_are_worth_less()
    {
        var (world, rules) = NewGame();
        world.Day = 1;
        world.Master.Gold = 1000;
        var gift = new MasterDecisions { Gifts = new List<GiftOrder> { new() { HeroId = "ari", KindId = "trinket" } } };

        int t0 = world.TowardMaster("ari").Trust;
        MasterPhase.Run(world, rules, gift);
        int first = world.TowardMaster("ari").Trust - t0;

        int t1 = world.TowardMaster("ari").Trust;
        MasterPhase.Run(world, rules, gift);
        int second = world.TowardMaster("ari").Trust - t1;

        Assert.True(first > 0);
        Assert.True(second < first);
    }

    [Fact]
    public void Exhausted_hero_prefers_rest()
    {
        var (world, rules) = NewGame();
        var gron = world.GetHero("gron");
        gron.State.Fatigue = 95;
        var scores = BaseLifePhase.Scores(world, rules, gron, new Dictionary<string, Activity>());
        int jitter = rules.Config.Activities.Jitter;
        Assert.All(scores.Where(s => s.Key != Activity.Rest), s => Assert.True(scores[Activity.Rest] - jitter > s.Value + jitter));
    }

    [Fact]
    public void Hint_works_only_when_hero_trusts_the_master()
    {
        var (world, rules) = NewGame();
        var pip = world.GetHero("pip");
        var none = new Dictionary<string, Activity>();
        var hint = new Dictionary<string, Activity> { ["pip"] = Activity.Train };

        SetTrust(world, "pip", 90);
        Assert.True(BaseLifePhase.Scores(world, rules, pip, hint)[Activity.Train] > BaseLifePhase.Scores(world, rules, pip, none)[Activity.Train]);

        SetTrust(world, "pip", 10);
        Assert.Equal(BaseLifePhase.Scores(world, rules, pip, none)[Activity.Train], BaseLifePhase.Scores(world, rules, pip, hint)[Activity.Train]);
    }

    [Fact]
    public void Heroes_with_opposing_values_quarrel_over_time()
    {
        var (world, rules) = NewGame(3);
        for (int i = 0; i < 60; i++)
            DayEngine.RunDay(world, rules, new MasterDecisions());

        Assert.Contains(world.Log.Events, e => e.Type == "quarrel" && e.Actors.Contains("vale") && e.Actors.Contains("anselm"));
    }

    [Fact]
    public void Strength_matters_far_more_than_zeal()
    {
        var (world, rules) = NewGame();
        var weakButEager = Combat.EffectivePower(rules, world.GetHero("pip"), Conduct.AllIn);      // 1★ по полной
        var strongButCalm = Combat.EffectivePower(rules, world.GetHero("vale"), Conduct.HalfHearted); // 2★ вполсилы
        Assert.True(strongButCalm > weakButEager);

        // Разница между «по полной» и «вполсилы» у одного героя — небольшая.
        var pip = world.GetHero("pip");
        int allIn = Combat.EffectivePower(rules, pip, Conduct.AllIn);
        int half = Combat.EffectivePower(rules, pip, Conduct.HalfHearted);
        Assert.True(allIn - half <= allIn / 4);
    }

    [Fact]
    public void Seeing_a_comrade_injured_can_break_morale_but_the_brave_hold()
    {
        var (world, rules) = NewGame();
        var injured = world.GetHero("tim");

        rules.Config.Combat.WitnessMoraleChance = 100;
        rules.Config.Combat.WitnessCouragePercent = 0;
        var pip = world.GetHero("pip"); // смелость 25 — трус
        Assert.Equal(Conduct.Steady, Combat.WitnessInjury(world, rules, pip, injured, Conduct.AllIn));
        Assert.Equal(Conduct.Passive, Combat.WitnessInjury(world, rules, pip, injured, Conduct.HalfHearted)); // оцепенел

        rules.Config.Combat.WitnessMoraleChance = 0;
        rules.Config.Combat.WitnessAffectionPercent = 0;
        Assert.Equal(Conduct.AllIn, Combat.WitnessInjury(world, rules, world.GetHero("gron"), injured, Conduct.AllIn));
    }

    [Fact]
    public void Resolve_grows_until_the_hero_finds_purpose()
    {
        var (world, rules) = NewGame();
        var tim = world.GetHero("tim");
        int start = tim.State.Resolve;
        Assert.True(start < rules.Config.Resolve.PurposeThreshold, "Призванный в Башню сначала растерян.");

        for (int i = 0; i < 40; i++)
            DayEngine.RunDay(world, rules, new MasterDecisions());

        Assert.True(tim.State.Resolve > start);
        Assert.True(tim.State.FoundPurpose);
        Assert.Single(world.Log.Events, e => e.Type == "found_purpose" && e.Actors.Contains("tim"));
    }

    [Fact]
    public void Resolve_not_ambition_drives_training()
    {
        var (world, rules) = NewGame();
        var marta = world.GetHero("marta");
        var none = new Dictionary<string, Activity>();

        marta.State.Resolve = 10;
        int lost = BaseLifePhase.Scores(world, rules, marta, none)[Activity.Train];
        marta.State.Resolve = 90;
        int determined = BaseLifePhase.Scores(world, rules, marta, none)[Activity.Train];

        Assert.True(determined - lost >= 25, "Решимость должна заметно менять желание тренироваться.");
    }

    [Fact]
    public void Pragmatic_rogue_adapts_faster_than_the_priest()
    {
        var (world, _) = NewGame();
        Assert.True(world.GetHero("vale").State.Resolve > world.GetHero("anselm").State.Resolve);
    }

    [Fact]
    public void Successful_expedition_brings_loot_that_must_be_decided()
    {
        var (world, rules) = NewGame();
        foreach (var h in world.Heroes)
            SetTrust(world, h.Id, 90);

        DayEngine.RunDay(world, rules, new MasterDecisions { Team = new List<string> { "tim", "gron", "ari", "vale" } });
        Assert.NotNull(world.Master.PendingLoot);

        var errors = new MasterDecisions().Validate(world, rules);
        Assert.Contains(errors, e => e.Contains("добыча"));
    }

    [Fact]
    public void Invalid_decisions_are_rejected_with_clear_messages()
    {
        var (world, rules) = NewGame();
        world.Master.Gold = 0;
        var d = new MasterDecisions
        {
            Team = new List<string> { "nobody" },
            Gifts = new List<GiftOrder> { new() { HeroId = "ari", KindId = "gear" } },
            Hints = new Dictionary<string, Activity> { ["pip"] = Activity.Brood }
        };
        var errors = d.Validate(world, rules);
        Assert.Contains(errors, e => e.Contains("nobody"));
        Assert.Contains(errors, e => e.Contains("золота"));
        Assert.Contains(errors, e => e.Contains("обиду"));
        Assert.Throws<InvalidOperationException>(() => DayEngine.RunDay(world, rules, d));
    }

    [Fact]
    public void Two_hundred_days_of_random_play_stay_consistent()
    {
        var (world, rules) = NewGame(99);
        var policyRng = new Pcg32(2024);
        var ids = world.Heroes.Select(h => h.Id).ToList();
        var kinds = rules.Config.Gifts.Kinds;

        for (int day = 0; day < 200; day++)
        {
            ids = world.Heroes.Select(h => h.Id).ToList(); // павшие выбывают
            if (ids.Count == 0)
                break;
            var d = new MasterDecisions();
            if (world.Master.PendingLoot is { } pool)
            {
                if (policyRng.Chance(20))
                    d.KeepLoot = true;
                else
                    d.LootShares = pool.Participants.ToDictionary(id => id, _ => pool.Amount / pool.Participants.Count / (policyRng.Chance(30) ? 3 : 1));
            }
            if (policyRng.Chance(50))
            {
                var team = ids.Where(_ => policyRng.Chance(60)).Take(5).ToList();
                if (team.Count >= rules.Config.Expedition.MinMembers)
                    d.Team = team;
            }
            var kind = kinds[policyRng.Next(kinds.Count)];
            if (policyRng.Chance(30) && world.Master.Gold >= kind.Cost)
                d.Gifts.Add(new GiftOrder { HeroId = ids[policyRng.Next(ids.Count)], KindId = kind.Id });
            if (policyRng.Chance(40))
                d.Hints[ids[policyRng.Next(ids.Count)]] = (Activity)policyRng.Next(4);

            DayEngine.RunDay(world, rules, d);
        }

        Assert.True(world.Day > 0);
        Assert.True(world.Master.Gold >= 0);
        Assert.All(world.Relationships, r =>
        {
            Assert.InRange(r.Trust, 0, 100);
            Assert.InRange(r.Affection, 0, 100);
            Assert.InRange(r.Rivalry, 0, 100);
        });
        Assert.All(world.Heroes, h =>
        {
            Assert.InRange(h.State.Fatigue, 0, 100);
            Assert.InRange(h.State.Stress, 0, 100);
        });
    }
}

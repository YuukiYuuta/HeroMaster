using HeroMaster.Core.Config;
using HeroMaster.Core.Content;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Random;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Tests;

public class FoundationTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static HeroCatalog Catalog() => GameJson.LoadCatalog(Path.Combine(DataDir, "heroes.json"));
    private static BalanceConfig Config() => GameJson.LoadConfig(Path.Combine(DataDir, "balance.json"));

    [Fact]
    public void Rng_same_seed_gives_same_sequence()
    {
        var a = new Pcg32(42);
        var b = new Pcg32(42);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Rng_restored_from_state_continues_identically()
    {
        var original = new Pcg32(7);
        for (int i = 0; i < 10; i++) original.Next(100);

        var restored = new Pcg32 { State = original.State, Increment = original.Increment };
        for (int i = 0; i < 100; i++)
            Assert.Equal(original.Next(1000), restored.Next(1000));
    }

    [Fact]
    public void Rng_range_stays_in_bounds()
    {
        var rng = new Pcg32(1);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.Range(-5, 5);
            Assert.InRange(v, -5, 5);
        }
    }

    [Fact]
    public void Data_files_load_and_validate()
    {
        var catalog = Catalog();
        var config = Config();

        Assert.Empty(catalog.Validate());
        Assert.Empty(config.Validate());
        Assert.Equal(7, catalog.Heroes.Count);
        Assert.True(catalog.Heroes.Count > config.Team.MaxSize, "Нужен запас героев сверх одной команды.");
    }

    [Fact]
    public void Catalog_validation_catches_mistakes()
    {
        var catalog = Catalog();
        catalog.Heroes[0].Traits.Courage = 150;
        catalog.Heroes[1].Values.Add("unknown_value");
        catalog.RelationshipOverrides.Add(new RelationshipOverride { From = "tim", To = "nobody" });
        catalog.Heroes[2].Stars = 7;

        var errors = catalog.Validate();
        Assert.Contains(errors, e => e.Contains("courage"));
        Assert.Contains(errors, e => e.Contains("unknown_value"));
        Assert.Contains(errors, e => e.Contains("nobody"));
        Assert.Contains(errors, e => e.Contains("stars = 7"));
    }

    [Fact]
    public void Same_seed_creates_identical_world()
    {
        var a = WorldFactory.Create(Catalog(), Config(), 123);
        var b = WorldFactory.Create(Catalog(), Config(), 123);
        Assert.Equal(GameJson.Serialize(a), GameJson.Serialize(b));
    }

    [Fact]
    public void Different_seeds_create_different_worlds()
    {
        var a = WorldFactory.Create(Catalog(), Config(), 1);
        var b = WorldFactory.Create(Catalog(), Config(), 2);
        Assert.NotEqual(GameJson.Serialize(a.Relationships), GameJson.Serialize(b.Relationships));
    }

    [Fact]
    public void World_has_every_relationship_and_explicit_overrides()
    {
        var world = WorldFactory.Create(Catalog(), Config(), 5);
        int n = world.Heroes.Count;

        // Каждый герой к каждому другому + каждый герой к мастеру.
        Assert.Equal(n * (n - 1) + n, world.Relationships.Count);
        Assert.Equal(70, world.GetRelationship("pip", "gron").Respect);
        Assert.Equal(25, world.TowardMaster("vale").Trust);
        Assert.All(world.Relationships, r => Assert.InRange(r.Trust, 0, 100));
    }

    [Fact]
    public void Most_starting_heroes_are_ordinary_people()
    {
        var heroes = Catalog().Heroes;
        Assert.True(heroes.Count(h => h.Stars == 1) > heroes.Count / 2, "Большинство героев должны быть 1★.");
        Assert.All(heroes, h => Assert.InRange(h.Stars, 1, 6));
    }

    [Fact]
    public void Ordinary_people_have_a_realistic_star_cap()
    {
        var heroes = Catalog().Heroes;
        Assert.All(heroes, h => Assert.InRange(h.MaxStars, h.Stars, 6));
        // Серая масса (1★) выше 3★ не поднимается.
        Assert.All(heroes.Where(h => h.Stars == 1), h => Assert.True(h.MaxStars <= 3, $"{h.Id}: потолок {h.MaxStars}★"));

        var catalog = Catalog();
        catalog.Heroes[0].MaxStars = 0;
        Assert.Contains(catalog.Validate(), e => e.Contains("maxStars"));
    }

    [Fact]
    public void Opposing_values_start_with_distrust_and_rivalry()
    {
        var catalog = Catalog();
        var config = Config();
        var world = WorldFactory.Create(catalog, config, 11);

        // Вейл: «Цель оправдывает средства». Ансельм: «Честь превыше выгоды» и «Не убиваю безоружных».
        Assert.Equal(2, catalog.ValueConflicts(world.GetHero("vale").Values, world.GetHero("anselm").Values));

        var valeToAnselm = world.GetRelationship("vale", "anselm");
        var def = catalog.DefaultRelationship;
        int jitter = config.World.RelationshipJitter;
        Assert.True(valeToAnselm.Trust <= def.Trust!.Value + jitter - 2 * config.World.OpposingValuesTrustPenalty);
        Assert.Equal(2 * config.World.OpposingValuesRivalry, valeToAnselm.Rivalry);

        // У Тима и Марты противоположных ценностей нет — соперничества тоже.
        Assert.Equal(0, world.GetRelationship("tim", "marta").Rivalry);
    }

    [Fact]
    public void Star_table_covers_one_to_six_and_rates_sum_to_100_percent()
    {
        var config = Config();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, config.Stars.Select(t => t.Stars).OrderBy(s => s));
        Assert.Equal(1_000_000, config.Stars.Sum(t => t.SummonRatePerMillion));

        config.Stars[0].SummonRatePerMillion += 1;
        Assert.Contains(config.Validate(), e => e.Contains("1000000"));
    }

    [Fact]
    public void Rarity_matches_the_agreed_scale()
    {
        var rate = Config().Stars.ToDictionary(t => t.Stars, t => t.SummonRatePerMillion);

        Assert.True(rate[1] > 900_000, "1★ — основная масса.");
        Assert.InRange(rate[2], 32_000, 159_800);   // между фиолетовым (~16%) и розовым (~3,2%) в CS:GO
        Assert.InRange(rate[3], 3_000, 10_000);     // как красное (~0,64%)
        Assert.InRange(rate[4], 1_000, 5_000);      // как нож или перчатки (~0,26%)
        Assert.InRange(rate[5], 1, 200);            // как очень дорогой нож
        Assert.Equal(0, rate[6]);                   // 6★ не выпадает при призыве
    }

    [Fact]
    public void Save_and_load_round_trip_keeps_everything()
    {
        var world = WorldFactory.Create(Catalog(), Config(), 99);
        world.Log.Append(1, DayPhase.BaseLife, "test", "Проверка", 3, -1, "bran");
        world.GetHero("gron").Techniques.Add(new Technique { Id = "t1", Situation = "narrow_passage", Action = "hold_formation", Strength = 30 });

        var path = Path.Combine(Path.GetTempPath(), $"hm-test-{Guid.NewGuid():N}.json");
        try
        {
            GameJson.SaveWorld(world, path);
            var loaded = GameJson.LoadWorld(path);
            Assert.Equal(GameJson.Serialize(world), GameJson.Serialize(loaded));

            // После загрузки случайность продолжается так же, как без сохранения.
            Assert.Equal(world.Rng.Next(1_000_000), loaded.Rng.Next(1_000_000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Event_log_assigns_sequential_ids_and_rejects_bad_values()
    {
        var log = new EventLog();
        var first = log.Append(0, DayPhase.MorningReport, "a", "", 1, 0);
        var second = log.Append(0, DayPhase.MorningReport, "b", "", 10, 5);
        Assert.Equal(first.Id + 1, second.Id);

        Assert.Throws<ArgumentOutOfRangeException>(() => log.Append(0, DayPhase.MorningReport, "c", "", 11, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Append(0, DayPhase.MorningReport, "c", "", 5, 6));
    }

    [Fact]
    public void Config_validation_catches_wrong_threshold_order()
    {
        var config = Config();
        config.Trust.BoycottBelow = 50;
        Assert.NotEmpty(config.Validate());
    }
}

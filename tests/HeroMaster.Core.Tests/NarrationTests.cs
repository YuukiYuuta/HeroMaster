using HeroMaster.Core.Narration;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Tests;

public class NarrationTests
{
    private static (GameWorld world, GameRules rules) NewGame(ulong seed = 42)
    {
        var rules = GameJson.LoadRules(Path.Combine(AppContext.BaseDirectory, "data"));
        return (WorldFactory.Create(rules.Catalog, rules.Config, seed), rules);
    }

    /// <summary>Несколько дней обычной игры: вылазки через день, добыча делится поровну, иногда остаётся мастеру.</summary>
    private static void Play(GameWorld world, GameRules rules, int days)
    {
        for (int d = 0; d < days; d++)
        {
            var decisions = new MasterDecisions();
            if (world.Master.PendingLoot is { } p)
            {
                if (world.Day % 4 == 3) decisions.KeepLoot = true;
                else decisions.LootShares = p.Participants.ToDictionary(id => id, _ => p.Amount / p.Participants.Count);
            }
            if (world.Day % 2 == 0 && world.Heroes.Count >= 4)
                decisions.Team = world.Heroes.Take(4).Select(h => h.Id).ToList();
            DayEngine.RunDay(world, rules, decisions);
        }
    }

    [Fact]
    public void Every_hero_writes_a_diary_entry_each_day()
    {
        var (world, rules) = NewGame();
        Play(world, rules, 3);
        foreach (var h in world.Heroes)
        {
            Assert.Equal(new[] { 1, 2, 3 }, h.Diary.Select(d => d.Day));
            Assert.All(h.Diary, d => Assert.False(string.IsNullOrWhiteSpace(d.Text)));
            Assert.All(h.Diary, d => Assert.Equal(TextSource.Template, d.Source));
        }
    }

    [Fact]
    public void Master_gets_a_morning_summary_every_day_and_a_battle_report_after_a_raid()
    {
        var (world, rules) = NewGame();
        Play(world, rules, 2); // день 1 — вылазка, день 2 — нет

        Assert.Contains(world.Chronicle, r => r.Day == 1 && r.Kind == DayReport.Battle && r.Text.Contains("врагов убито"));
        Assert.Contains(world.Chronicle, r => r.Day == 1 && r.Kind == DayReport.Morning && r.Text.Contains("отчёте о бое"));
        Assert.DoesNotContain(world.Chronicle, r => r.Day == 2 && r.Kind == DayReport.Battle);
        Assert.Contains(world.Chronicle, r => r.Day == 2 && r.Kind == DayReport.Morning);
    }

    [Fact]
    public void Diaries_speak_in_first_person_with_the_right_gender_and_no_leftover_markers()
    {
        var (world, rules) = NewGame(7);
        Play(world, rules, 30);

        var texts = world.Heroes.SelectMany(h => h.Diary.Select(d => d.Text))
            .Concat(world.Chronicle.Select(r => r.Text))
            .Concat(world.Heroes.SelectMany(h => h.Beliefs.Select(b => b.Text)));
        Assert.All(texts, t => Assert.DoesNotContain("{", t));

        var marta = world.GetHero("marta");
        Assert.DoesNotContain(marta.Diary, d => d.Text.Contains("Тренировался") || d.Text.Contains("Отдыхал —"));
    }

    [Fact]
    public void Remembered_feelings_show_up_in_the_diary()
    {
        var (world, rules) = NewGame();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            world.Master.PendingLoot = new LootPool { Amount = 100, FromDay = world.Day, Participants = new() { "tim", "gron", "marta", "pip" } };
            DayEngine.RunDay(world, rules, new MasterDecisions { KeepLoot = true });
        }
        var last = world.GetHero("gron").Diary.Last().Text;
        Assert.Contains("Мастер забрал всю добычу себе", last);
        Assert.Contains("мастер забирает добычу себе", last);
    }

    [Fact]
    public void Narration_never_changes_the_course_of_the_game()
    {
        var (a, rules) = NewGame(5);
        var (b, _) = NewGame(5);
        Play(a, rules, 10);
        Play(b, rules, 10);

        // Рассказчик не трогает генератор: тексты можно переписать как угодно — партия пойдёт так же.
        var rngBefore = GameJson.Serialize(a.Rng);
        var events = a.Log.ForDay(a.Day).ToList();
        TemplateNarrator.WriteDay(a, rules, events);
        Assert.Equal(rngBefore, GameJson.Serialize(a.Rng));

        foreach (var h in b.Heroes)
            foreach (var d in h.Diary)
                d.Text = "переписано";
        Play(a, rules, 10);
        Play(b, rules, 10);
        Assert.Equal(GameJson.Serialize(a.Log), GameJson.Serialize(b.Log));
        Assert.Equal(GameJson.Serialize(a.Rng), GameJson.Serialize(b.Rng));
    }

    [Fact]
    public void Diary_beliefs_and_reports_survive_save_and_load()
    {
        var (world, rules) = NewGame();
        Play(world, rules, 4);
        var path = Path.Combine(Path.GetTempPath(), $"hm_{Guid.NewGuid():N}.json");
        try
        {
            GameJson.SaveWorld(world, path);
            var loaded = GameJson.LoadWorld(path);
            Assert.Equal(GameJson.Serialize(world), GameJson.Serialize(loaded));
            Assert.NotEmpty(loaded.Chronicle);
            Assert.NotEmpty(loaded.GetHero("tim").Diary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Diary_and_reports_keep_only_recent_days()
    {
        var (world, rules) = NewGame();
        rules.Config.Memory.MaxDiaryEntries = 5;
        rules.Config.Memory.MaxReports = 4;
        Play(world, rules, 9);
        Assert.All(world.Heroes, h => Assert.Equal(5, h.Diary.Count));
        Assert.Equal(4, world.Chronicle.Count);
        Assert.Equal(world.Day, world.Chronicle.Last().Day);
    }
}

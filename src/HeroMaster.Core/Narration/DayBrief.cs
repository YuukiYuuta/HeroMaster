using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Memory;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Narration
{
    /// <summary>
    /// Всё, что нужно языковой модели, чтобы переписать тексты дня живее: кто герои, что с ними
    /// случилось, что они думают, и черновики из шаблонов. Только факты — модель ничего не решает.
    /// </summary>
    public sealed class DayBrief
    {
        public int Day { get; set; }
        public List<HeroBrief> Heroes { get; set; } = new();
        /// <summary>Главные события дня для утренней сводки мастеру.</summary>
        public string MorningDraft { get; set; } = "";
        /// <summary>Черновик отчёта о бое; null — боя не было.</summary>
        public string? BattleDraft { get; set; }
        public string? BattleTitle { get; set; }

        private const int RecentDiaryEntries = 2;
        private const int BeliefsShown = 6;

        public static DayBrief Build(GameWorld world, GameRules rules)
        {
            int day = world.Day;
            var events = world.Log.ForDay(day).ToList();
            var brief = new DayBrief { Day = day };

            var morning = world.Chronicle.LastOrDefault(r => r.Day == day && r.Kind == DayReport.Morning);
            var battle = world.Chronicle.LastOrDefault(r => r.Day == day && r.Kind == DayReport.Battle);
            brief.MorningDraft = morning?.Text ?? "";
            brief.BattleDraft = battle?.Text;
            brief.BattleTitle = battle?.Title;

            var valueText = rules.Catalog.ValueCatalog.ToDictionary(v => v.Id, v => v.Text);
            foreach (var h in world.Heroes)
            {
                var today = h.Diary.LastOrDefault(d => d.Day == day);
                brief.Heroes.Add(new HeroBrief
                {
                    Id = h.Id,
                    Name = h.Name,
                    Gender = h.Gender == Gender.Female ? "женщина" : "мужчина",
                    Stars = h.Stars,
                    Profession = h.Profession,
                    Bio = h.Bio,
                    Dream = h.Dream,
                    Values = h.Values.Select(v => valueText.TryGetValue(v, out var t) ? t : v).ToList(),
                    Character = Character(h.Traits),
                    Mood = Mood(h, world.TowardMaster(h.Id).Trust),
                    Today = events.Where(e => e.Actors.Contains(h.Id) && e.Type != "status_warning" && e.Type != "belief_formed")
                        .OrderBy(e => e.Id).Select(e => e.Summary).ToList(),
                    Beliefs = Reflection.Strongest(h, BeliefsShown)
                        .Select(b => b.IsKeyMemory ? $"{b.Text} (не забудет никогда)" : $"{b.Text} ({Firmness(rules, b.Strength)})")
                        .ToList(),
                    RecentDiary = h.Diary.Where(d => d.Day < day).Reverse().Take(RecentDiaryEntries).Reverse().Select(d => d.Text).ToList(),
                    DiaryDraft = today?.Text ?? ""
                });
            }
            return brief;
        }

        /// <summary>Подставляет переписанные моделью тексты. Пустые и неизвестные — пропускаются, остаётся шаблон.</summary>
        public static int Apply(GameWorld world, int day, IReadOnlyDictionary<string, string> diaries, string? morning, string? battle)
        {
            int applied = 0;
            foreach (var pair in diaries)
            {
                var hero = world.Heroes.FirstOrDefault(h => h.Id == pair.Key);
                var entry = hero?.Diary.LastOrDefault(d => d.Day == day);
                if (entry == null || string.IsNullOrWhiteSpace(pair.Value))
                    continue;
                entry.Text = pair.Value.Trim();
                entry.Source = TextSource.Ai;
                applied++;
            }
            applied += ApplyReport(world, day, DayReport.Morning, morning);
            applied += ApplyReport(world, day, DayReport.Battle, battle);
            return applied;
        }

        private static int ApplyReport(GameWorld world, int day, string kind, string? text)
        {
            var report = world.Chronicle.LastOrDefault(r => r.Day == day && r.Kind == kind);
            if (report == null || string.IsNullOrWhiteSpace(text))
                return 0;
            report.Text = text!.Trim();
            report.Source = TextSource.Ai;
            return 1;
        }

        private static string Firmness(GameRules rules, int strength) =>
            strength >= rules.Config.Memory.StrongFrom ? "твёрдо уверен(а)" : strength >= 20 ? "склоняется к этому" : "смутное впечатление";

        private static string Character(Traits t)
        {
            var words = new List<string>();
            void Add(int value, string high, string low)
            {
                if (value >= 70) words.Add(high);
                else if (value <= 30) words.Add(low);
            }
            Add(t.Courage, "смелый", "робкий");
            Add(t.Pride, "гордый", "скромный");
            Add(t.Empathy, "сострадательный", "чёрствый");
            Add(t.Discipline, "дисциплинированный", "своевольный");
            Add(t.Ambition, "честолюбивый", "без амбиций");
            Add(t.Pragmatism, "расчётливый", "идеалист");
            return words.Count == 0 ? "ничем особо не выделяется" : string.Join(", ", words);
        }

        private static string Mood(Hero h, int trust)
        {
            var s = h.State;
            var parts = new List<string>();
            switch (s.Status)
            {
                case MoodStatus.Discontented: parts.Add("недоволен мастером"); break;
                case MoodStatus.OnEdge: parts.Add("на грани, вот-вот перестанет слушаться мастера"); break;
                case MoodStatus.Boycott: parts.Add("объявил мастеру бойкот"); break;
                default: parts.Add(trust >= 60 ? "доверяет мастеру" : "к мастеру относится настороженно"); break;
            }
            if (s.Stress >= 60) parts.Add("сильный стресс");
            else if (s.Stress >= 35) parts.Add("неспокоен");
            if (s.Fatigue >= 70) parts.Add("вымотан");
            parts.Add(s.FoundPurpose ? "понял, что выбраться можно, только став сильнее"
                : s.Resolve < 30 ? "растерян, тоскует по дому" : "постепенно свыкается с Башней");
            return string.Join("; ", parts);
        }
    }

    public sealed class HeroBrief
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Gender { get; set; } = "";
        public int Stars { get; set; }
        public string Profession { get; set; } = "";
        public string Bio { get; set; } = "";
        public string Dream { get; set; } = "";
        public List<string> Values { get; set; } = new();
        public string Character { get; set; } = "";
        public string Mood { get; set; } = "";
        /// <summary>События дня с участием героя — единственные факты, о которых можно писать.</summary>
        public List<string> Today { get; set; } = new();
        public List<string> Beliefs { get; set; } = new();
        /// <summary>Пара прошлых записей — чтобы голос героя не менялся от дня ко дню.</summary>
        public List<string> RecentDiary { get; set; } = new();
        public string DiaryDraft { get; set; } = "";
    }
}

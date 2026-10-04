using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Memory;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Narration
{
    /// <summary>
    /// Рассказчик на шаблонах: дневники героев, утренняя сводка и отчёт о бое.
    /// Работает всегда, без языковой модели; модель (если подключена) потом лишь переписывает эти тексты живее.
    /// Тексты ни на что в симуляции не влияют, а разнообразие берётся из устойчивого хеша,
    /// а не из генератора мира — поэтому рассказчик не меняет ход партии.
    /// </summary>
    public static class TemplateNarrator
    {
        private const int DiaryLines = 3;
        private const int ReportLines = 10;
        /// <summary>Слабее этого впечатление в дневник не попадает; от memory.strongFrom — пишется как твёрдый вывод.</summary>
        private const int HunchFrom = 20;

        public static void WriteDay(GameWorld world, GameRules rules, IReadOnlyList<GameEvent> dayEvents)
        {
            var m = rules.Config.Memory;
            int day = world.Day;

            foreach (var hero in world.Heroes)
            {
                hero.Diary.RemoveAll(d => d.Day == day);
                hero.Diary.Add(new DiaryEntry { Day = day, Text = Diary(world, rules, hero, dayEvents) });
                if (hero.Diary.Count > m.MaxDiaryEntries)
                    hero.Diary.RemoveRange(0, hero.Diary.Count - m.MaxDiaryEntries);
            }

            world.Chronicle.RemoveAll(r => r.Day == day);
            var battle = BattleReport(world, rules, dayEvents);
            if (battle != null)
                world.Chronicle.Add(battle);
            world.Chronicle.Add(MorningReport(world, dayEvents));
            if (world.Chronicle.Count > m.MaxReports)
                world.Chronicle.RemoveRange(0, world.Chronicle.Count - m.MaxReports);
        }

        // ---------- Дневник ----------

        public static string Diary(GameWorld world, GameRules rules, Hero hero, IReadOnlyList<GameEvent> dayEvents)
        {
            int seed = Hash(hero.Id) + world.Day * 7;
            var parts = new List<string> { Opening(hero, seed) };

            var lines = dayEvents
                .Where(e => e.Actors.Contains(hero.Id))
                .Select(e => (e, line: Line(world, hero, e, seed + (int)e.Id)))
                .Where(x => x.line != null)
                .GroupBy(x => x.e.Type).Select(g => g.OrderByDescending(x => x.e.Importance).ThenBy(x => x.e.Id).First())
                .OrderByDescending(x => x.e.Importance).ThenBy(x => x.e.Id)
                .Take(DiaryLines)
                .OrderBy(x => x.e.Id)
                .Select(x => x.line!);
            parts.AddRange(lines);

            var closing = Closing(world, rules, hero, seed);
            if (closing != null)
                parts.Add(closing);
            return string.Join(" ", parts);
        }

        private static string Opening(Hero hero, int seed)
        {
            var s = hero.State;
            string[] options;
            if (s.Status == MoodStatus.Boycott)
                options = new[] { "Этому мастеру я больше не служу.", "Пусть мастер ищет себе других дураков." };
            else if (s.Status == MoodStatus.OnEdge)
                options = new[] { "Ещё немного — и я перестану слушаться мастера.", "Терпение моё на исходе." };
            else if (s.Stress >= 60)
                options = new[] { "Тяжёлый день.", "Руки до сих пор дрожат.", "Не знаю, сколько ещё так выдержу." };
            else if (s.Fatigue >= 70)
                options = new[] { "Еле стою на ногах.", "Устал{g:|а} так, что пишу с трудом." };
            else if (s.Resolve < 30)
                options = new[] { "Опять снился дом.", "До сих пор не понимаю, за что я здесь.", "Скучаю по дому." };
            else
                options = new[] { "Ещё один день в Башне.", "День прошёл.", "Записываю, пока не забыл{g:|а}." };
            return Reflection.ApplyGender(Pick(options, seed), hero.Gender);
        }

        private static string? Closing(GameWorld world, GameRules rules, Hero hero, int seed)
        {
            int day = world.Day;
            // Ключевые воспоминания рождаются из событий, о которых дневник уже написал, — их не повторяем.
            var fresh = hero.Beliefs
                .Where(b => b.LastReinforcedDay == day && !b.IsKeyMemory && b.Strength >= HunchFrom)
                .OrderByDescending(b => b.Strength)
                .FirstOrDefault();
            if (fresh != null)
            {
                var template = fresh.Strength >= rules.Config.Memory.StrongFrom
                    ? Pick(new[] { "Всё больше думаю: {0}.", "Чем дальше, тем яснее: {0}.", "Одно я понял{g:|а}: {0}." }, seed / 3)
                    : Pick(new[] { "Кажется, {0}.", "Похоже, {0}.", "Начинаю думать, что {0}." }, seed / 3);
                return Reflection.ApplyGender(template.Replace("{0}", LowerFirst(world, fresh.Text)), hero.Gender);
            }
            if (hero.State.FoundPurpose && !string.IsNullOrEmpty(hero.Dream))
                return $"Мечта всё та же: {LowerFirst(world, hero.Dream)}.";
            return null;
        }

        /// <summary>Строка дневника от первого лица о событии. null — о таком в дневник не пишут.</summary>
        private static string? Line(GameWorld world, Hero hero, GameEvent e, int seed)
        {
            bool first = e.Actors.Count > 0 && e.Actors[0] == hero.Id;
            string? other = e.Actors.FirstOrDefault(a => a != hero.Id && a != Ids.Master);
            string? t = null;

            switch (e.Type)
            {
                case "activity_train":
                    t = Pick(new[] { "Тренировал{g:ся|ась} до седьмого пота.", "Весь день махал{g:|а} оружием на площадке.", "Тренировал{g:ся|ась}." }, seed);
                    break;
                case "activity_rest":
                    t = Pick(new[] { "Отдыхал{g:|а} — сил ни на что не было.", "Отлёживал{g:ся|ась}.", "Наконец-то выспал{g:ся|ась}." }, seed);
                    break;
                case "activity_work":
                    t = Pick(new[] { "Работал{g:|а} по хозяйству: руки помнят своё ремесло.", "Занял{g:ся|ась} работой — так меньше лезут мысли." }, seed);
                    break;
                case "activity_brood":
                    t = hero.State.Status == MoodStatus.Normal
                        ? Pick(new[] { "Весь вечер думал{g:|а} о доме.", "Сидел{g:|а} {g:один|одна} и вспоминал{g:|а} родных." }, seed)
                        : "Сидел{g:|а} {g:один|одна} и злил{g:ся|ась} на мастера.";
                    break;
                case "socialized":
                    t = Pick(new[] { "{g:Провёл|Провела} время с {otherIns}. Стало легче.", "Болтал{g:|а} с {otherIns} о всякой ерунде.", "{g:Провёл|Провела} вечер с {otherIns}." }, seed);
                    break;
                case "quarrel":
                    t = Pick(new[] { "Поругал{g:ся|ась} с {otherIns}. До сих пор кипит.", "Опять сцепил{g:ся|ась} с {otherIns}." }, seed);
                    break;
                case "teaching":
                    t = first
                        ? "Поделил{g:ся|ась} с {otherIns} тем, что понял{g:|а} в боях."
                        : "{other} {og:объяснил|объяснила} мне, как держаться в бою. Запомню.";
                    break;
                case "teaching_refused":
                    t = first
                        ? "Хотел{g:|а} поделиться опытом, но {other} и слушать не {og:стал|стала}."
                        : "{other} опять {og:полез|полезла} с советами. Обойдусь.";
                    break;
                case "technique_learned":
                    t = "Кажется, начинаю понимать, как выживать в этих боях.";
                    break;
                case "found_purpose":
                    t = "Сегодня я понял{g:|а}: дороги назад нет. Выбраться можно, только став сильнее.";
                    break;
                case "loot_kept":
                    t = "Мастер забрал всю добычу себе. Мы рисковали жизнью — и ничего.";
                    break;
                case "loot_share":
                    t = e.Emotion < 0 ? "Мастер дал мне меньше, чем я заслужил{g:|а}." : "Получил{g:|а} свою долю добычи, по-честному.";
                    break;
                case "master_gift":
                    t = e.Emotion > 0 ? "Мастер сделал мне подарок. Приятно, чего уж." : "Мастер опять с подарком. Чего ему от меня надо?";
                    break;
                case "expedition_result":
                    t = e.Data.TryGetValue("outcome", out var outcome) && outcome == "Victory"
                        ? Pick(new[] { "Мы отбились — все волны выдержали.", "Бой был тяжёлый, но мы выстояли." }, seed)
                        : null;
                    break;
                case "battle_injured":
                    t = first ? "Меня ранили. На миг показалось — всё, конец." : null;
                    break;
                case "battle_morale_broken":
                    t = first ? "Видел{g:|а}, что стало с {otherIns}, — и руки опустились." : null;
                    break;
                case "battle_self_defense":
                    t = first ? "Драться не хотел{g:|а}, но на меня напали — пришлось отбиваться." : null;
                    break;
                case "battle_conduct":
                    e.Data.TryGetValue("response", out var response);
                    if (response == nameof(OrderResponse.Boycott)) t = "В бою я и пальцем не пошевелил{g:|а} ради мастера.";
                    else if (response == nameof(OrderResponse.Refuses)) t = "Не стал{g:|а} я лезть в драку. Пусть мастер сам воюет.";
                    else if (response == nameof(OrderResponse.Grudging)) t = "{g:Дрался|Дралась} вполсилы — не за что мне выкладываться.";
                    break;
                case "mvp":
                    t = "Говорят, сегодня я {g:дрался|дралась} лучше всех.";
                    break;
                case "advice_worked":
                    t = "Мастер подсказал, где встать, — и это сработало.";
                    break;
                case "advice_failed":
                    t = "Послушал{g:ся|ась} мастера — и зря.";
                    break;
                case "battle_advice_spam":
                    t = "Мастер в бою сыпал советами так, что голова шла кругом.";
                    break;
                case "hero_died":
                    other = e.Data.TryGetValue("hero", out var dead) ? dead : null;
                    t = other == null ? null : "{other} погиб{og:|ла}. Не могу в это поверить.";
                    break;
            }

            return t == null ? null : Reflection.Render(world, t, hero, other);
        }

        // ---------- Отчёты для мастера ----------

        private static readonly HashSet<string> BattleMoments = new HashSet<string>
        {
            "battle_injured", "battle_death", "battle_morale_broken", "battle_conduct", "battle_self_defense", "battle_advice_spam"
        };

        public static DayReport? BattleReport(GameWorld world, GameRules rules, IReadOnlyList<GameEvent> dayEvents)
        {
            var result = dayEvents.FirstOrDefault(e => e.Type == "expedition_result");
            if (result == null)
                return null;

            var lines = new List<string>();
            lines.AddRange(dayEvents
                .Where(e => BattleMoments.Contains(e.Type) && e.Importance >= 4)
                .OrderBy(e => e.Data.TryGetValue("tick", out var t) && int.TryParse(t, out var n) ? n : 0).ThenBy(e => e.Id)
                .Take(ReportLines - 2)
                .Select(e => e.Summary));
            lines.Add(result.Summary);
            var mvp = dayEvents.FirstOrDefault(e => e.Type == "mvp");
            if (mvp != null)
                lines.Add(mvp.Summary);

            return new DayReport
            {
                Day = world.Day,
                Kind = DayReport.Battle,
                Title = $"Отчёт о бое: «{rules.Mission(rules.Config.Expedition.MissionId).Name}»",
                Text = string.Join("\n", lines)
            };
        }

        public static DayReport MorningReport(GameWorld world, IReadOnlyList<GameEvent> dayEvents)
        {
            // Подробности боя — в отчёте о бое; здесь — главное за день и то, к чему пришли герои.
            var lines = dayEvents
                .Where(e => !e.Type.StartsWith("battle_") && e.Type != "status_warning"
                            && e.Type != "expedition_result" && e.Type != "mvp"
                            && (e.Importance >= 5 || e.Type == "belief_formed"))
                .OrderByDescending(e => e.Importance).ThenBy(e => e.Id)
                .Take(ReportLines)
                .OrderBy(e => e.Id)
                .Select(e => e.Summary)
                .ToList();
            var battle = dayEvents.FirstOrDefault(e => e.Type == "expedition_result");
            if (battle != null)
                lines.Insert(0, battle.Data.TryGetValue("outcome", out var o) && o == "Victory"
                    ? "Вылазка: оборона выдержала (подробности — в отчёте о бое)."
                    : "Вылазка: отряд погиб (подробности — в отчёте о бое).");
            if (lines.Count == 0)
                lines.Add("День прошёл спокойно: герои занимались своими делами.");

            return new DayReport
            {
                Day = world.Day,
                Kind = DayReport.Morning,
                Title = $"Утренняя сводка: итоги дня {world.Day}",
                Text = string.Join("\n", lines)
            };
        }

        // ---------- Вспомогательное ----------

        /// <summary>Первая буква — маленькая, кроме имён и Башни: «мастер делит добычу честно», но «Грон — хороший товарищ».</summary>
        private static string LowerFirst(GameWorld world, string text)
        {
            if (text.Length == 0 || !char.IsUpper(text[0]))
                return text;
            int space = text.IndexOfAny(new[] { ' ', ',' });
            var word = space < 0 ? text : text.Substring(0, space);
            bool proper = word == "Башня"
                          || world.Heroes.Any(h => h.Name.StartsWith(word))
                          || world.Fallen.Any(f => f.Name.StartsWith(word));
            return proper ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);
        }

        private static string Pick(string[] options, int seed) => options[(int)((uint)seed % (uint)options.Length)];

        /// <summary>Устойчивый хеш строки (FNV-1a): одинаков при каждом запуске, в отличие от GetHashCode.</summary>
        private static int Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var ch in s)
                {
                    h ^= ch;
                    h *= 16777619;
                }
                return (int)(h & 0x7fffffff);
            }
        }
    }
}

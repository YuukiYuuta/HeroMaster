using System.Collections.Generic;
using System.Linq;
using System.Text;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Memory
{
    /// <summary>
    /// Ночная рефлексия: герой перебирает события дня и делает выводы — убеждения.
    /// Повторяющийся опыт укрепляет вывод, противоположный — ослабляет, а без подтверждений он
    /// медленно забывается. Самое тяжёлое (гибель товарища, момент осознания) становится
    /// ключевым воспоминанием и не забывается никогда.
    /// Всё считает код по правилам из data/beliefs.json — языковая модель здесь не участвует.
    /// </summary>
    public static class Reflection
    {
        private const int MaxSourceEvents = 10;

        public static void Run(GameWorld world, GameRules rules)
        {
            int day = world.Day;
            // Снимок: записи о новых убеждениях, сделанные этой ночью, сами не рефлексируются.
            var events = world.Log.ForDay(day).ToList();

            foreach (var hero in world.Heroes)
            {
                foreach (var e in events.Where(e => e.Actors.Contains(hero.Id)))
                    foreach (var rule in rules.Beliefs.For(e.Type))
                    {
                        if (!rule.MatchesEmotion(e.Emotion) || !IsSubject(rule, e, hero))
                            continue;
                        var aboutId = ResolveAbout(world, rule, e, hero);
                        if (aboutId == null)
                            continue;
                        Strengthen(world, rules, hero, rule, aboutId, e);
                    }

                Forget(rules, hero, day);
            }
        }

        /// <summary>
        /// Как убеждения о мастере сдвигают готовность слушаться: «мастер делит честно» помогает,
        /// «мастер забирает добычу себе» мешает — даже когда доверие уже восстановлено подарками.
        /// </summary>
        public static int MasterAttitude(GameRules rules, Hero hero)
        {
            var m = rules.Config.Memory;
            int sum = hero.Beliefs.Where(b => b.AboutId == Ids.Master).Sum(b => b.Emotion * b.Strength);
            return System.Math.Max(-m.MasterBeliefMax, System.Math.Min(m.MasterBeliefMax, sum / m.MasterBeliefDivisor));
        }

        /// <summary>Самые сильные убеждения героя: сначала ключевые воспоминания, потом по силе.</summary>
        public static IEnumerable<Belief> Strongest(Hero hero, int count) =>
            hero.Beliefs.OrderByDescending(b => b.IsKeyMemory).ThenByDescending(b => b.Strength).ThenBy(b => b.FormedDay).Take(count);

        private static bool IsSubject(BeliefRule rule, GameEvent e, Hero hero)
        {
            switch (rule.Subject)
            {
                case "first": return e.Actors.Count > 0 && e.Actors[0] == hero.Id;
                case "second": return e.Actors.Count > 1 && e.Actors[1] == hero.Id;
                default: return true;
            }
        }

        /// <summary>О ком вывод. null — вывод сделать не о ком (например, «другой» участник — сам герой).</summary>
        private static string? ResolveAbout(GameWorld world, BeliefRule rule, GameEvent e, Hero hero)
        {
            if (rule.About == BeliefCatalog.AboutMaster)
                return Ids.Master;
            if (rule.About == BeliefCatalog.AboutSelf)
                return hero.Id;

            string? other;
            if (rule.About == BeliefCatalog.AboutOther)
                other = e.Actors.FirstOrDefault(a => a != hero.Id && a != Ids.Master);
            else
                other = e.Data.TryGetValue(rule.About.Substring(BeliefCatalog.AboutDataPrefix.Length), out var v) ? v : null;

            if (other == null || other == hero.Id || other == Ids.Master)
                return null;
            return world.HasHero(other) || world.Fallen.Any(f => f.Id == other) ? other : null;
        }

        private static string BeliefId(BeliefRule rule, string aboutId) =>
            rule.About == BeliefCatalog.AboutMaster || rule.About == BeliefCatalog.AboutSelf ? rule.Id : rule.Id + ":" + aboutId;

        private static void Strengthen(GameWorld world, GameRules rules, Hero hero, BeliefRule rule, string aboutId, GameEvent e)
        {
            var m = rules.Config.Memory;
            int day = world.Day;
            string id = BeliefId(rule, aboutId);

            var belief = hero.Beliefs.FirstOrDefault(b => b.Id == id);
            if (belief == null)
            {
                belief = new Belief
                {
                    Id = id,
                    AboutId = aboutId,
                    Text = Render(world, rule.Text, hero, aboutId),
                    Emotion = rule.Emotion,
                    FormedDay = day,
                    IsKeyMemory = rule.Key || e.Importance >= m.KeyMemoryImportance
                };
                hero.Beliefs.Add(belief);
            }

            int before = belief.Strength;
            belief.Strength = GameWorld.Clamp(belief.Strength + rule.Gain);
            belief.LastReinforcedDay = day;
            belief.SourceEventIds.Add(e.Id);
            if (belief.SourceEventIds.Count > MaxSourceEvents)
                belief.SourceEventIds.RemoveAt(0);

            // Противоположный опыт подтачивает старые выводы о том же человеке.
            foreach (var opposed in rule.Opposes)
            {
                var o = hero.Beliefs.FirstOrDefault(b => b.Id == BeliefId(rules.Beliefs.Rules.First(r => r.Id == opposed), aboutId));
                if (o == null || o.IsKeyMemory)
                    continue;
                o.Strength -= rule.Gain;
                if (o.Strength <= 0)
                    hero.Beliefs.Remove(o);
            }

            var actors = aboutId == hero.Id ? new[] { hero.Id } : new[] { hero.Id, aboutId };
            int emotion = System.Math.Sign(belief.Emotion) * System.Math.Min(3, System.Math.Abs(belief.Emotion));
            // О ключевых воспоминаниях журнал уже знает (гибель, момент осознания) — пишем только о созревших выводах.
            if (!belief.IsKeyMemory && before < m.StrongFrom && belief.Strength >= m.StrongFrom)
            {
                var ev = world.Log.Append(day, DayPhase.NightReflection, "belief_formed",
                    $"{hero.Name} всё твёрже {hero.G("уверен", "уверена")}: «{belief.Text}».",
                    importance: 4, emotion: emotion, actors: actors);
                ev.Data["belief"] = belief.Id;
            }
        }

        /// <summary>Неподтверждённое слабеет и забывается; лишнее (самое слабое и старое) вытесняется.</summary>
        private static void Forget(GameRules rules, Hero hero, int day)
        {
            var m = rules.Config.Memory;
            foreach (var b in hero.Beliefs.Where(b => !b.IsKeyMemory && b.LastReinforcedDay != day))
                b.Strength -= m.DecayPerNight;
            hero.Beliefs.RemoveAll(b => !b.IsKeyMemory && b.Strength <= 0);

            Trim(hero, false, m.MaxBeliefs);
            Trim(hero, true, m.MaxKeyMemories);
        }

        private static void Trim(Hero hero, bool key, int max)
        {
            var group = hero.Beliefs.Where(b => b.IsKeyMemory == key).ToList();
            foreach (var b in group.OrderBy(b => b.Strength).ThenBy(b => b.FormedDay).Take(System.Math.Max(0, group.Count - max)))
                hero.Beliefs.Remove(b);
        }

        /// <summary>
        /// Подставляет имена в нужном падеже и формы по полу: {other}, {otherGen}, {otherIns},
        /// {g:он|она} — по полу героя, {og:он|она} — по полу другого героя (живого или павшего).
        /// </summary>
        public static string Render(GameWorld world, string template, Hero subject, string? aboutId)
        {
            string name = aboutId ?? "", gen = name, ins = name;
            var otherGender = Gender.Male;
            var other = world.Heroes.FirstOrDefault(h => h.Id == aboutId);
            if (other != null)
            {
                name = other.Name; gen = other.NameGenitive; ins = other.NameInstrumental; otherGender = other.Gender;
            }
            else
            {
                var fallen = world.Fallen.FirstOrDefault(f => f.Id == aboutId);
                if (fallen != null)
                {
                    name = fallen.Name; gen = fallen.NameGenitive; ins = fallen.NameInstrumental; otherGender = fallen.Gender;
                }
            }

            var text = template.Replace("{otherGen}", gen).Replace("{otherIns}", ins).Replace("{other}", name);
            return ApplyGender(ApplyGender(text, otherGender, "{og:"), subject.Gender);
        }

        /// <summary>Раскрывает {g:мужская|женская} (или другой маркер) по полу.</summary>
        public static string ApplyGender(string text, Gender gender, string marker = "{g:")
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int start = text.IndexOf(marker, i, System.StringComparison.Ordinal);
                if (start < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                int end = text.IndexOf('}', start);
                int bar = text.IndexOf('|', start);
                if (end < 0 || bar < 0 || bar > end)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, start - i);
                int from = start + marker.Length;
                sb.Append(gender == Gender.Female ? text.Substring(bar + 1, end - bar - 1) : text.Substring(from, bar - from));
                i = end + 1;
            }
            return sb.ToString();
        }
    }
}

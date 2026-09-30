using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>
    /// Ночь: восстановление, лечение и пересчёт статусов.
    /// Ухудшение статуса наступает сразу, а улучшение — только с запасом доверия (recoverMargin),
    /// иначе статус «дребезжал» бы на границе. В бойкот герой уходит только после
    /// нескольких ночей «на грани» — у мастера всегда есть время заметить и исправить.
    /// Рефлексия и память появятся здесь в группе 4.
    /// </summary>
    public static class NightPhase
    {
        public static void Run(GameWorld world, GameRules rules)
        {
            var a = rules.Config.Activities;
            int day = world.Day;

            foreach (var hero in world.Heroes)
            {
                var s = hero.State;
                s.Fatigue = GameWorld.Clamp(s.Fatigue - a.NightFatigueRecovery);
                s.Stress = GameWorld.Clamp(s.Stress - a.NightStressRecovery);

                GrowResolve(world, rules, hero);
                UpdateStatus(world, rules, hero);
            }

            // Обиды остывают, если их не подогревать.
            foreach (var r in world.Relationships)
                if (r.To != Ids.Master && r.Rivalry > 0)
                    r.Rivalry = GameWorld.Clamp(r.Rivalry - a.RivalryDecayPerNight);
        }

        /// <summary>
        /// Каждый день в Башне герой всё яснее понимает: выхода нет, кроме как становиться сильнее.
        /// Первый переход порога — момент осознания, важное событие для его памяти.
        /// </summary>
        private static void GrowResolve(GameWorld world, GameRules rules, Hero hero)
        {
            var rc = rules.Config.Resolve;
            var s = hero.State;
            s.Resolve = GameWorld.Clamp(s.Resolve + rc.DailyGain);

            if (!s.FoundPurpose && s.Resolve >= rc.PurposeThreshold)
            {
                s.FoundPurpose = true;
                world.Log.Append(world.Day, DayPhase.NightReflection, "found_purpose",
                    $"{hero.Name} {hero.G("понял", "поняла")}: пути назад нет, выбраться из Башни можно, только став сильнее.",
                    importance: 8, emotion: 1, actors: new[] { hero.Id });
            }
        }

        public static void UpdateStatus(GameWorld world, GameRules rules, Hero hero)
        {
            var t = rules.Config.Trust;
            int trust = world.TowardMaster(hero.Id).Trust;
            var s = hero.State;
            int day = world.Day;

            int current = TrustRules.Rank(s.Status);
            int raw = RankFor(trust, t.BoycottBelow, t.OnEdgeBelow, t.DiscontentedBelow);
            // Бойкот — только после нескольких ночей «на грани».
            if (raw == 3 && s.DaysOnEdge < t.DaysOnEdgeBeforeBoycott)
                raw = 2;

            int next;
            if (raw > current)
            {
                next = raw;
            }
            else
            {
                int withMargin = RankFor(trust, t.BoycottBelow + t.RecoverMargin, t.OnEdgeBelow + t.RecoverMargin, t.DiscontentedBelow + t.RecoverMargin);
                next = System.Math.Min(current, System.Math.Max(raw, withMargin));
            }

            var newStatus = (MoodStatus)next;
            s.DaysOnEdge = next >= 2 ? s.DaysOnEdge + 1 : 0;

            var actors = new[] { hero.Id, Ids.Master };
            if (next > current)
            {
                s.Status = newStatus;
                world.Log.Append(day, DayPhase.NightReflection, "status_changed", WorseText(hero, newStatus),
                    importance: newStatus == MoodStatus.Boycott ? 8 : 7, emotion: -3, actors: actors);
            }
            else if (next < current)
            {
                s.Status = newStatus;
                world.Log.Append(day, DayPhase.NightReflection, "status_changed", BetterText(hero, newStatus),
                    importance: 5, emotion: 2, actors: actors);
            }
            else if (next > 0)
            {
                // Частые предупреждения: мастер должен видеть, к чему всё идёт.
                world.Log.Append(day, DayPhase.NightReflection, "status_warning", WarningText(hero, newStatus),
                    importance: 2, emotion: -1, actors: actors);
            }
        }

        /// <summary>Статус в начале партии. Бойкота на старте не бывает — максимум «на грани».</summary>
        public static MoodStatus InitialStatus(int trust, Config.BalanceConfig config)
        {
            var t = config.Trust;
            return (MoodStatus)System.Math.Min(2, RankFor(trust, t.BoycottBelow, t.OnEdgeBelow, t.DiscontentedBelow));
        }

        private static int RankFor(int trust, int boycottBelow, int onEdgeBelow, int discontentedBelow)
        {
            if (trust < boycottBelow) return 3;
            if (trust < onEdgeBelow) return 2;
            if (trust < discontentedBelow) return 1;
            return 0;
        }

        private static string WorseText(Hero h, MoodStatus status)
        {
            switch (status)
            {
                case MoodStatus.Discontented: return $"{h.Name} {h.G("недоволен", "недовольна")} мастером.";
                case MoodStatus.OnEdge: return $"{h.Name} на грани: ещё немного — и перестанет подчиняться.";
                default: return $"{h.Name} {h.G("объявил", "объявила")} мастеру бойкот.";
            }
        }

        private static string BetterText(Hero h, MoodStatus status)
        {
            switch (status)
            {
                case MoodStatus.Normal: return $"{h.Name} {h.G("оттаял", "оттаяла")}: обида на мастера прошла.";
                case MoodStatus.Discontented: return $"{h.Name} уже не так зол{h.G("", "а")}, но всё ещё {h.G("недоволен", "недовольна")}.";
                default: return $"{h.Name} {h.G("прекратил", "прекратила")} бойкот, но пока на грани.";
            }
        }

        private static string WarningText(Hero h, MoodStatus status)
        {
            switch (status)
            {
                case MoodStatus.Discontented: return $"{h.Name} ворчит и косо смотрит на мастера.";
                case MoodStatus.OnEdge: return $"{h.Name} открыто огрызается. До бойкота недалеко.";
                default: return $"{h.Name} продолжает бойкот.";
            }
        }
    }
}

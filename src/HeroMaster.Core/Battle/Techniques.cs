using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Battle
{
    /// <summary>
    /// Приёмы — то, чему герой научился на опыте. Урок общий, а не про одну карту:
    /// «в обороне держаться узкого прохода» пригодится в любом бою.
    /// Учатся медленно, как люди: несколько удачных повторений, без практики навык слабеет.
    /// Усвоенный приём меняет то, как герой видит позиции, — и без подсказок мастера.
    /// </summary>
    public static class Techniques
    {
        public const string Defense = "defense";
        public const string Narrow = "narrow";
        public const string Cover = "cover";
        public const string Far = "far";

        public static string Describe(string action)
        {
            switch (action)
            {
                case Narrow: return "в обороне держаться узкого прохода";
                case Cover: return "в обороне держаться в укрытии";
                case Far: return "держаться подальше от мест, откуда лезут враги";
                default: return action;
            }
        }

        /// <summary>Какие уроки можно вынести из позиции в этой зоне.</summary>
        public static List<string> Features(MissionDefinition mission, ZoneDefinition zone)
        {
            var result = new List<string>();
            if (zone.Spawn)
                return result;
            if (zone.Width <= 2) result.Add(Narrow);
            if (zone.Cover >= 20) result.Add(Cover);
            if (mission.DistanceToSpawns(zone.Id) >= 2) result.Add(Far);
            return result;
        }

        public static Technique? Find(Hero hero, string action) =>
            hero.Techniques.FirstOrDefault(t => t.Situation == Defense && t.Action == action);

        public static int Strength(Hero hero, string action) => Find(hero, action)?.Strength ?? 0;

        private static Technique Ensure(Hero hero, string action, string learnedFrom)
        {
            var t = Find(hero, action);
            if (t == null)
            {
                t = new Technique { Id = $"{Defense}:{action}", Situation = Defense, Action = action, LearnedFrom = learnedFrom };
                hero.Techniques.Add(t);
            }
            return t;
        }

        /// <summary>Насколько приёмы героя поднимают в его глазах ценность зоны.</summary>
        public static int ZoneBonus(GameRules rules, Hero hero, MissionDefinition mission, ZoneDefinition zone) =>
            Features(mission, zone).Sum(f => Strength(hero, f)) * rules.Config.Learning.TechniqueValuePercent / 100;

        /// <summary>
        /// Опыт подтвердил (или опроверг) урок. Сила растёт со скоростью обучения героя.
        /// Первое пересечение порога — момент «запомнил», важное событие для памяти.
        /// </summary>
        public static void Reinforce(GameWorld world, GameRules rules, Hero hero, string action, bool success, string learnedFrom)
        {
            var c = rules.Config.Learning;
            var t = Ensure(hero, action, learnedFrom);
            int before = t.Strength;
            int learning = rules.Config.Tier(hero.Stars).LearningSpeedPercent;
            if (success)
            {
                t.Successes++;
                t.Strength = GameWorld.Clamp(t.Strength + c.StrengthPerSuccess * learning / 100);
            }
            else
            {
                t.Failures++;
                t.Strength = GameWorld.Clamp(t.Strength - c.StrengthLossPerFailure);
            }
            t.LastPracticedDay = world.Day;

            if (before < c.LearnedThreshold && t.Strength >= c.LearnedThreshold)
                world.Log.Append(world.Day, DayPhase.Expedition, "technique_learned",
                    $"{hero.Name} {hero.G("усвоил", "усвоила")} урок: {Describe(action)}.",
                    importance: 8, emotion: 2, actors: new[] { hero.Id });
        }

        /// <summary>Стоять там, где приём работает, — тоже практика (только для уже знакомых приёмов).</summary>
        public static void Practice(GameWorld world, GameRules rules, Hero hero, IEnumerable<string> features)
        {
            foreach (var f in features)
            {
                var t = Find(hero, f);
                if (t == null || t.Strength == 0)
                    continue;
                t.Strength = GameWorld.Clamp(t.Strength + rules.Config.Learning.PracticeStrength);
                t.LastPracticedDay = world.Day;
            }
        }

        /// <summary>Без практики навык слабеет.</summary>
        public static void Decay(GameWorld world, GameRules rules, Hero hero)
        {
            foreach (var t in hero.Techniques.Where(t => t.LastPracticedDay < world.Day && t.Strength > 0))
                t.Strength = GameWorld.Clamp(t.Strength - rules.Config.Learning.DecayPerIdleDay);
        }

        /// <summary>
        /// На базе, за разговором, опытный передаёт приём менее опытному. У уважаемого учителя учатся быстрее,
        /// у соперника — отказываются учиться. Возвращает true, если урок состоялся.
        /// </summary>
        public static bool TryTeach(GameWorld world, GameRules rules, Hero teacher, Hero learner)
        {
            var c = rules.Config.Learning;
            var lesson = teacher.Techniques
                .Where(t => t.Situation == Defense && t.Strength >= c.LearnedThreshold && Strength(learner, t.Action) < t.Strength - 20)
                .OrderByDescending(t => t.Strength)
                .FirstOrDefault();
            if (lesson == null)
                return false;

            var view = world.GetRelationship(learner.Id, teacher.Id);
            if (view.Rivalry > c.RivalRefusesTeachingAbove)
            {
                if (world.Rng.Chance(c.TeachChancePercent))
                    world.Log.Append(world.Day, DayPhase.BaseLife, "teaching_refused",
                        $"{teacher.Name} {teacher.G("попытался", "попыталась")} поделиться опытом, но {learner.Name} и слушать не {learner.G("стал", "стала")}: от соперника советов не принимают.",
                        importance: 4, emotion: -1, actors: new[] { teacher.Id, learner.Id });
                return false;
            }
            if (!world.Rng.Chance(c.TeachChancePercent))
                return false;

            int amount = c.TeachingStrength;
            if (view.Respect >= c.RespectedTeacherFrom)
                amount += amount * c.RespectedTeacherBonusPercent / 100;
            amount = amount * rules.Config.Tier(learner.Stars).LearningSpeedPercent / 100;

            var t = Ensure(learner, lesson.Action, teacher.Id);
            int before = t.Strength;
            t.Strength = GameWorld.Clamp(System.Math.Min(lesson.Strength, t.Strength + amount));
            t.LastPracticedDay = world.Day;

            world.Log.Append(world.Day, DayPhase.BaseLife, "teaching",
                $"{teacher.Name} и {learner.Name} разговорились о боях: {teacher.Name} {teacher.G("объяснил", "объяснила")}, как {Lesson(lesson.Action)}.",
                importance: 4, emotion: 1, actors: new[] { teacher.Id, learner.Id });

            if (before < c.LearnedThreshold && t.Strength >= c.LearnedThreshold)
                world.Log.Append(world.Day, DayPhase.BaseLife, "technique_learned",
                    $"{learner.Name} {learner.G("усвоил", "усвоила")} урок от {teacher.NameGenitive}: {Describe(lesson.Action)}.",
                    importance: 8, emotion: 2, actors: new[] { learner.Id, teacher.Id });
            return true;
        }

        private static string Lesson(string action)
        {
            switch (action)
            {
                case Narrow: return "держать узкий проход";
                case Cover: return "прятаться за укрытием";
                case Far: return "не лезть туда, откуда идут враги";
                default: return action;
            }
        }
    }
}

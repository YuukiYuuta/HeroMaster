using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>
    /// Жизнь на базе. Каждый герой сам выбирает занятие (utility AI): у каждого занятия есть оценка
    /// от характера и состояния, побеждает наибольшая. Намёк мастера лишь сдвигает оценку —
    /// и только если герой доверяет мастеру.
    /// Кто был на вылазке, успевает одно занятие вечером; остальные — два (днём и вечером).
    /// </summary>
    public static class BaseLifePhase
    {
        private static readonly Activity[] AllActivities =
            { Activity.Rest, Activity.Train, Activity.Socialize, Activity.Work, Activity.Brood };

        private const int Impossible = -1000;

        public static void Run(GameWorld world, GameRules rules, IReadOnlyDictionary<string, Activity> hints, IReadOnlyList<string> expeditionMembers)
        {
            // Днём на базе только те, кто не ушёл на вылазку.
            var atBaseDuringDay = world.Heroes.Where(h => !expeditionMembers.Contains(h.Id)).ToList();
            foreach (var hero in atBaseDuringDay)
                Act(world, rules, hero, hints, atBaseDuringDay, evening: false);

            // Вечером все дома.
            foreach (var hero in world.Heroes)
                Act(world, rules, hero, hints, world.Heroes, evening: true);

            EveningTension(world, rules);
        }

        /// <summary>
        /// «Общий ужин»: живя под одной крышей, несовместимые люди сцепляются и без разговоров по душам.
        /// Каждая пара с противоположными ценностями или соперничеством проверяется раз за вечер.
        /// </summary>
        private static void EveningTension(GameWorld world, GameRules rules)
        {
            var c = rules.Config.Activities;
            var heroes = world.Heroes;
            for (int i = 0; i < heroes.Count; i++)
            {
                for (int j = i + 1; j < heroes.Count; j++)
                {
                    var a = heroes[i];
                    var b = heroes[j];
                    int conflicts = rules.Catalog.ValueConflicts(a.Values, b.Values);
                    int rivalry = System.Math.Max(world.GetRelationship(a.Id, b.Id).Rivalry, world.GetRelationship(b.Id, a.Id).Rivalry);
                    int chance = System.Math.Min(c.TensionChanceMax,
                        conflicts * c.TensionChancePerConflict + rivalry / 10 * c.TensionChancePerTenRivalry);
                    if (chance > 0 && world.Rng.Chance(chance))
                        Quarrel(world, rules, a, b, conflicts, "За общим ужином");
                }
            }
        }

        private static void Quarrel(GameWorld world, GameRules rules, Hero a, Hero b, int conflicts, string when)
        {
            var c = rules.Config.Activities;
            foreach (var (x, y) in new[] { (a, b), (b, a) })
            {
                TrustRules.ChangeRelationship(world, x.Id, y.Id, trust: -c.QuarrelTrustLoss, affection: -c.QuarrelAffectionLoss, rivalry: c.QuarrelRivalry);
                x.State.Stress = GameWorld.Clamp(x.State.Stress + c.QuarrelStress);
            }
            string reason = conflicts > 0 ? "не сошлись в том, что правильно, а что нет" : "старое соперничество дало о себе знать";
            world.Log.Append(world.Day, DayPhase.BaseLife, "quarrel",
                $"{when} {a.Name} и {b.Name} поссорились: {reason}.",
                importance: 6, emotion: -3, actors: new[] { a.Id, b.Id });
        }

        /// <summary>Оценки всех занятий для героя (без случайного разброса). Открыто для тестов и отладки.</summary>
        public static Dictionary<Activity, int> Scores(GameWorld world, GameRules rules, Hero hero, IReadOnlyDictionary<string, Activity> hints)
        {
            var c = rules.Config.Activities;
            var t = hero.Traits;
            var s = hero.State;
            bool boycott = s.Status == MoodStatus.Boycott;

            var scores = new Dictionary<Activity, int>
            {
                [Activity.Rest] = c.RestBase + s.Fatigue * c.RestFatiguePercent / 100 + s.Stress * c.RestStressPercent / 100,
                [Activity.Train] = c.TrainBase + s.Resolve * c.TrainResolvePercent / 100
                      + t.Ambition * c.TrainAmbitionPercent / 100 + t.Discipline * c.TrainDisciplinePercent / 100
                      - s.Fatigue * c.TrainFatiguePercent / 100,
                [Activity.Socialize] = c.SocializeBase + t.Empathy * c.SocializeEmpathyPercent / 100 + s.Stress * c.SocializeStressPercent / 100,
                // Бойкотирующий герой не работает на мастера.
                [Activity.Work] = boycott ? Impossible : c.WorkBase + t.Discipline * c.WorkDisciplinePercent / 100,
                [Activity.Brood] = s.Stress * c.BroodStressPercent / 100 + StatusBroodBonus(c, s.Status)
                                   + (100 - s.Resolve) * c.BroodDespairPercent / 100
            };

            // Скука: одно и то же подряд надоедает.
            if (System.Enum.TryParse<Activity>(s.LastActivity, out var last) && scores[last] > Impossible)
                scores[last] -= System.Math.Min(c.RepeatPenaltyMax, s.ActivityStreak * c.RepeatPenalty);

            if (hints.TryGetValue(hero.Id, out var hinted))
            {
                int trust = world.TowardMaster(hero.Id).Trust;
                if (trust >= c.HintIgnoredBelowTrust)
                    scores[hinted] += c.HintBonusMax * trust / 100;
                else if (t.Pride >= c.HintSpitePride)
                    scores[hinted] -= c.HintSpitePenalty; // назло мастеру
            }

            return scores;
        }

        private static int StatusBroodBonus(Config.ActivityConfig c, MoodStatus status)
        {
            switch (status)
            {
                case MoodStatus.Discontented: return c.BroodDiscontentedBonus;
                case MoodStatus.OnEdge: return c.BroodOnEdgeBonus;
                case MoodStatus.Boycott: return c.BroodBoycottBonus;
                default: return 0;
            }
        }

        private static void Act(GameWorld world, GameRules rules, Hero hero, IReadOnlyDictionary<string, Activity> hints,
            IReadOnlyList<Hero> present, bool evening)
        {
            var c = rules.Config.Activities;
            var scores = Scores(world, rules, hero, hints);

            Activity chosen = Activity.Rest;
            int best = int.MinValue;
            foreach (var activity in AllActivities)
            {
                int score = scores[activity];
                if (score <= Impossible)
                    continue;
                if (c.Jitter > 0)
                    score += world.Rng.Range(-c.Jitter, c.Jitter);
                if (score > best)
                {
                    best = score;
                    chosen = activity;
                }
            }

            // Не с кем общаться — отдыхает.
            var partners = present.Where(p => p.Id != hero.Id).ToList();
            if (chosen == Activity.Socialize && partners.Count == 0)
                chosen = Activity.Rest;

            if (hero.State.LastActivity == chosen.ToString())
                hero.State.ActivityStreak++;
            else
            {
                hero.State.LastActivity = chosen.ToString();
                hero.State.ActivityStreak = 1;
            }

            bool followedHint = hints.TryGetValue(hero.Id, out var hinted) && hinted == chosen;
            string when = evening ? "Вечером" : "Днём";
            string hintNote = followedHint ? " (по намёку мастера)" : "";
            var s = hero.State;
            int day = world.Day;
            var phase = DayPhase.BaseLife;

            switch (chosen)
            {
                case Activity.Rest:
                    s.Fatigue = GameWorld.Clamp(s.Fatigue - c.RestFatigueRecovery);
                    s.Stress = GameWorld.Clamp(s.Stress - c.RestStressRecovery);
                    Log(world, day, phase, "activity_rest", $"{when} {hero.Name} {hero.G("отдыхал", "отдыхала")}{hintNote}.", 1, 1, hero, chosen, followedHint);
                    break;

                case Activity.Train:
                    s.Fatigue = GameWorld.Clamp(s.Fatigue + c.TrainFatigue);
                    hero.Experience += c.TrainExperience * rules.Config.Tier(hero.Stars).LearningSpeedPercent / 100;
                    Log(world, day, phase, "activity_train", $"{when} {hero.Name} {hero.G("тренировался", "тренировалась")}{hintNote}.", 2, 1, hero, chosen, followedHint);
                    break;

                case Activity.Work:
                    s.Fatigue = GameWorld.Clamp(s.Fatigue + c.WorkFatigue);
                    world.Master.Gold += c.WorkGold;
                    Log(world, day, phase, "activity_work",
                        $"{when} {hero.Name} {hero.G("работал", "работала")} по хозяйству ({hero.Profession.ToLowerInvariant()}){hintNote}.", 1, 0, hero, chosen, followedHint);
                    break;

                case Activity.Brood:
                    int lost = s.Status == MoodStatus.Normal ? 0 : -TrustRules.Change(world, hero.Id, -rules.Config.Trust.BroodTrustLoss);
                    // Обиженный копит обиду на мастера; остальные просто тоскуют по дому.
                    string brood = s.Status != MoodStatus.Normal
                        ? $"{when} {hero.Name} {hero.G("сидел", "сидела")} в одиночестве, мрачнее тучи."
                        : $"{when} {hero.Name} {hero.G("сидел", "сидела")} в одиночестве и {hero.G("тосковал", "тосковала")} по дому.";
                    Log(world, day, phase, "activity_brood", brood, lost > 0 ? 3 : 2, -2, hero, chosen, followedHint);
                    break;

                case Activity.Socialize:
                    Socialize(world, rules, hero, partners, when, hintNote, followedHint);
                    break;
            }
        }

        private static void Socialize(GameWorld world, GameRules rules, Hero hero, List<Hero> partners, string when, string hintNote, bool followedHint)
        {
            var c = rules.Config.Activities;
            int day = world.Day;

            // Партнёра выбирают по симпатии и доверию; соперников — реже, но не никогда.
            var weights = partners.Select(p =>
            {
                var r = world.GetRelationship(hero.Id, p.Id);
                return System.Math.Max(1, 20 + r.Affection + r.Trust - r.Rivalry / 2);
            }).ToList();
            int roll = world.Rng.Next(weights.Sum());
            var partner = partners[0];
            for (int i = 0; i < partners.Count; i++)
            {
                if (roll < weights[i])
                {
                    partner = partners[i];
                    break;
                }
                roll -= weights[i];
            }

            int conflicts = rules.Catalog.ValueConflicts(hero.Values, partner.Values);
            int rivalry = System.Math.Max(world.GetRelationship(hero.Id, partner.Id).Rivalry, world.GetRelationship(partner.Id, hero.Id).Rivalry);
            int quarrelChance = System.Math.Min(90, conflicts * c.QuarrelChancePerConflict + rivalry / 10 * c.QuarrelChancePerTenRivalry);

            if (world.Rng.Chance(quarrelChance))
            {
                Quarrel(world, rules, hero, partner, conflicts, when);
            }
            else
            {
                foreach (var (a, b) in new[] { (hero, partner), (partner, hero) })
                    TrustRules.ChangeRelationship(world, a.Id, b.Id, trust: c.SocializeTrust, affection: c.SocializeAffection);
                hero.State.Stress = GameWorld.Clamp(hero.State.Stress - c.SocializeStressRecovery);
                Log(world, day, DayPhase.BaseLife, "socialized",
                    $"{when} {hero.Name} {hero.G("провёл", "провела")} время с {partner.NameInstrumental}{hintNote}.", 2, 1, hero, Activity.Socialize, followedHint, partner.Id);

                // За разговором опытный может поделиться приёмом.
                if (!Battle.Techniques.TryTeach(world, rules, hero, partner))
                    Battle.Techniques.TryTeach(world, rules, partner, hero);
            }
        }

        private static void Log(GameWorld world, int day, DayPhase phase, string type, string summary, int importance, int emotion,
            Hero hero, Activity activity, bool followedHint, string? partnerId = null)
        {
            var actors = partnerId == null ? new[] { hero.Id } : new[] { hero.Id, partnerId };
            var e = world.Log.Append(day, phase, type, summary, importance, emotion, actors);
            e.Data["activity"] = activity.ToString();
            if (followedHint)
                e.Data["followedHint"] = "true";
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Battle
{
    /// <summary>
    /// Совет мастера в бою: «займите эту позицию» — всему отряду или одному герою.
    /// Это совет, а не приказ: каждый решает сам по доверию, дисциплине и решимости.
    /// Бойкотирующий не слушает, отказавшийся драться идёт только туда, где безопаснее,
    /// опытный спорит, если его приёмы говорят, что текущее место лучше.
    /// Советовать можно сколько угодно, но частые и противоречивые советы путают героев.
    /// </summary>
    public static class MasterAdvice
    {
        /// <summary>Даёт совет. Возвращает ошибки (пусто — совет прозвучал).</summary>
        public static List<string> Give(GameWorld world, GameRules rules, BattleState s, string zoneId, string? heroId)
        {
            var errors = new List<string>();
            if (s.IsOver)
                errors.Add("Бой уже закончен.");
            var zone = s.Mission.Zones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null)
                errors.Add($"Нет такой позиции «{zoneId}».");
            else if (zone.Spawn)
                errors.Add("Туда нельзя: оттуда лезут враги.");
            if (heroId != null && s.Heroes.All(h => h.Hero.Id != heroId || h.Dead))
                errors.Add("Этого героя нет в строю.");
            if (errors.Count > 0)
                return errors;

            var c = rules.Config.Advice;
            var recent = s.Advice.Where(a => s.Tick - a.Tick < c.SpamWindowTicks).ToList();
            bool contradiction = recent.Any(a => a.Zone != zoneId && (a.HeroId == null || heroId == null || a.HeroId == heroId));
            int excess = System.Math.Max(0, recent.Count + 1 - c.MaxAdviceInWindow);
            s.Advice.Add(new AdviceRecord { Tick = s.Tick, Zone = zoneId, HeroId = heroId });

            var targets = s.Heroes.Where(h => h.Alive && (heroId == null || h.Hero.Id == heroId)).ToList();
            string where = $"«{zone!.Name}»";
            s.Say("advice", heroId == null
                ? $"Совет мастера всем: держать позицию {where}."
                : $"Совет мастера — {targets[0].Hero.Name}: держать позицию {where}.", 4, 0);

            if (excess > 0 || contradiction)
            {
                int penalty = excess * c.SpamTrustPenalty + (contradiction ? c.ContradictionTrustPenalty : 0);
                foreach (var t in targets)
                    TrustRules.Change(world, t.Hero.Id, -penalty);
                s.Say("advice_spam", contradiction
                    ? "Мастер противоречит сам себе — отряд не понимает, кого слушать."
                    : "Мастер засыпает отряд советами — герои путаются и раздражаются.", 5, -2);
            }

            int confusion = excess * c.SpamFollowPenalty + (contradiction ? c.SpamFollowPenalty : 0);
            foreach (var t in targets)
                Respond(world, rules, s, t, zone, confusion);

            if (heroId == null)
                FollowTheCrowd(rules, s, zone);
            return errors;
        }

        /// <summary>
        /// Стадный инстинкт после совета всем: кто пропустил совет мимо ушей, но видит, что большинство уходит,
        /// скорее пойдёт следом, чем останется без своих. Бойкотирующий и отказавшийся драться — не идут.
        /// </summary>
        private static void FollowTheCrowd(GameRules rules, BattleState s, ZoneDefinition zone)
        {
            var fighters = s.Heroes.Where(h => h.Alive && h.Conduct != Conduct.Abandoned && h.Conduct != Conduct.Passive).ToList();
            foreach (var h in fighters.Where(h => h.TargetZone != zone.Id))
            {
                int going = fighters.Count(o => o != h && o.TargetZone == zone.Id);
                int staying = fighters.Count(o => o != h && o.TargetZone == h.TargetZone);
                if (going == 0 || going < staying)
                    continue;
                var hero = h.Hero;
                h.TargetZone = zone.Id;
                h.MoveCooldown = rules.Config.Battle.HeroMoveTicks;
                s.Say("advice_crowd", $"{hero.Name} не {hero.G("горел", "горела")} желанием, но {hero.G("пошёл", "пошла")} за остальными: без своих страшнее.", 3, 0, hero.Id);
            }
        }

        private static void Respond(GameWorld world, GameRules rules, BattleState s, BattleHero bh, ZoneDefinition zone, int confusion)
        {
            var c = rules.Config.Advice;
            var h = bh.Hero;
            string where = $"«{zone.Name}»";

            // Мастер подтвердил выбор героя — исход боя закрепит или опровергнет урок.
            if (bh.TargetZone == zone.Id)
            {
                if (!bh.FollowedAdvice.Contains(zone.Id))
                    bh.FollowedAdvice.Add(zone.Id);
                s.Say("advice_followed", $"{h.Name} и так держится позиции {where} — мастер подтвердил выбор.", 2, 1, h.Id);
                return;
            }

            if (bh.Conduct == Conduct.Abandoned)
            {
                s.Say("advice_ignored", $"{h.Name} демонстративно {h.G("пропустил", "пропустила")} совет мимо ушей — бойкот.", 3, -1, h.Id);
                return;
            }

            var current = s.Mission.Zone(bh.TargetZone);
            if (bh.Conduct == Conduct.Passive)
            {
                if (zone.Cover > current.Cover)
                    Follow(rules, s, bh, zone, $"{h.Name} драться не хочет, но в укрытие {h.G("пошёл", "пошла")} охотно: {where}.");
                else
                    s.Say("advice_ignored", $"{h.Name} не {h.G("двинулся", "двинулась")} с места: драться {h.G("он", "она")} не собирается.", 3, -1, h.Id);
                return;
            }

            // Опыт против совета: приёмы говорят, что нынешнее место лучше.
            int here = Techniques.ZoneBonus(rules, h, s.Mission, current);
            int there = Techniques.ZoneBonus(rules, h, s.Mission, zone);
            if (here - there >= c.DisagreeMargin)
            {
                s.Say("advice_disagree", $"{h.Name} не {h.G("согласен", "согласна")} с мастером: опыт подсказывает, что «{current.Name}» надёжнее.", 5, 0, h.Id);
                return;
            }

            int readiness = world.TowardMaster(h.Id).Trust
                            + (h.State.Resolve - 50) * 20 / 100
                            + (h.Traits.Discipline - 50) * 40 / 100
                            - confusion;
            if (c.FollowJitter > 0)
                readiness += world.Rng.Range(-c.FollowJitter, c.FollowJitter);

            if (readiness >= c.FollowThreshold)
                Follow(rules, s, bh, zone, $"{h.Name} {h.G("последовал", "последовала")} совету и идёт к позиции {where}.");
            else
                s.Say("advice_ignored", $"{h.Name} {h.G("пропустил", "пропустила")} совет мимо ушей.", 3, -1, h.Id);
        }

        private static void Follow(GameRules rules, BattleState s, BattleHero bh, ZoneDefinition zone, string text)
        {
            bh.TargetZone = zone.Id;
            bh.MoveCooldown = rules.Config.Battle.HeroMoveTicks;
            bh.FollowedAdvice.Add(zone.Id);
            s.Say("advice_followed", text, 3, 1, bh.Hero.Id);
        }
    }
}

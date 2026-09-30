using System;
using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>
    /// Игровой день: решения мастера → вылазка → жизнь на базе → ночь.
    /// Утренний отчёт — это просто события прошлого дня из лога.
    /// </summary>
    public static class DayEngine
    {
        public static List<GameEvent> RunDay(GameWorld world, GameRules rules, MasterDecisions decisions)
        {
            var errors = decisions.Validate(world, rules);
            if (errors.Count > 0)
                throw new InvalidOperationException("Решения мастера с ошибками:\n" + string.Join("\n", errors));

            world.Day++;
            long firstEventId = world.Log.NextId;

            MasterPhase.Run(world, rules, decisions);
            var expeditionMembers = ExpeditionPhase.Run(world, rules, decisions.Team);
            BaseLifePhase.Run(world, rules, decisions.Hints, expeditionMembers);
            NightPhase.Run(world, rules);

            return world.Log.Events.Where(e => e.Id >= firstEventId).ToList();
        }
    }
}

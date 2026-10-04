using System;
using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Battle;
using HeroMaster.Core.Events;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>
    /// Игровой день: решения мастера → вылазка (живой бой) → жизнь на базе → ночь → тексты дня
    /// (дневники, сводка, отчёт о бое; шаблоны — сразу, языковая модель может переписать их позже).
    /// День можно прожить целиком (<see cref="RunDay"/>) или по частям: начать, провести бой
    /// тик за тиком (панель показывает его в реальном времени) и закончить.
    /// </summary>
    public static class DayEngine
    {
        public static List<GameEvent> RunDay(GameWorld world, GameRules rules, MasterDecisions decisions)
        {
            var day = BeginDay(world, rules, decisions);
            if (day.Battle != null)
                BattleEngine.RunToEnd(world, rules, day.Battle);
            return FinishDay(world, rules, day);
        }

        /// <summary>Утро и решения мастера. Если есть команда — начинается бой, его надо провести до конца.</summary>
        public static DayInProgress BeginDay(GameWorld world, GameRules rules, MasterDecisions decisions)
        {
            var errors = decisions.Validate(world, rules);
            if (errors.Count > 0)
                throw new InvalidOperationException("Решения мастера с ошибками:\n" + string.Join("\n", errors));

            world.Day++;
            var day = new DayInProgress { Decisions = decisions, FirstEventId = world.Log.NextId };

            MasterPhase.Run(world, rules, decisions);

            if (decisions.Team.Count > 0)
            {
                var mission = rules.Mission(rules.Config.Expedition.MissionId);
                day.Battle = BattleEngine.Start(world, rules, mission, decisions.Team);
            }
            return day;
        }

        /// <summary>Итоги боя, жизнь на базе и ночь. Бой (если был) должен быть закончен.</summary>
        public static List<GameEvent> FinishDay(GameWorld world, GameRules rules, DayInProgress day)
        {
            var members = new List<string>();
            if (day.Battle != null)
            {
                if (!day.Battle.IsOver)
                    throw new InvalidOperationException("Бой ещё идёт.");
                members = BattleAftermath.Apply(world, rules, day.Battle);
            }

            BaseLifePhase.Run(world, rules, day.Decisions.Hints, members);
            NightPhase.Run(world, rules);

            var events = world.Log.Events.Where(e => e.Id >= day.FirstEventId).ToList();
            Narration.TemplateNarrator.WriteDay(world, rules, events);
            return events;
        }
    }

    /// <summary>Начатый, но ещё не законченный день.</summary>
    public sealed class DayInProgress
    {
        public MasterDecisions Decisions { get; set; } = new();
        public long FirstEventId { get; set; }
        public BattleState? Battle { get; set; }
    }
}

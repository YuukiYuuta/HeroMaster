using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>
    /// Как герой ведёт себя в бою прямо сейчас. Это не решение «раз и навсегда»:
    /// настрой меняется по ходу рейда — от ранений товарищей, от страха, от нападения на себя.
    /// </summary>
    public enum Conduct
    {
        /// <summary>Выкладывается по полной.</summary>
        AllIn,
        /// <summary>Честно сражается.</summary>
        Steady,
        /// <summary>Сражается вполсилы.</summary>
        HalfHearted,
        /// <summary>Отказался драться или оцепенел от страха; держится позади.</summary>
        Passive,
        /// <summary>Бойкот: бросил позицию, подставляя команду.</summary>
        Abandoned,
        /// <summary>Не хотел драться, но на него напали — отбивается, чтобы выжить.</summary>
        SelfDefense
    }

    public static class Combat
    {
        /// <summary>Настрой на входе в рейд по результату проверки послушания.</summary>
        public static Conduct StartingConduct(OrderResponse response)
        {
            switch (response)
            {
                case OrderResponse.Enthusiastic: return Conduct.AllIn;
                case OrderResponse.Complies: return Conduct.Steady;
                case OrderResponse.Grudging: return Conduct.HalfHearted;
                case OrderResponse.Refuses: return Conduct.Passive;
                default: return Conduct.Abandoned;
            }
        }

        /// <summary>
        /// Боевая сила: звёзды + опыт (не больше потолка для звёздности), минус усталость и раны.
        /// Именно она решает исход, а не рвение.
        /// </summary>
        public static int Power(GameRules rules, Hero hero)
        {
            var c = rules.Config.Combat;
            int fromExperience = System.Math.Min(hero.Experience / c.ExperiencePerPower, hero.Stars * c.ExperienceCapPerStar);
            int power = hero.Stars * c.PowerPerStar + fromExperience;
            power -= power * hero.State.Fatigue * c.FatiguePowerLossPercent / 10000;
            return System.Math.Max(1, power);
        }

        /// <summary>Какую долю своей силы герой вкладывает при таком настрое. Разброс небольшой: ±10%.</summary>
        public static int EffortPercent(GameRules rules, Conduct conduct)
        {
            var c = rules.Config.Combat;
            switch (conduct)
            {
                case Conduct.AllIn: return c.AllInEffortPercent;
                case Conduct.Steady: return 100;
                case Conduct.HalfHearted: return c.HalfHeartedEffortPercent;
                case Conduct.SelfDefense: return c.SelfDefenseEffortPercent;
                default: return 0;
            }
        }

        public static int EffectivePower(GameRules rules, Hero hero, Conduct conduct) =>
            Power(rules, hero) * EffortPercent(rules, conduct) / 100;

        public static bool IsFighting(Conduct conduct) => conduct != Conduct.Passive && conduct != Conduct.Abandoned;

        /// <summary>
        /// Товарища тяжело ранили на глазах. Шанс пасть духом: база − смелость + привязанность к раненому.
        /// Упавший духом сражается на ступень хуже; трус, уже дерущийся вполсилы, цепенеет от страха.
        /// Возвращает новый настрой (или прежний, если герой устоял).
        /// </summary>
        public static Conduct WitnessInjury(GameWorld world, GameRules rules, Hero witness, Hero injured, Conduct current)
        {
            if (!IsFighting(current) || current == Conduct.SelfDefense)
                return current;

            var c = rules.Config.Combat;
            int affection = world.GetRelationship(witness.Id, injured.Id).Affection;
            int chance = c.WitnessMoraleChance
                         - witness.Traits.Courage * c.WitnessCouragePercent / 100
                         + affection * c.WitnessAffectionPercent / 100;
            if (!world.Rng.Chance(GameWorld.Clamp(chance)))
                return current;

            switch (current)
            {
                case Conduct.AllIn: return Conduct.Steady;
                case Conduct.Steady: return Conduct.HalfHearted;
                default: return witness.Traits.Courage < c.PanicCourageBelow ? Conduct.Passive : Conduct.HalfHearted;
            }
        }
    }
}

using System;

namespace HeroMaster.Core.Random
{
    /// <summary>
    /// Детерминированный генератор случайных чисел PCG32.
    /// Одинаковое зерно даёт одинаковую последовательность на любой платформе
    /// (System.Random такого не гарантирует). Состояние сохраняется вместе с миром,
    /// поэтому загруженная игра продолжается ровно так же, как продолжилась бы без сохранения.
    /// </summary>
    public sealed class Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        public ulong State { get; set; }
        public ulong Increment { get; set; }

        /// <summary>Только для загрузки из сохранения.</summary>
        public Pcg32()
        {
            Increment = 1;
        }

        public Pcg32(ulong seed, ulong stream = 54)
        {
            Increment = (stream << 1) | 1UL;
            State = 0;
            NextUInt();
            State = unchecked(State + seed);
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = State;
            State = unchecked(old * Multiplier + Increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>Число в диапазоне [0, bound) без перекоса в пользу малых значений.</summary>
        public int Next(int bound)
        {
            if (bound <= 0)
                throw new ArgumentOutOfRangeException(nameof(bound), "Граница должна быть больше нуля.");

            uint b = (uint)bound;
            uint threshold = unchecked(0u - b) % b;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                    return (int)(r % b);
            }
        }

        /// <summary>Число в диапазоне [min, max] включительно.</summary>
        public int Range(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
                throw new ArgumentException("max меньше min.");
            return minInclusive + Next(maxInclusive - minInclusive + 1);
        }

        /// <summary>true с вероятностью percent из 100.</summary>
        public bool Chance(int percent) => Next(100) < percent;
    }
}

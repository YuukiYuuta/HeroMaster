using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    public static class TrustRules
    {
        /// <summary>Меняет доверие героя к мастеру в пределах 0–100. Возвращает фактическое изменение.</summary>
        public static int Change(GameWorld world, string heroId, int delta)
        {
            var r = world.TowardMaster(heroId);
            int before = r.Trust;
            r.Trust = GameWorld.Clamp(before + delta);
            return r.Trust - before;
        }

        /// <summary>Меняет отношение одного героя к другому по всем осям сразу.</summary>
        public static void ChangeRelationship(GameWorld world, string from, string to,
            int trust = 0, int affection = 0, int rivalry = 0, int respect = 0)
        {
            var r = world.GetRelationship(from, to);
            r.Trust = GameWorld.Clamp(r.Trust + trust);
            r.Affection = GameWorld.Clamp(r.Affection + affection);
            r.Rivalry = GameWorld.Clamp(r.Rivalry + rivalry);
            r.Respect = GameWorld.Clamp(r.Respect + respect);
        }

        /// <summary>Ранг статуса: 0 — в порядке, 3 — бойкот.</summary>
        public static int Rank(MoodStatus status) => (int)status;
    }
}

using HeroMaster.Core.Battle;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Persistence;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Web;

/// <summary>
/// Одна партия для панели мастера. Вся логика — в ядре; здесь только загрузка, сохранение
/// и перевод мира в удобный для страницы вид. Запросы обрабатываются по одному.
/// </summary>
public sealed class GameSession
{
    private readonly object _lock = new();
    private readonly string _savePath;
    private readonly GameRules _rules;
    private GameWorld? _world;
    /// <summary>Начатый день с идущим боем; null — день не начат.</summary>
    private DayInProgress? _day;

    public GameSession(string dataDir, string savePath)
    {
        _savePath = savePath;
        _rules = GameJson.LoadRules(dataDir);

        if (File.Exists(savePath))
        {
            try { _world = GameJson.LoadWorld(savePath); }
            catch (InvalidDataException) { _world = null; } // сохранение старого формата — начнём заново
        }
    }

    public object State()
    {
        lock (_lock)
            return BuildState();
    }

    public object NewGame(ulong? seed)
    {
        lock (_lock)
        {
            _day = null;
            _world = WorldFactory.Create(_rules.Catalog, _rules.Config, seed ?? (ulong)DateTime.UtcNow.Ticks);
            GameJson.SaveWorld(_world, _savePath);
            return BuildState();
        }
    }

    /// <summary>
    /// Начинает день. Если мастер отправил команду — день останавливается на живом бое,
    /// который страница показывает тик за тиком. Без вылазки день проживается сразу.
    /// Мир сохраняется только в конце дня, поэтому прерванный бой ничего не портит.
    /// </summary>
    public (List<string> errors, object? state) BeginDay(MasterDecisions decisions)
    {
        lock (_lock)
        {
            if (_world == null)
                return (new List<string> { "Партия не начата." }, null);
            if (_day != null)
                return (new List<string> { "Идёт бой — дождитесь его конца." }, null);

            var errors = decisions.Validate(_world, _rules);
            if (errors.Count > 0)
                return (errors, null);

            _day = DayEngine.BeginDay(_world, _rules, decisions);
            if (_day.Battle == null)
                return (errors, FinishDayLocked());
            return (errors, BuildState());
        }
    }

    /// <summary>Продвигает бой на несколько тиков. feedFrom — сколько строк ленты страница уже видела.</summary>
    public object? StepBattle(int ticks, int feedFrom)
    {
        lock (_lock)
        {
            if (_world == null || _day?.Battle == null)
                return null;
            for (int i = 0; i < Math.Clamp(ticks, 0, 50) && !_day.Battle.IsOver; i++)
                BattleEngine.Step(_world, _rules, _day.Battle);
            return BattleSnapshot(_day.Battle, feedFrom);
        }
    }

    /// <summary>Совет мастера в бою: держать позицию. heroId = null — всему отряду.</summary>
    public (List<string> errors, object? snapshot) Advise(string zoneId, string? heroId, int feedFrom)
    {
        lock (_lock)
        {
            if (_world == null || _day?.Battle == null)
                return (new List<string> { "Сейчас нет боя." }, null);
            var errors = MasterAdvice.Give(_world, _rules, _day.Battle, zoneId, string.IsNullOrEmpty(heroId) ? null : heroId);
            return (errors, errors.Count > 0 ? null : BattleSnapshot(_day.Battle, feedFrom));
        }
    }

    /// <summary>Возвращает (ошибки, состояние) после итогов боя, жизни на базе и ночи.</summary>
    public (List<string> errors, object? state) FinishDay()
    {
        lock (_lock)
        {
            if (_world == null || _day == null)
                return (new List<string> { "Нет начатого дня." }, null);
            if (_day.Battle != null && !_day.Battle.IsOver)
                return (new List<string> { "Бой ещё идёт." }, null);
            return (new List<string>(), FinishDayLocked());
        }
    }

    private object FinishDayLocked()
    {
        DayEngine.FinishDay(_world!, _rules, _day!);
        _day = null;
        GameJson.SaveWorld(_world!, _savePath);
        return BuildState();
    }

    private object BattleSnapshot(BattleState s, int feedFrom)
    {
        var nextWave = s.WavesSpawned < s.TotalWaves ? s.Mission.Waves[s.WavesSpawned].AtTick : (int?)null;
        return new
        {
            s.Tick,
            outcome = s.Outcome,
            wavesSpawned = s.WavesSpawned,
            totalWaves = s.TotalWaves,
            nextWaveTick = nextWave,
            s.Loot,
            monstersKilled = s.MonstersKilled,
            mvp = s.IsOver ? s.Mvp?.Hero.Id : null,
            mission = new
            {
                s.Mission.Name,
                s.Mission.Description,
                zones = s.Mission.Zones.Select(z => new { z.Id, z.Name, z.X, z.Y, z.Width, z.Cover, z.Spawn, z.Links })
            },
            heroes = s.Heroes.Select(h => new
            {
                h.Hero.Id,
                h.Hero.Name,
                h.Hero.NameGenitive,
                role = h.Hero.CombatRole,
                h.Zone,
                h.TargetZone,
                h.Hp,
                h.MaxHp,
                conduct = h.Conduct,
                h.Injured,
                h.Dead,
                h.Kills,
                h.DamageDealt,
                h.Healed,
                h.Score
            }),
            monsters = s.Monsters.Where(m => m.Alive)
                .GroupBy(m => new { m.Zone, m.Def.Id, m.Def.Name })
                .Select(g => new { g.Key.Zone, type = g.Key.Id, name = g.Key.Name, count = g.Count(), hp = g.Sum(m => m.Hp), maxHp = g.Count() * g.First().Def.Hp }),
            feedFrom = Math.Max(0, feedFrom),
            feed = s.Feed.Skip(Math.Max(0, feedFrom)).Select(f => new { f.Tick, f.Kind, f.Text, f.Importance, f.Emotion })
        };
    }

    public object? Hero(string id)
    {
        lock (_lock)
        {
            if (_world == null || !_world.HasHero(id))
                return null;

            var h = _world.GetHero(id);
            var valueText = _rules.Catalog.ValueCatalog.ToDictionary(v => v.Id, v => v.Text);
            var toMaster = _world.TowardMaster(id);
            return new
            {
                h.Id,
                h.Name,
                h.Stars,
                h.Profession,
                h.Bio,
                h.Dream,
                values = h.Values.Select(v => valueText.TryGetValue(v, out var t) ? t : v),
                traits = h.Traits.All().Select(t => new { id = t.Key, value = t.Value }),
                h.Experience,
                h.Purse,
                power = Combat.Power(_rules, h),
                sense = Positioning.Sense(_rules, h),
                techniques = h.Techniques.Where(t => t.Strength > 0).OrderByDescending(t => t.Strength).Select(t => new
                {
                    t.Action,
                    name = Techniques.Describe(t.Action),
                    t.Strength,
                    learned = t.Strength >= _rules.Config.Learning.LearnedThreshold,
                    t.Successes,
                    t.Failures,
                    from = t.LearnedFrom == Ids.Master ? "мастер" : _world.Heroes.FirstOrDefault(o => o.Id == t.LearnedFrom)?.Name ?? ""
                }),
                master = new { toMaster.Trust, toMaster.Respect, toMaster.Affection, toMaster.Fear },
                relations = _world.Heroes.Where(o => o.Id != id).Select(o =>
                {
                    var r = _world.GetRelationship(id, o.Id);
                    return new { o.Id, o.Name, r.Trust, r.Affection, r.Respect, r.Rivalry, r.Fear };
                }),
                recent = _world.Log.Events.Where(e => e.Actors.Contains(id)).Reverse().Take(15).Select(EventDto)
            };
        }
    }

    public object Log()
    {
        lock (_lock)
            return _world == null ? Array.Empty<object>() : _world.Log.Events.Select(EventDto).ToArray();
    }

    private object BuildState()
    {
        if (_world == null)
            return new { hasGame = false };

        var w = _world;
        var c = _rules.Config;
        return new
        {
            hasGame = true,
            w.Seed,
            w.Day,
            gold = w.Master.Gold,
            pendingLoot = w.Master.PendingLoot == null ? null : new
            {
                w.Master.PendingLoot.Amount,
                w.Master.PendingLoot.Participants
            },
            heroes = w.Heroes.Select(h => new
            {
                h.Id,
                h.Name,
                h.Stars,
                h.Profession,
                status = h.State.Status,
                trust = w.TowardMaster(h.Id).Trust,
                h.State.Resolve,
                h.State.FoundPurpose,
                h.State.Fatigue,
                h.State.Stress,
                h.Purse,
                power = Combat.Power(_rules, h)
            }),
            giftKinds = c.Gifts.Kinds.Select(k => new { k.Id, k.Name, k.Cost }),
            teamMin = c.Expedition.MinMembers,
            teamMax = c.Team.MaxSize,
            lastDay = w.Log.ForDay(w.Day).Select(EventDto),
            // Идущий бой: после перезагрузки страница продолжит показ с того же места.
            battle = _day?.Battle == null ? null : BattleSnapshot(_day.Battle, 0),
            fallen = w.Fallen.Select(f => new { f.Name, f.Stars, f.Profession, f.Day, f.Where, f.KilledBy })
        };
    }

    private static object EventDto(GameEvent e) => new
    {
        e.Id,
        e.Day,
        e.Phase,
        e.Type,
        e.Summary,
        e.Importance,
        e.Emotion,
        e.Actors
    };
}

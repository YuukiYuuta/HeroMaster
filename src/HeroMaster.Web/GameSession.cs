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
            _world = WorldFactory.Create(_rules.Catalog, _rules.Config, seed ?? (ulong)DateTime.UtcNow.Ticks);
            GameJson.SaveWorld(_world, _savePath);
            return BuildState();
        }
    }

    /// <summary>Возвращает (ошибки, состояние). Если ошибки есть — день не прожит.</summary>
    public (List<string> errors, object? state) RunDay(MasterDecisions decisions)
    {
        lock (_lock)
        {
            if (_world == null)
                return (new List<string> { "Партия не начата." }, null);

            var errors = decisions.Validate(_world, _rules);
            if (errors.Count > 0)
                return (errors, null);

            DayEngine.RunDay(_world, _rules, decisions);
            GameJson.SaveWorld(_world, _savePath);
            return (errors, BuildState());
        }
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
            lastDay = w.Log.ForDay(w.Day).Select(EventDto)
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

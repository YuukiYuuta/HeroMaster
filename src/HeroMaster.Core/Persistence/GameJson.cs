using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroMaster.Core.Config;
using HeroMaster.Core.Content;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Persistence
{
    /// <summary>Чтение и запись данных игры и сохранений в JSON.</summary>
    public static class GameJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // Кириллица остаётся читаемой, без \u-последовательностей.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static T Deserialize<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidDataException($"Пустой JSON для {typeof(T).Name}.");

        public static HeroCatalog LoadCatalog(string path)
        {
            var catalog = Deserialize<HeroCatalog>(File.ReadAllText(path, Encoding.UTF8));
            ThrowIfInvalid(path, catalog.Validate());
            return catalog;
        }

        public static BalanceConfig LoadConfig(string path)
        {
            var config = Deserialize<BalanceConfig>(File.ReadAllText(path, Encoding.UTF8));
            ThrowIfInvalid(path, config.Validate());
            return config;
        }

        /// <summary>
        /// Загружает все правила из папки data: heroes.json, balance.json, monsters.json и missions/*.json.
        /// Любая ошибка в данных — понятное сообщение со списком проблем.
        /// </summary>
        public static Simulation.GameRules LoadRules(string dataDir)
        {
            var catalog = LoadCatalog(Path.Combine(dataDir, "heroes.json"));
            var config = LoadConfig(Path.Combine(dataDir, "balance.json"));

            var monstersPath = Path.Combine(dataDir, "monsters.json");
            var monsters = Deserialize<Battle.MonsterCatalog>(File.ReadAllText(monstersPath, Encoding.UTF8));
            ThrowIfInvalid(monstersPath, monsters.Validate());

            var missions = new System.Collections.Generic.List<Battle.MissionDefinition>();
            foreach (var path in Directory.GetFiles(Path.Combine(dataDir, "missions"), "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                var mission = Deserialize<Battle.MissionDefinition>(File.ReadAllText(path, Encoding.UTF8));
                ThrowIfInvalid(path, mission.Validate(monsters));
                missions.Add(mission);
            }

            var rules = new Simulation.GameRules(catalog, config, monsters, missions);
            if (missions.All(m => m.Id != config.Expedition.MissionId))
                throw new InvalidDataException($"В balance.json указана миссия «{config.Expedition.MissionId}», но такой нет в data/missions.");
            return rules;
        }

        public static void SaveWorld(GameWorld world, string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Сначала пишем во временный файл, потом подменяем: сбой посреди записи не испортит сохранение.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(world), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        public static GameWorld LoadWorld(string path)
        {
            var world = Deserialize<GameWorld>(File.ReadAllText(path, Encoding.UTF8));
            if (world.FormatVersion != GameWorld.CurrentFormatVersion)
                throw new InvalidDataException(
                    $"Сохранение версии {world.FormatVersion}, а игра читает версию {GameWorld.CurrentFormatVersion}.");
            return world;
        }

        private static void ThrowIfInvalid(string path, System.Collections.Generic.List<string> errors)
        {
            if (errors.Any())
                throw new InvalidDataException($"Ошибки в {path}:\n" + string.Join("\n", errors));
        }
    }
}

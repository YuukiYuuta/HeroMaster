# Мастер героев — прототип (этап 0)

Текстовый прототип симуляции автономных героев. Цель этапа — проверить, ощущаются ли герои живыми без прямого общения с ними.

## Структура

- `data/heroes.json` — герои, их черты, ценности и стартовые отношения.
- `data/balance.json` — все числа баланса (пороги доверия, скорость обучения и т. д.).
- `src/HeroMaster.Core` — ядро симуляции (netstandard2.1, C# 9 — совместимо с Unity).
- `src/HeroMaster.Cli` — консольная оболочка для игры в прототип.
- `tests/HeroMaster.Core.Tests` — автотесты ядра.

## Команды

```bash
dotnet test
```

```bash
dotnet run --project src/HeroMaster.Cli -- new 42
```

```bash
dotnet run --project src/HeroMaster.Cli -- roster
```

```bash
dotnet run --project src/HeroMaster.Cli -- log
```

`new` без числа берёт случайное зерно. Одно и то же зерно всегда даёт одинаковое начало партии.
Сохранение лежит в `saves/world.json`.

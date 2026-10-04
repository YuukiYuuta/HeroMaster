using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroMaster.Core.Simulation;
using HeroMaster.Web;

// Панель мастера — локальная страница для игры в прототип и его проверки.
// Запуск: dotnet run --project src/HeroMaster.Web, затем http://localhost:5080

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5080");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var savePath = Path.Combine(FindProjectRoot(builder.Environment.ContentRootPath), "saves", "world.json");
var narrator = new AiNarrator(builder.Configuration);
var usage = new AiUsageLog(Path.Combine(Path.GetDirectoryName(savePath)!, "ai_usage.json"));
builder.Services.AddSingleton(new GameSession(dataDir, savePath, narrator, usage));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", (GameSession game) => game.State());
app.MapPost("/api/new", (GameSession game, NewGameRequest request) => game.NewGame(request.Seed));
// День: начать (если есть команда — начнётся живой бой), вести бой тик за тиком, закончить.
app.MapPost("/api/day/begin", (GameSession game, MasterDecisions decisions) =>
{
    var (errors, state) = game.BeginDay(decisions);
    return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(state);
});
app.MapPost("/api/battle/step", (GameSession game, StepRequest request) =>
    game.StepBattle(request.Ticks, request.FeedFrom) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());
app.MapPost("/api/battle/advice", (GameSession game, AdviceRequest request) =>
{
    var (errors, snapshot) = game.Advise(request.Zone, request.HeroId, request.FeedFrom);
    return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(snapshot);
});
app.MapPost("/api/day/finish", (GameSession game) =>
{
    var (errors, state) = game.FinishDay();
    return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(state);
});
app.MapGet("/api/hero/{id}", (GameSession game, string id) => game.Hero(id) is { } hero ? Results.Ok(hero) : Results.NotFound());
app.MapGet("/api/log", (GameSession game) => game.Log());

app.Run();

// Сохранение лежит в папке saves рядом с HeroMaster.slnx — там же, где у консольной версии.
static string FindProjectRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "HeroMaster.slnx")))
            return dir.FullName;
    return start;
}

record NewGameRequest(ulong? Seed);
record StepRequest(int Ticks, int FeedFrom);
record AdviceRequest(string Zone, string? HeroId, int FeedFrom);

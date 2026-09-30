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
builder.Services.AddSingleton(new GameSession(dataDir, savePath));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", (GameSession game) => game.State());
app.MapPost("/api/new", (GameSession game, NewGameRequest request) => game.NewGame(request.Seed));
app.MapPost("/api/day", (GameSession game, MasterDecisions decisions) =>
{
    var (errors, state) = game.RunDay(decisions);
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

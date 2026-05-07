using Zhuiying.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("tmdbproxy", c => c.BaseAddress = new Uri("http://tmdbproxy:5001"));
builder.Services.AddCors();
builder.Services.AddSingleton<TgMessageStore>();

var app = builder.Build();
app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

var store = app.Services.GetRequiredService<TgMessageStore>();

// 首页
app.MapGet("/", () => Results.Json(new {
    status = "ok",
    service = "Zhuiying MainSite",
    endpoints = new[] { "/", "/api/movies", "/api/tg/messages", "/api/tg/messages (POST)" }
}));

// 获取影视列表 (调用 TMDB 代理)
app.MapGet("/api/movies", async (IHttpClientFactory f, string? q = null) =>
{
    var client = f.CreateClient("tmdbproxy");
    var url = string.IsNullOrWhiteSpace(q) ? "api/trending" : $"api/search?q={Uri.EscapeDataString(q)}";
    var resp = await client.GetAsync(url);
    return resp.IsSuccessStatusCode ? Results.Content(await resp.Content.ReadAsStringAsync(), "application/json") : Results.StatusCode((int)resp.StatusCode);
});

// 接收 TG Bot 推送的消息
app.MapPost("/api/tg/messages", (TgMessageDto msg) =>
{
    store.Add(msg);
    Console.WriteLine($"[TG] Received: {msg.Username} -> {msg.Text}");
    return Results.Ok(new { success = true });
});

// 查询收到的 TG 消息
app.MapGet("/api/tg/messages", () => Results.Ok(store.GetAll()));

app.Run();

public class TgMessageStore
{
    private readonly List<TgMessageDto> _messages = new();
    public void Add(TgMessageDto msg) { _messages.Insert(0, msg); }
    public IEnumerable<TgMessageDto> GetAll() => _messages.Take(100);
}

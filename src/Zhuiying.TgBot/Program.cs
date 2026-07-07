using System.Net.Http.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Zhuiying.Shared;

var builder = Host.CreateApplicationBuilder(args);
var botToken = builder.Configuration["Telegram:BotToken"]
    ?? Environment.GetEnvironmentVariable("TG_BOT_TOKEN")
    ?? "YOUR_BOT_TOKEN_HERE";
var mainSiteUrl = builder.Configuration["MainSite:Url"]
    ?? Environment.GetEnvironmentVariable("MAIN_SITE_URL")
    ?? "http://mainsite:5000";

builder.Services.AddHttpClient("mainsite", c => c.BaseAddress = new Uri(mainSiteUrl));
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));

var host = builder.Build();
host.Run();

public class BotWorker(ITelegramBotClient bot, IHttpClientFactory httpFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
        bot.StartReceiving(
            async (b, update, ct) =>
            {
                if (update.Message?.Text is string text && update.Message.Chat is Chat chat)
                {
                    var msg = new TgMessageDto(
                        chat.Id,
                        chat.Username ?? $"user_{chat.Id}",
                        text,
                        DateTime.UtcNow
                    );
                    // 推送给主站
                    try
                    {
                        var resp = await httpFactory.CreateClient("mainsite").PostAsJsonAsync("api/tg/messages", msg, ct);
                        Console.WriteLine($"[Bot] Pushed to MainSite: {resp.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Bot] Push failed: {ex.Message}");
                    }
                    // 回复用户
                    await b.SendMessage(chat.Id, $"已收到你的消息: {text}", cancellationToken: ct);
                }
            },
            (b, ex, ct) => Task.CompletedTask,
            receiverOptions
        );
    }
}

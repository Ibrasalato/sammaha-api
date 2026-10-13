using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;

namespace Sammha.Infrastructure.Services;

public record WebhookMessage(string Url, object Body);

/// <summary>In-memory queue: a request never waits on, or fails because of, n8n.</summary>
public class WebhookQueue : IWebhookPublisher
{
    private readonly Channel<WebhookMessage> _channel = Channel.CreateBounded<WebhookMessage>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest });
    public ChannelReader<WebhookMessage> Reader => _channel.Reader;

    public Task PublishAsync(string url, object body)
    {
        if (!string.IsNullOrWhiteSpace(url)) _channel.Writer.TryWrite(new WebhookMessage(url, body));
        return Task.CompletedTask;
    }
}

public class WebhookSender(WebhookQueue queue, IHttpClientFactory http, ILogger<WebhookSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var client = http.CreateClient("webhooks");
        await foreach (var m in queue.Reader.ReadAllAsync(stop))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var res = await client.PostAsJsonAsync(m.Url, m.Body, stop);
                    if (res.IsSuccessStatusCode) break;
                    log.LogWarning("Webhook {Url} answered {Status}", m.Url, (int)res.StatusCode);
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Webhook {Url} failed (attempt {N})", m.Url, attempt); }
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), stop);
            }
        }
    }
}

/// <summary>One-time codes go to the SMS provider through an n8n webhook (Integrations:SmsWebhookUrl).</summary>
public class WebhookSmsSender(IWebhookPublisher webhooks, IOptions<IntegrationOptions> opt) : ISmsSender
{
    public Task SendCodeAsync(string target, string code, string purpose) =>
        string.IsNullOrWhiteSpace(opt.Value.SmsWebhookUrl) ? Task.CompletedTask
            : webhooks.PublishAsync(opt.Value.SmsWebhookUrl, new { to = target, code, purpose, text = $"رمز التحقق في سمّها: {code}" });
}

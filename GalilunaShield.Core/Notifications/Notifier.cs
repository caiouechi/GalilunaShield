using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text;
using GalilunaShield.Configuration;

namespace GalilunaShield.Notifications;

/// <summary>
/// Sends the parent a message when a red flag is detected and they are away from the computer, by email and/or
/// a push webhook (ntfy, Telegram, Slack, Discord, or a generic endpoint). Runs entirely from this computer;
/// no server of ours is involved. Failures are logged, never thrown, so a mail problem never stops monitoring.
/// </summary>
public sealed class Notifier : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly NotificationConfig _config;
    private readonly Action<LogLevel, string> _log;
    private readonly Dictionary<string, DateTimeOffset> _lastPerSource = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public Notifier(NotificationConfig config, Action<LogLevel, string> log)
    {
        _config = config;
        _log = log;
    }

    public bool AnyChannelConfigured => _config.Enabled && (_config.Email.Enabled || _config.Webhook.Enabled);

    /// <summary>Called for each alert. Sends notifications if enabled, severe enough, and not rate-limited.</summary>
    public void Notify(AlertRecord alert)
    {
        if (!_config.Enabled || alert.Severity < _config.MinSeverity) return;

        lock (_gate)
        {
            var key = alert.Source;
            if (_lastPerSource.TryGetValue(key, out var last) && DateTimeOffset.Now - last < TimeSpan.FromMinutes(_config.MinMinutesBetween))
            {
                return;
            }
            _lastPerSource[key] = DateTimeOffset.Now;
        }

        var (title, body) = Compose(alert);
        _ = Task.Run(async () =>
        {
            if (_config.Email.Enabled) await SendEmailAsync(title, body);
            if (_config.Webhook.Enabled) await SendWebhookAsync(title, body, alert.Severity);
        });
    }

    private (string Title, string Body) Compose(AlertRecord a)
    {
        var kind = a.PossibleOnly ? "Possible red flag" : $"{a.Severity} red flag";
        var where = a.HeardWhere ?? a.Source;
        var title = $"Galiluna Shield: {kind}";
        var sb = new StringBuilder();
        sb.AppendLine($"{kind} detected at {a.At:HH:mm} on {a.At:ddd d MMM}.");
        sb.AppendLine($"Heard on: {where}");
        sb.AppendLine($"Matched: {string.Join(", ", a.Matches.Select(m => m.Word))}");
        if (_config.IncludeText) sb.AppendLine($"Said: \"{a.Text}\"");
        sb.AppendLine();
        sb.AppendLine("Open Galiluna Shield on the computer, or the parent dashboard, to hear the clip and see the screen.");
        return (title, sb.ToString());
    }

    private async Task SendEmailAsync(string subject, string body)
    {
        var e = _config.Email;
        if (string.IsNullOrWhiteSpace(e.SmtpHost) || string.IsNullOrWhiteSpace(e.To)) return;
        try
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(string.IsNullOrWhiteSpace(e.From) ? e.Username : e.From),
                Subject = subject,
                Body = body,
            };
            foreach (var to in e.To.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                msg.To.Add(to);
            }
            using var client = new SmtpClient(e.SmtpHost, e.SmtpPort) { EnableSsl = e.UseSsl };
            if (!string.IsNullOrEmpty(e.Username))
            {
                client.Credentials = new NetworkCredential(e.Username, e.Password);
            }
            await client.SendMailAsync(msg);
            _log(LogLevel.Info, "Alert email sent to the parent.");
        }
        catch (Exception ex)
        {
            _log(LogLevel.Warning, $"Could not send the alert email: {ex.Message}");
        }
    }

    private async Task SendWebhookAsync(string title, string body, Severity severity)
    {
        var w = _config.Webhook;
        try
        {
            HttpResponseMessage resp;
            switch (w.Type.ToLowerInvariant())
            {
                case "ntfy":
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, w.Url) { Content = new StringContent(body, Encoding.UTF8) };
                    req.Headers.TryAddWithoutValidation("Title", title);
                    req.Headers.TryAddWithoutValidation("Priority", severity >= Severity.Critical ? "urgent" : severity >= Severity.High ? "high" : "default");
                    req.Headers.TryAddWithoutValidation("Tags", "rotating_light,shield");
                    resp = await Http.SendAsync(req);
                    break;
                }
                case "telegram":
                {
                    var url = $"https://api.telegram.org/bot{w.BotToken}/sendMessage";
                    resp = await Http.PostAsJsonAsync(url, new { chat_id = w.ChatId, text = $"{title}\n\n{body}" });
                    break;
                }
                case "slack":
                    resp = await Http.PostAsJsonAsync(w.Url, new { text = $"*{title}*\n{body}" });
                    break;
                case "discord":
                    resp = await Http.PostAsJsonAsync(w.Url, new { content = $"**{title}**\n{body}" });
                    break;
                default:
                    resp = await Http.PostAsJsonAsync(w.Url, new { title, message = body, severity = severity.ToString() });
                    break;
            }

            if (resp.IsSuccessStatusCode) _log(LogLevel.Info, "Alert push notification sent.");
            else _log(LogLevel.Warning, $"Push notification rejected ({(int)resp.StatusCode}).");
        }
        catch (Exception ex)
        {
            _log(LogLevel.Warning, $"Could not send the push notification: {ex.Message}");
        }
    }

    /// <summary>Sends a test notification through all enabled channels; returns a human-readable result.</summary>
    public async Task<string> SendTestAsync()
    {
        var results = new List<string>();
        if (_config.Email.Enabled)
        {
            await SendEmailAsync("Galiluna Shield test", "This is a test alert from Galiluna Shield. If you received it, email notifications are working.");
            results.Add("email attempted");
        }
        if (_config.Webhook.Enabled)
        {
            await SendWebhookAsync("Galiluna Shield test", "This is a test alert. If you received it, push notifications are working.", Severity.Low);
            results.Add("push attempted");
        }
        return results.Count == 0 ? "No channels are enabled." : "Test sent (" + string.Join(", ", results) + "). Check your inbox / phone.";
    }

    public void Dispose() { }
}

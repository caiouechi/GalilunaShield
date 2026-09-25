using System.Text.Json.Serialization;

namespace GalilunaShield.Configuration;

/// <summary>The local web dashboard the parent opens from their phone/browser on the home network.</summary>
public sealed class WebDashboardConfig
{
    public bool Enabled { get; set; } = false;
    /// <summary>Port the dashboard listens on. The parent opens http://this-computer-ip:Port on their phone.</summary>
    public int Port { get; set; } = 8787;
}

/// <summary>How the parent is alerted when they are away from the child's computer.</summary>
public sealed class NotificationConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Only notify for this severity and above (Low = everything, Critical = only the worst).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
    public Severity MinSeverity { get; set; } = Severity.High;

    /// <summary>Never send more than one notification per source within this many minutes (avoids floods).</summary>
    public double MinMinutesBetween { get; set; } = 2;

    /// <summary>Include the flagged sentence in the notification. Turn off if you prefer not to send content off-device.</summary>
    public bool IncludeText { get; set; } = true;

    public EmailNotificationConfig Email { get; set; } = new();
    public WebhookNotificationConfig Webhook { get; set; } = new();
}

public sealed class EmailNotificationConfig
{
    public bool Enabled { get; set; } = false;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    /// <summary>SMTP password / app password. Stored locally; consider an app-specific password.</summary>
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    /// <summary>Where the alert emails go (the parent's address). Comma-separated for several.</summary>
    public string To { get; set; } = "";
}

/// <summary>
/// A push notification via a simple HTTP POST. Works with free services like ntfy.sh and Telegram bots,
/// or any endpoint that accepts a JSON/text POST — no backend of ours required.
/// </summary>
public sealed class WebhookNotificationConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>generic | ntfy | telegram | slack | discord</summary>
    public string Type { get; set; } = "ntfy";

    /// <summary>
    /// For ntfy: the topic URL, e.g. https://ntfy.sh/your-secret-topic (install the ntfy app, subscribe to
    /// that topic). For telegram: leave blank and set BotToken + ChatId. For slack/discord/generic: the
    /// incoming-webhook URL.
    /// </summary>
    public string Url { get; set; } = "";

    // Telegram
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
}

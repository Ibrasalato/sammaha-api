namespace Sammha.Application.Common;

public class OtpOptions
{
    /// <summary>fixed code until an SMS provider is connected; leave empty for random codes sent by SMS</summary>
    public string? FixedCode { get; set; } = "4321";
    public int Length { get; set; } = 4;
    public int TtlMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>Section "Integrations": n8n webhooks.</summary>
public class IntegrationOptions
{
    /// <summary>every in-app notification is forwarded here (n8n sends SMS / WhatsApp)</summary>
    public string? NotifyWebhookUrl { get; set; }
    /// <summary>one-time codes go through this webhook to the SMS provider</summary>
    public string? SmsWebhookUrl { get; set; }
    /// <summary>ticket emails (n8n «Sammha 6 — Tickets Email»)</summary>
    public string? TicketWebhookUrl { get; set; }
    /// <summary>e-invoice emails</summary>
    public string? InvoiceWebhookUrl { get; set; }
    /// <summary>public site, for links in emails</summary>
    public string SiteUrl { get; set; } = "https://ibrasalato.github.io/sammaha/";
}

public class PaymentOptions
{
    /// <summary>development: card payments succeed at once without Moyasar</summary>
    public bool Simulate { get; set; } = true;
    public string? MoyasarSecretKey { get; set; }
    /// <summary>the secret_token configured on the Moyasar webhook</summary>
    public string? WebhookSecret { get; set; }
    /// <summary>"hyperpay" or "moyasar" (default: hyperpay once its access token is set, otherwise moyasar)</summary>
    public string? Provider { get; set; }
    /// <summary>this API's public address, for the HyperPay payment page and its return (e.g. https://api.sammha.ico.org.sa)</summary>
    public string? PublicApiUrl { get; set; }
    public HyperPayOptions HyperPay { get; set; } = new();
}

/// <summary>HyperPay COPYandPAY (OPPWA). Each channel has its own entity id from HyperPay; a channel without one is hidden.</summary>
public class HyperPayOptions
{
    /// <summary>https://eu-test.oppwa.com/ (test) or https://eu-prod.oppwa.com/ (live)</summary>
    public string BaseUrl { get; set; } = "https://eu-test.oppwa.com/";
    public string? AccessToken { get; set; }
    /// <summary>Visa / Mastercard</summary>
    public string? EntityId { get; set; }
    public string? MadaEntityId { get; set; }
    public string? ApplePayEntityId { get; set; }
    /// <summary>test only: "EXTERNAL" (simulated 3-D Secure); leave empty in production</summary>
    public string? TestMode { get; set; }
    public bool Configured => !string.IsNullOrWhiteSpace(AccessToken) && (!string.IsNullOrWhiteSpace(EntityId) || !string.IsNullOrWhiteSpace(MadaEntityId) || !string.IsNullOrWhiteSpace(ApplePayEntityId));
}

/// <summary>the payment channels an owner can pick</summary>
public static class PayChannels
{
    public const string Mada = "mada", Card = "card", ApplePay = "applepay";
    public static readonly string[] All = [Mada, Card, ApplePay];
}

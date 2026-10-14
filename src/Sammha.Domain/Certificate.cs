namespace Sammha.Domain;

/// <summary>
/// A bilingual registration certificate the owner requested, issued automatically and attested by the International Camel Organization.
/// It covers one mark or all the owner's marks, and keeps a snapshot of them at issue time; verification compares it with today's registry.
/// </summary>
public class Certificate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>SC-2026-000001</summary>
    public string Number { get; set; } = "";
    public Guid OwnerId { get; set; }
    /// <summary>mark | portfolio</summary>
    public string Scope { get; set; } = CertificateScopes.Mark;
    public string OwnerNameAr { get; set; } = "";
    public string OwnerNameEn { get; set; } = "";
    public string? NationalId { get; set; }
    public string? Nationality { get; set; }
    /// <summary>JSON snapshot of the marks: id, name, latin, type, acquiredAt, registry code, source</summary>
    public string ItemsJson { get; set; } = "[]";
    public int ItemCount { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    /// <summary>HMAC of the content, printed on the certificate and carried in its QR code</summary>
    public string Signature { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }
}

public static class CertificateScopes
{
    public const string Mark = "mark", Portfolio = "portfolio";
}

/// <summary>a mark as it stood when the certificate was issued</summary>
public record CertificateItem(Guid Id, string Name, string Latin, string Type, DateTimeOffset AcquiredAt, string RegistryCode, string Source, string? BaseName);

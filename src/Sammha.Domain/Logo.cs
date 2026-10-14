namespace Sammha.Domain;

/// <summary>the logo an owner uploads for a mark they own; shown publicly once the committee approves it</summary>
public class NameLogo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReservedId { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "image/png";
    public byte[] Bytes { get; set; } = [];
    public string Status { get; set; } = LogoStatuses.Pending;
    public string? RejectReason { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public static class LogoStatuses
{
    public const string Pending = "pending", Approved = "approved", Rejected = "rejected";
}

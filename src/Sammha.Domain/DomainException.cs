namespace Sammha.Domain;

/// <summary>A business rule was broken. The message is Arabic and shown to the user as-is.</summary>
public class DomainException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
    public static DomainException NotFound(string message) => new(message, 404);
    public static DomainException Forbidden(string message) => new(message, 403);
    public static DomainException Unauthorized(string message = "سجّل الدخول أولًا") => new(message, 401);
}

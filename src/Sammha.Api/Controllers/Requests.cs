using Sammha.Application.Common;

namespace Sammha.Api.Controllers;

// request bodies (camelCase JSON from the React client)
public record SignInBody(string Login, string Password, string Portal);
public record CodeBody(string Challenge, string Code);
public record ChallengeBody(string Challenge);
public record PasswordBody(string Current, string Next);
public record RefreshBody(string RefreshToken);
public record SignOutBody(string? RefreshToken, bool Everywhere = false);
public record ProfileBody(string? AvatarUrl, bool AvatarSet, bool ShowAvatar);
public record AccountBody(string FullName, string? NationalId);
public record EmailBody(string Email);
public record PhoneBody(string Phone);
public record NamesBody(string Type, List<string> Names);
public record CheckBody(List<string> Names, string? Type);
public record CompoundBody(Guid ReservedId, string FullName);
public record ReasonBody(string? Reason);
public record WithdrawBody(Guid CandidateId);
public record AmountBody(decimal Amount);
public record MaxBody(decimal Max);
public record ListingBody(Guid ReservedId, string Mode, decimal Price);
public record TrackBody(string Number, string Phone);
public record FollowUpBody(string Number, string Phone, string Body);
public record ReviewBody(List<ReviewDecision> Decisions, string? Note);
public record IcoAuctionBody(DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal? OpeningPrice);
public record ScheduleBody(List<Guid> CandidateIds, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int StaggerMinutes = 0);
public record RoleBody(string Role);
public record ActiveBody(bool Active);
public record TicketPatch(string? Status, string? Priority, string? Assignee);
public record ReplyBody(string Body, bool Internal);
public record CardBody(string Purpose, Guid? Ref, decimal Amount, string Callback);
public record TopUpBody(decimal Amount, string? Callback);

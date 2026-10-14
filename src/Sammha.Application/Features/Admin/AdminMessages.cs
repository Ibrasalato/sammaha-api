using Sammha.Application.Common;

namespace Sammha.Application.Features.Admin;

public record GetOverviewQuery : IQuery<AdminOverviewDto>;
public record ListRequestsQuery(string? Status) : IQuery<List<RequestDto>>;
public record ReviewRequestCommand(Guid Id, List<ReviewDecision> Decisions, string? Note) : ICommand<RequestDto>;
public record RejectRequestCommand(Guid Id, string Reason) : ICommand<RequestDto>;
public record CancelRequestCommand(Guid Id, string Reason) : ICommand<RequestDto>;
public record GetAwaitingScheduleQuery : IQuery<List<AwaitingDto>>;
/// <summary>StaggerMinutes &gt; 0 releases the marks one after another: each auction starts and ends that many minutes after the previous one, in the order given</summary>
public record ScheduleAuctionsCommand(List<Guid> CandidateIds, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int StaggerMinutes = 0) : ICommand<List<AuctionDto>>;
public record ListInvoicesQuery : IQuery<List<InvoiceDto>>;
public record MarkInvoicePaidCommand(Guid Id) : ICommand<InvoiceDto>;
public record ListUsersQuery : IQuery<List<UserDto>>;
public record CreateStaffCommand(StaffInput Input) : ICommand<UserDto>;
public record SetUserRoleCommand(Guid Id, string Role) : ICommand<UserDto>;
public record SetUserActiveCommand(Guid Id, bool Active) : ICommand<UserDto>;
public record SaveEventCommand(CamelEventDto Event) : ICommand<CamelEventDto>;
public record DeleteEventCommand(string Id) : ICommand<MediatR.Unit>;

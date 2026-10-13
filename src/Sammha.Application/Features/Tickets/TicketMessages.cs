using Sammha.Application.Common;

namespace Sammha.Application.Features.Tickets;

public record SubmitTicketCommand(TicketInput Input) : ICommand<TicketDto>;
public record TrackTicketQuery(string Number, string Phone) : IQuery<TicketDto?>;
public record FollowUpTicketCommand(string Number, string Phone, string Body) : ICommand<TicketDto>;
public record GetMyTicketsQuery : IQuery<List<TicketDto>>;
public record ListTicketsQuery : IQuery<List<TicketDto>>;
public record GetTicketQuery(Guid Id) : IQuery<TicketDto>;
public record UpdateTicketCommand(Guid Id, string? Status, string? Priority, string? Assignee) : ICommand<TicketDto>;
public record ReplyTicketCommand(Guid Id, string Body, bool Internal) : ICommand<TicketDto>;

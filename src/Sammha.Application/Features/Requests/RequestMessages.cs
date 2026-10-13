using Sammha.Application.Common;

namespace Sammha.Application.Features.Requests;

public record GetMyRequestsQuery : IQuery<List<RequestDto>>;
public record GetRequestQuery(Guid Id) : IQuery<RequestDto>;
public record CreateRequestCommand(string Type, List<string> Names) : ICommand<RequestDto>;
public record CreateCompoundRequestCommand(Guid ReservedId, string FullName) : ICommand<RequestDto>;
public record CreateInvestmentRequestCommand(string Type, List<string> Names) : ICommand<RequestDto>;
public record GetMyInvestmentsQuery : IQuery<List<InvestmentItemDto>>;
public record PayDepositCommand(Guid RequestId) : ICommand<RequestDto>;
public record CancelMyRequestCommand(Guid RequestId, string? Reason) : ICommand<CancelResult>;
public record WithdrawNameCommand(Guid RequestId, Guid CandidateId) : ICommand<WithdrawResult>;

public record CancelResult(decimal Refunded);
public record WithdrawResult(decimal Refunded, bool RequestCancelled);

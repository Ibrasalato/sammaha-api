using Sammha.Application.Common;

namespace Sammha.Application.Features.Wallet;

public record GetWalletQuery : IQuery<WalletDto>;
public record GetMyInvoicesQuery : IQuery<List<InvoiceDto>>;
public record PayInvoiceCommand(Guid InvoiceId) : ICommand<InvoiceDto>;
public record GetMyNamesQuery : IQuery<List<ReservedNameDto>>;
public record GetNotificationsQuery : IQuery<List<NotificationDto>>;
public record MarkNotificationsReadCommand : ICommand<MediatR.Unit>;

using System.Text.Json;
using Sammha.Application.Common;

namespace Sammha.Application.Features.Payments;

public record StartCardPaymentCommand(string Purpose, Guid? Ref, decimal Amount, string CallbackUrl, string? Method = null) : ICommand<CardCheckout>;
/// <summary>the owner came back from HyperPay: ask for the result, then credit and complete the purpose</summary>
public record CompleteHyperPayCommand(Guid PaymentId, string? CheckoutId) : ICommand<HyperPayResult>;
public record HyperPayResult(bool Paid, bool Pending, string? CallbackUrl, string? Message);
/// <summary>what the HyperPay page needs to show the widget</summary>
public record GetHyperPayFormQuery(Guid PaymentId) : IQuery<HyperPayFormDto?>;
public record HyperPayFormDto(string CheckoutId, string? Integrity, string? Method, decimal Amount, string Purpose, string? CallbackUrl);
public record TopUpCommand(decimal Amount, string CallbackUrl) : ICommand<WalletDto>;
public record MoyasarWebhookCommand(JsonElement Body) : ICommand<MediatR.Unit>;

public record CardCheckout(string? Url, bool Simulated);

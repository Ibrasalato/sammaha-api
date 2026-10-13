using System.Text.Json;
using Sammha.Application.Common;

namespace Sammha.Application.Features.Payments;

public record StartCardPaymentCommand(string Purpose, Guid? Ref, decimal Amount, string CallbackUrl) : ICommand<CardCheckout>;
public record TopUpCommand(decimal Amount, string CallbackUrl) : ICommand<WalletDto>;
public record MoyasarWebhookCommand(JsonElement Body) : ICommand<MediatR.Unit>;

public record CardCheckout(string? Url, bool Simulated);

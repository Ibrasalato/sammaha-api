using Sammha.Application.Common;

namespace Sammha.Application.Features.Bidding;

public record PlaceBidCommand(Guid AuctionId, decimal Amount) : ICommand<AuctionViewDto>;
public record SetAutoBidCommand(Guid AuctionId, decimal Max) : ICommand<AuctionViewDto>;
public record CancelAutoBidCommand(Guid AuctionId) : ICommand<AuctionViewDto>;
public record ToggleWatchCommand(Guid AuctionId) : ICommand<WatchResult>;
public record PayBidDepositCommand(Guid AuctionId) : ICommand<AuctionViewDto>;
public record GetWatchlistQuery : IQuery<List<AuctionDto>>;
public record GetMyBidsQuery : IQuery<List<MyBidDto>>;

public record WatchResult(bool Watching);

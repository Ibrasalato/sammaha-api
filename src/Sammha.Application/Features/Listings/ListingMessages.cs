using Sammha.Application.Common;

namespace Sammha.Application.Features.Listings;

public record CreateListingCommand(Guid ReservedId, string Mode, decimal Price) : ICommand<ListingDto>;
public record CancelListingCommand(Guid ListingId) : ICommand<ListingDto>;
public record GetMyListingsQuery : IQuery<List<ListingDto>>;
public record GetMarketQuery : IQuery<List<ListingDto>>;
public record BuyListingCommand(Guid ListingId) : ICommand<ListingDto>;

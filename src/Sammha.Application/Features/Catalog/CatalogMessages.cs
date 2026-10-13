using Sammha.Application.Common;

namespace Sammha.Application.Features.Catalog;

public record GetStatsQuery : IQuery<PublicStatsDto>;
public record ListAuctionsQuery(AuctionFilter Filter) : IQuery<List<AuctionDto>>;
public record GetAuctionQuery(Guid Id) : IQuery<AuctionViewDto>;
public record ListReservedNamesQuery(string? Q, string? Type) : IQuery<List<ReservedNameDto>>;
public record CheckNamesQuery(List<string> Names) : IQuery<List<NameCheckDto>>;
public record ListEventsQuery : IQuery<List<CamelEventDto>>;
public record VerifyCertificateQuery(string Code) : IQuery<ReservedNameDto?>;

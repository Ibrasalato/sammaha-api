namespace Sammha.Domain;

/// <summary>Business rules (section "Platform" in appsettings). Mirrors RULES in the front end's config.ts.</summary>
public class PlatformOptions
{
    public decimal DepositAmount { get; set; } = 50;
    public decimal OpeningPrice { get; set; } = 50;
    public decimal MinIncrement { get; set; } = 500;
    /// <summary>opening price of «منقية» names; null = the same as OpeningPrice</summary>
    public decimal? ManqiyaOpeningPrice { get; set; } = 3000;
    /// <summary>default length in days offered when the committee schedules an auction</summary>
    public int AuctionDays { get; set; } = 2;
    public int ExtendMinutes { get; set; } = 2;
    public int NamesPerRequest { get; set; } = 3;
    public decimal VatRate { get; set; } = 0.15m;
    public string TimeZone { get; set; } = "Asia/Riyadh";
    public double SimilarThreshold { get; set; } = 0.7;

    public decimal InvestFee { get; set; } = 100;
    public decimal InvestOwnerShare { get; set; } = 0.30m;
    public int InvestMaxNames { get; set; } = 3;

    public int ResaleNoticeHours { get; set; } = 72;
    public int ResaleAuctionDays { get; set; } = 3;
    public decimal ResaleCommission { get; set; } = 0.10m;
    public decimal ResaleBidDeposit { get; set; } = 50;
    public int InstantSaleHours { get; set; } = 72;
    public decimal InstantCommission { get; set; } = 0.15m;

    /// <summary>the super admin account (cannot be demoted or suspended)</summary>
    public string SuperAdminEmail { get; set; } = "ibrasalato@gmail.com";
}

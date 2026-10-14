IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE SEQUENCE [InvoiceNo] AS int START WITH 1 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE SEQUENCE [ListingNo] AS int START WITH 1001 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE SEQUENCE [RequestNo] AS int START WITH 1001 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE SEQUENCE [TicketNo] AS int START WITH 1 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Auctions] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NULL,
        [CandidateId] uniqueidentifier NULL,
        [ListingId] uniqueidentifier NULL,
        [Name] nvarchar(400) NOT NULL,
        [Type] nvarchar(400) NOT NULL,
        [RequesterId] uniqueidentifier NOT NULL,
        [OpeningPrice] decimal(12,2) NOT NULL,
        [MinIncrement] decimal(12,2) NOT NULL,
        [StartsAt] datetimeoffset NOT NULL,
        [EndsAt] datetimeoffset NOT NULL,
        [CurrentPrice] decimal(12,2) NOT NULL,
        [BidCount] int NOT NULL,
        [LeaderId] uniqueidentifier NULL,
        [Settled] bit NOT NULL,
        [WinnerId] uniqueidentifier NULL,
        [FinalPrice] decimal(12,2) NULL,
        [WonByRequester] bit NULL,
        [Extensions] int NOT NULL,
        [IsInvestment] bit NOT NULL,
        [BidDeposit] decimal(12,2) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Auctions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [AutoBids] (
        [AuctionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Max] decimal(12,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AutoBids] PRIMARY KEY ([AuctionId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [BidDeposits] (
        [AuctionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [State] nvarchar(400) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_BidDeposits] PRIMARY KEY ([AuctionId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Bids] (
        [Id] uniqueidentifier NOT NULL,
        [AuctionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [Auto] bit NOT NULL,
        CONSTRAINT [PK_Bids] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Events] (
        [Id] nvarchar(60) NOT NULL,
        [Title] nvarchar(400) NOT NULL,
        [TitleEn] nvarchar(400) NOT NULL,
        [Kind] nvarchar(400) NOT NULL,
        [StartsOn] nvarchar(400) NOT NULL,
        [EndsOn] nvarchar(400) NOT NULL,
        [Approx] bit NOT NULL,
        [City] nvarchar(400) NOT NULL,
        [Country] nvarchar(400) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Url] nvarchar(400) NULL,
        [CityEn] nvarchar(400) NULL,
        [CountryEn] nvarchar(400) NULL,
        [DescriptionEn] nvarchar(2000) NULL,
        CONSTRAINT [PK_Events] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Listings] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(400) NOT NULL,
        [ReservedId] uniqueidentifier NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Type] nvarchar(400) NOT NULL,
        [SellerId] uniqueidentifier NOT NULL,
        [Mode] nvarchar(400) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [CommissionRate] decimal(12,2) NOT NULL,
        [Status] nvarchar(400) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [StartsAt] datetimeoffset NOT NULL,
        [EndsAt] datetimeoffset NOT NULL,
        [AuctionId] uniqueidentifier NULL,
        [SoldPrice] decimal(12,2) NULL,
        [SoldAt] datetimeoffset NULL,
        [Payout] decimal(12,2) NULL,
        [PayoutPaid] bit NOT NULL,
        CONSTRAINT [PK_Listings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(400) NOT NULL,
        [Title] nvarchar(400) NOT NULL,
        [Body] nvarchar(400) NOT NULL,
        [Link] nvarchar(400) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [Read] bit NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [OtpChallenges] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Purpose] nvarchar(400) NOT NULL,
        [Target] nvarchar(400) NOT NULL,
        [CodeHash] nvarchar(400) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [Attempts] int NOT NULL,
        [Used] bit NOT NULL,
        CONSTRAINT [PK_OtpChallenges] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Purpose] nvarchar(400) NOT NULL,
        [Ref] uniqueidentifier NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Status] nvarchar(400) NOT NULL,
        [ProviderId] nvarchar(400) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [PaidAt] datetimeoffset NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [ReservedNames] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [Normalized] nvarchar(400) NOT NULL,
        [Type] nvarchar(400) NOT NULL,
        [OwnerId] uniqueidentifier NULL,
        [OwnerName] nvarchar(400) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [AcquiredAt] datetimeoffset NOT NULL,
        [Source] nvarchar(400) NOT NULL,
        [BaseName] nvarchar(400) NULL,
        [AuctionId] uniqueidentifier NULL,
        [CertCode] nvarchar(400) NOT NULL,
        CONSTRAINT [PK_ReservedNames] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Tickets] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(400) NOT NULL,
        [Kind] nvarchar(400) NOT NULL,
        [Topic] nvarchar(400) NOT NULL,
        [Subject] nvarchar(400) NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Phone] nvarchar(400) NOT NULL,
        [Email] nvarchar(400) NULL,
        [RefNumber] nvarchar(400) NULL,
        [Message] nvarchar(3000) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Status] nvarchar(400) NOT NULL,
        [Priority] nvarchar(400) NOT NULL,
        [Assignee] nvarchar(400) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Tickets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(120) NOT NULL,
        [Phone] nvarchar(10) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [NationalId] nvarchar(400) NULL,
        [Role] nvarchar(20) NOT NULL,
        [PasswordHash] nvarchar(400) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [AvatarUrl] nvarchar(max) NULL,
        [ShowAvatar] bit NOT NULL,
        [Active] bit NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [WalletTxs] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Kind] nvarchar(400) NOT NULL,
        [Note] nvarchar(400) NOT NULL,
        [Ref] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WalletTxs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Watches] (
        [AuctionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [NotifiedStart] bit NOT NULL,
        [NotifiedEnd] bit NOT NULL,
        CONSTRAINT [PK_Watches] PRIMARY KEY ([AuctionId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [TicketMessages] (
        [Id] uniqueidentifier NOT NULL,
        [TicketId] uniqueidentifier NOT NULL,
        [By] nvarchar(400) NOT NULL,
        [Author] nvarchar(400) NOT NULL,
        [Body] nvarchar(3000) NOT NULL,
        [Internal] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_TicketMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TicketMessages_Tickets_TicketId] FOREIGN KEY ([TicketId]) REFERENCES [Tickets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(400) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [AuctionId] uniqueidentifier NULL,
        [ListingId] uniqueidentifier NULL,
        [Name] nvarchar(400) NOT NULL,
        [Type] nvarchar(400) NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [Vat] decimal(12,2) NOT NULL,
        [Subtotal] decimal(12,2) NOT NULL,
        [DepositApplied] decimal(12,2) NOT NULL,
        [AmountDue] decimal(12,2) NOT NULL,
        [Status] nvarchar(400) NOT NULL,
        [IssuedAt] datetimeoffset NOT NULL,
        [PaidAt] datetimeoffset NULL,
        [OwnerShare] decimal(12,2) NULL,
        [OwnerShareTo] uniqueidentifier NULL,
        [OwnerSharePaid] bit NOT NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Requests] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(400) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Type] nvarchar(400) NOT NULL,
        [Kind] nvarchar(400) NOT NULL,
        [Status] nvarchar(400) NOT NULL,
        [DepositAmount] decimal(12,2) NOT NULL,
        [DepositPaid] bit NOT NULL,
        [DepositState] nvarchar(400) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SubmittedAt] datetimeoffset NULL,
        [ReviewedAt] datetimeoffset NULL,
        [ReviewerId] uniqueidentifier NULL,
        [ReviewerName] nvarchar(400) NULL,
        [ReviewNote] nvarchar(1000) NULL,
        [CancelledAt] datetimeoffset NULL,
        [CancelledBy] nvarchar(400) NULL,
        [CancelReason] nvarchar(400) NULL,
        [BaseReservedId] uniqueidentifier NULL,
        [BaseName] nvarchar(400) NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Requests_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE TABLE [Candidates] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [Position] int NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Normalized] nvarchar(400) NOT NULL,
        [Status] nvarchar(400) NOT NULL,
        [SimilarJson] nvarchar(max) NOT NULL,
        [RejectReason] nvarchar(400) NULL,
        [AuctionId] uniqueidentifier NULL,
        [WithdrawnAt] datetimeoffset NULL,
        CONSTRAINT [PK_Candidates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Candidates_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Auctions_Settled_EndsAt] ON [Auctions] ([Settled], [EndsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Auctions_StartsAt] ON [Auctions] ([StartsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Bids_AuctionId_Amount] ON [Bids] ([AuctionId], [Amount]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Candidates_Normalized] ON [Candidates] ([Normalized]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Candidates_RequestId] ON [Candidates] ([RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Invoices_AuctionId] ON [Invoices] ([AuctionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_Number] ON [Invoices] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Invoices_UserId] ON [Invoices] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Listings_Mode_Status] ON [Listings] ([Mode], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Listings_Number] ON [Listings] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Listings_ReservedId_Status] ON [Listings] ([ReservedId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_CreatedAt] ON [Notifications] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Payments_ProviderId] ON [Payments] ([ProviderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Requests_Number] ON [Requests] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Requests_Status] ON [Requests] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Requests_UserId_CreatedAt] ON [Requests] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_ReservedNames_CertCode] ON [ReservedNames] ([CertCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReservedNames_Normalized] ON [ReservedNames] ([Normalized]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_ReservedNames_OwnerId] ON [ReservedNames] ([OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_TicketMessages_TicketId] ON [TicketMessages] ([TicketId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tickets_Number] ON [Tickets] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_Tickets_Phone] ON [Tickets] ([Phone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Phone] ON [Users] ([Phone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    CREATE INDEX [IX_WalletTxs_UserId_CreatedAt] ON [WalletTxs] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161522_Initial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013161522_Initial', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013163037_RefreshTokens'
)
BEGIN
    ALTER TABLE [Users] ADD [SecurityStamp] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013163037_RefreshTokens'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [FamilyId] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [RevokedReason] nvarchar(400) NULL,
        [ReplacedById] uniqueidentifier NULL,
        [CreatedByIp] nvarchar(400) NULL,
        [UserAgent] nvarchar(400) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013163037_RefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_FamilyId] ON [RefreshTokens] ([FamilyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013163037_RefreshTokens'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013163037_RefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_RevokedAt] ON [RefreshTokens] ([UserId], [RevokedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013163037_RefreshTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013163037_RefreshTokens', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [FailedLogins] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [LastLoginAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [LockedUntil] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [NotifyOutbid] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [NotifyReminders] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [NotifyRequests] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [NotifyResults] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    CREATE TABLE [PlatformSettings] (
        [Id] int NOT NULL,
        [RequestsOpen] bit NOT NULL,
        [RequestsClosedNote] nvarchar(300) NULL,
        [CompoundOpen] bit NOT NULL,
        [CompoundClosedNote] nvarchar(300) NULL,
        [ResaleAuctionOpen] bit NOT NULL,
        [InstantSaleOpen] bit NOT NULL,
        [SaleClosedNote] nvarchar(300) NULL,
        [InvestmentOpen] bit NOT NULL,
        [InvestmentClosedNote] nvarchar(300) NULL,
        [SignupOpen] bit NOT NULL,
        [SupportEmail] nvarchar(160) NOT NULL,
        [SupportPhone] nvarchar(30) NOT NULL,
        [WhatsApp] nvarchar(30) NOT NULL,
        [AnnouncementOn] bit NOT NULL,
        [Announcement] nvarchar(400) NULL,
        [AnnouncementTone] nvarchar(10) NOT NULL,
        [Maintenance] bit NOT NULL,
        [MaintenanceNote] nvarchar(300) NULL,
        [PasswordMinLength] int NOT NULL,
        [MaxLoginAttempts] int NOT NULL,
        [LockoutMinutes] int NOT NULL,
        [StaffIdleMinutes] int NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(120) NULL,
        CONSTRAINT [PK_PlatformSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformSettings_Single] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    CREATE TABLE [SettingsChanges] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetimeoffset NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [UserName] nvarchar(120) NOT NULL,
        [Field] nvarchar(60) NOT NULL,
        [OldValue] nvarchar(400) NULL,
        [NewValue] nvarchar(400) NULL,
        CONSTRAINT [PK_SettingsChanges] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    CREATE INDEX [IX_SettingsChanges_At] ON [SettingsChanges] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014043820_PlatformSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014043820_PlatformSettings', N'10.0.13');
END;

COMMIT;
GO


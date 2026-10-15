using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sammha.Domain;
using Sammha.Infrastructure.Data;
using Sammha.Infrastructure.Legacy;

namespace Sammha.Tests;

/// <summary>a small copy of the previous platform's schema, imported into a fresh database</summary>
public class LegacyImportTests(TestApp app) : IClassFixture<TestApp>, IAsyncLifetime
{
    private static readonly string Server = Environment.GetEnvironmentVariable("SAMMHA_TEST_DB") ?? "Server=localhost,1433;User Id=sa;Password=Sammha_dev_2026!;TrustServerCertificate=True";
    private readonly string _name = "SammhaLegacy_" + Guid.NewGuid().ToString("N")[..8];
    private string Conn => $"{Server};Database={_name}";
    private static readonly string OldHash = new Microsoft.AspNetCore.Identity.PasswordHasher<object>().HashPassword(new object(), "Old@pass1");

    public async Task InitializeAsync()
    {
        await using (var c = new SqlConnection(Server)) { await c.OpenAsync(); await Exec(c, $"CREATE DATABASE [{_name}]"); }
        await using var db = new SqlConnection(Conn);
        await db.OpenAsync();
        var past = DateTime.UtcNow.AddHours(3).AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss");
        var past2 = DateTime.UtcNow.AddHours(3).AddDays(-29).ToString("yyyy-MM-ddTHH:mm:ss");
        var future = DateTime.UtcNow.AddHours(3).AddDays(5).ToString("yyyy-MM-ddTHH:mm:ss");
        await Exec(db, """
            CREATE TABLE C_Sex (ID int, Sex_Name nvarchar(50));
            INSERT C_Sex VALUES (1, N'ذكر'), (2, N'أنثى');
            CREATE TABLE C_Nationality (ID int, Nationality_Code nvarchar(128), Nationality_NameAr nvarchar(128));
            INSERT C_Nationality VALUES (1, 'SA', N'سعودي');
            CREATE TABLE C_ReservationType (ID int, ResType_Name nvarchar(50));
            INSERT C_ReservationType VALUES (1, N'فردية'), (2, N'منقية');
            CREATE TABLE C_Order_Status (ID int, ORD_Status_Name nvarchar(50));
            INSERT C_Order_Status VALUES (1, N'جديد'), (2, N'مقبول'), (3, N'مرفوض');
            CREATE TABLE C_Bidding_Status (ID int, Bidding_Status nvarchar(50));
            INSERT C_Bidding_Status VALUES (1, N'جاري'), (2, N'منتهي'), (3, N'ملغي');
            CREATE TABLE Black_List (ID int, BL_Member_FK int, BL_StartDate date, BL_EndDate date);
            INSERT Black_List VALUES (1, 3, '2024-01-01', NULL);
            CREATE TABLE Members (ID int, MEM_FirstName nvarchar(50), MEM_SecondName nvarchar(50), MEM_ThirdName nvarchar(50), MEM_FamilyName nvarchar(50),
              MEM_Sex_FK int, MEM_Nationality_FK int, MEM_ID_No nvarchar(50), MEM_Password nvarchar(200), MEM_DOB date, MEM_Mobile nvarchar(50), MEM_Mobile_Key nvarchar(10),
              MEM_Register_Date date, MEM_Status_FK int, MEM_Email nvarchar(100), WalletAmmount float, MEM_BankCardIBANNumber nvarchar(255), MEM_BankCardBankName nvarchar(255), MEM_BankCardUserName nvarchar(255));
            CREATE TABLE Orders (ID int, Order_Date date, Order_Member_FK int, Order_First_Choice nvarchar(150), Order_Second_Choice nvarchar(150), Order_Third_Choice nvarchar(150),
              Order_First_Selected bit, Order_Second_Selected bit, Order_Third_Selected bit, Order_First_Status_FK int, Order_Second_Status_FK int, Order_Third_Status_FK int,
              Order_ResType_FK int, Order_First_IsConvertedToBid bit, Order_Second_IsConvertedToBid bit, Order_Third_IsConvertedToBid bit,
              Order_First_BID_ID int, Order_Second_BID_ID int, Order_Third_BID_ID int, Order_First_RejectReasone nvarchar(1000), Order_Second_RejectReasone nvarchar(1000), Order_Third_RejectReasone nvarchar(1000),
              IsBillPaied bit, PaiedDateTime datetime, Order_Sex_FK int, Order_First_Exception bit, Order_Second_Exception bit, Order_Third_Exception bit, IsInvestment bit);
            CREATE TABLE Biddings (ID int, Bid_Order_FK int, Bid_OrderOption_FK int, Bid_Name nvarchar(250), Bid_Start datetime, Bid_End datetime, Bid_Status_FK int,
              Bid_StartValue int, Bid_MaxVlaue int, InvestedByMember int, InvestedPersenategValue int);
            CREATE TABLE Biddings_Details (ID int, BidDet_Master_FK int, BidDet_Datetime datetime, BidDet_Member_FK int, BidDet_Value int);
            CREATE TABLE Biddings_Winners (ID int, BidWin_BidCode_FK int, BidWin_Member_FK int, BidWin_Value int, BidWin_Status_FK int, BidWin_Date datetime, BidWin_IsFinalWinner bit, BidWin_Withdrawal_Date datetime);
            CREATE TABLE Bills (ID int, Bill_Type_FK int, Bill_Member_FK int, Bill_Data_FK int, Bill_Value float, Bill_VAT float, Bill_Total float, Bill_Datetime datetime,
              Bill_IsPaied bit, Bill_PayDateTime datetime, Bill_IsCancled bit, Bill_IsRefunded bit, Bill_PaiedRelatedToBillId int, Bill_IsRefundedToWallit bit, Bill_RefundedDate datetime);
            CREATE TABLE CompoundNames_Master (ID int, CompReg_User_FK int, CompReg_Date datetime, CompReg_Fee float, CompReg_Vat float, CompReg_FullValue float, CompReg_IsBillPaied bit, CompReg_PaiedDateTime datetime);
            CREATE TABLE CompoundNames_Details (ID int, CompDet_Master_FK int, CompDet_Name nvarchar(150), CompDet_Name_F nvarchar(150));
            CREATE TABLE Members_Notifications (ID int, MemNotf_Date nvarchar(50), MemNotf_MEM_ID_FK int, MemNotf_Description nvarchar(1000), MemNotf_IsSeen bit, MemNotf_Link nvarchar(500));
            CREATE TABLE C_RequestType (Id int, Name nchar(100));
            INSERT C_RequestType VALUES (1, N'نقل ملكية'), (2, N'طرح للبيع'), (3, N'طلب اسم مستثنى');
            CREATE TABLE C_RequestTypeType (Id int, ReuestTypeId int, Name nvarchar(255), PriceNoVat float, StateCode bit);
            INSERT C_RequestTypeType VALUES (2, 3, N'استثناء فردي بكار', 5000, 0), (17, 3, N'استثناء سيف الملك', 10000, 0);
            CREATE TABLE C_RequestStatus (Id int, Name nchar(100));
            INSERT C_RequestStatus VALUES (1, N'جديد'), (2, N'تمت الموافقة'), (3, N'مرفوض'), (4, N'تم التنفيذ');
            CREATE TABLE Request (Id int, RequestTypeId int, RequestStatusId int, MemberId int, Title nvarchar(500), Name nvarchar(500), Biddings_WinnersId int, ToMemberId int,
              DateTime datetime, LatestNote nvarchar(500), LatestNoteBy nvarchar(500), LatestUpdateDateTime datetime, BillId int, RejectResion nvarchar(500), RequestTypeTypeId int);
            CREATE TABLE PaymentCard (ID int, CardNumber nvarchar(50));
            INSERT PaymentCard VALUES (1, '4111111111111111');
            CREATE TABLE C_Settings (ID int, Sett_GuaranteeValue int, Sett_GuaranteeValue_Withdrawn int, Sett_Fardeyya_BidStartValue int, Sett_Manqeyya_BidStartValue int,
              Sett_Fardeyya_BidMinValue int, Sett_Fardeyya_BidMaxValue int, Sett_Default_BidPeriod_InDays int, Sett_Fardeyya_ResFees int, Sett_Manqeyya_ResFees int);
            INSERT C_Settings VALUES (1, 5000, 10000, 50, 3000, 500, 500, 2, 5000, 5000);
            CREATE TABLE ContactUs (ID int, Cont_Name nvarchar(1000), Cont_Mobile nvarchar(20), Cont_Tilte nvarchar(500), Cont_Msg nvarchar(3000), Cont_Date datetime);
            INSERT ContactUs VALUES (1, N'سعد', '0551110001', N'استفسار عن المزاد', N'متى يبدأ مزاد الاسم؟', '2024-03-01T10:00:00'),
              (2, N'زائر', '0559999999', NULL, N'أريد التواصل مع الإدارة', '2024-03-02T11:00:00'), (3, N'فارغ', '0559999998', N'', N'  ', '2024-03-03T11:00:00');
            """);
        await Exec(db, $"""
            INSERT Members VALUES
              (1, N'سعد', N'محمد', N'علي', N'القحطاني', 1, 1, '1000000001', '{OldHash}', '1980-05-01', '551110001', '966', '2022-01-10', 1, 'saad@old.example', 250.5, 'SA0380000000608010167519', N'الراجحي', N'سعد القحطاني'),
              (2, N'ماجد', N'فهد', N'سالم', N'العتيبي', 1, 1, '1000000002', '{OldHash}', '1985-02-02', '0551110002', '', '2022-02-10', 1, NULL, 0, NULL, NULL, NULL),
              (3, N'خالد', N'ناصر', N'سعد', N'الدوسري', 1, 1, '1000000003', '{OldHash}', '1990-03-03', '0551110003', '966', '2022-03-10', 1, 'k@old.example', 0, NULL, NULL, NULL),
              (4, N'سعد', N'محمد', N'علي', N'القحطاني', 1, 1, '1000000001', 'plain-old', NULL, '0551110001', '966', '2023-01-10', 1, 'saad@old.example', 0, NULL, NULL, NULL);
            INSERT Orders VALUES
              (5001, '{past}', 1, N'شعار الغيم البعيد', N'شعار الريح الغربية', NULL, 1, 1, 0, 2, 3, 1, 1, 1, 0, 0, 7001, NULL, NULL, NULL, N'مشابه لاسم مسجل', NULL, 1, '{past}', 1, 0, 0, 0, 0),
              (5002, '{past}', 2, N'شعار وادي القمر', NULL, NULL, 1, 0, 0, 2, 1, 1, 1, 1, 0, 0, 7002, NULL, NULL, NULL, NULL, NULL, 1, '{past}', 2, 1, 0, 0, 0),
              (5003, '{past}', 1, N'شعار الفجر الساطع', NULL, NULL, 1, 0, 0, 2, 1, 1, 2, 1, 0, 0, 7003, NULL, NULL, NULL, NULL, NULL, 1, '{past}', 1, 0, 0, 0, 0);
            INSERT Biddings VALUES
              (7001, 5001, 1, N'شعار الغيم البعيد', '{past}', '{past2}', 2, 500, 1200, NULL, NULL),
              (7002, 5002, 1, N'شعار وادي القمر', '{past}', '{past2}', 2, 500, 900, NULL, NULL),
              (7003, 5003, 1, N'شعار الفجر الساطع', '{past}', '{future}', 1, 500, 600, NULL, NULL);
            INSERT Biddings_Details VALUES
              (1, 7001, '{past}', 2, 1000), (2, 7001, '{past}', 1, 1200), (3, 7002, '{past}', 2, 900), (4, 7003, '{past}', 2, 600);
            INSERT Biddings_Winners VALUES (1, 7001, 1, 1200, 1, '{past2}', 1, NULL), (2, 7002, 2, 900, 1, '{past2}', 1, NULL);
            INSERT Bills VALUES
              (9001, 1, 1, 5001, 4347.83, 652.17, 5000, '{past}', 1, '{past}', 0, 0, NULL, 0, NULL),
              (9002, 1, 2, 5002, 4347.83, 652.17, 5000, '{past}', 1, '{past}', 0, 0, NULL, 0, NULL),
              (9003, 4, 1, 7001, 1043.48, 156.52, 1200, '{past2}', 1, '{past2}', 0, 0, NULL, 0, NULL),
              (9004, 2, 2, 7001, 4347.83, 652.17, 5000, '{past}', 1, '{past}', 0, 0, NULL, 0, NULL),
              (9005, 3, 2, 7001, 5000, 0, 5000, '{past2}', 1, '{past2}', 0, 0, 9004, 1, '{past2}'),
              (9006, 4, 2, 7002, 782.61, 117.39, 900, '{past2}', 0, NULL, 0, 0, NULL, 0, NULL),
              (9007, 6, 1, NULL, 300, 0, 300, '{past}', 1, '{past}', 0, 0, NULL, 0, NULL),
              (9008, 1, 1, 5003, 4347.83, 652.17, 5000, '{past}', 1, '{past}', 0, 0, NULL, 0, NULL),
              (9009, 7, 1, 7003, 100, 0, 100, '{past}', 1, '{past}', 0, 0, NULL, 0, NULL);
            INSERT CompoundNames_Master VALUES (8001, 1, '{past2}', 100, 15, 115, 1, '{past2}');
            INSERT CompoundNames_Details VALUES (1, 8001, N'شعار الغيم البعيد الثاني', NULL), (2, 8001, N'شعار الغيم البعيد الثالث', NULL);
            INSERT Request VALUES (1, 1, 2, 1, N'هبة', N'شعار الغيم البعيد الثالث', NULL, 2, '{past2}', NULL, N'الإدارة', '{past2}', NULL, NULL, NULL), (2, 2, 1, 1, N'بيع', N'شعار الغيم البعيد', 1, NULL, '{past2}', NULL, NULL, NULL, NULL, NULL, NULL),
              (3, 3, 4, 1, N'مشاركة بدون اسم', N'الشريحة 900115000123456', NULL, NULL, '{past2}', NULL, N'اللجنة', '{past2}', NULL, NULL, 2),
              (4, 3, 3, 2, N'استثناء', N'رقم الشريحة: 900115000654321', NULL, NULL, '{past2}', NULL, N'اللجنة', '{past2}', NULL, N'الشريحة مربوطة باسم', 17);
            INSERT Members_Notifications VALUES (1, '15/03/2025 10:30:00', 1, N'تم قبول طلبك', 1, NULL), (2, 'garbage', 1, N'رسالة قديمة', 0, NULL);
            """);
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var c = new SqlConnection(Server);
        await c.OpenAsync();
        await Exec(c, $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]");
    }

    private static async Task Exec(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<LegacyReport> Run(bool commit)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var imp = scope.ServiceProvider.GetRequiredService<LegacyImporter>();
        await using var src = new LegacySource(Conn);
        var map = await LegacyImporter.AutoMapAsync(src, CancellationToken.None);
        return await imp.ImportAsync(src, map, commit, CancellationToken.None);
    }

    [Fact]
    public async Task Imports_the_old_platform_once()
    {
        // inspect lists sizes and lookups and warns about stored cards
        await using (var scope = app.Services.CreateAsyncScope())
        await using (var src = new LegacySource(Conn))
        {
            var w = new StringWriter();
            await scope.ServiceProvider.GetRequiredService<LegacyImporter>().InspectAsync(src, w, CancellationToken.None);
            Assert.Contains("PaymentCard", w.ToString());
            Assert.Contains("rejected", w.ToString());
        }

        // a dry run saves nothing
        var dry = await Run(false);
        Assert.Equal(4, dry.Counts["members"]);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SammhaDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Phone == "0551110001"));
            Assert.False(await db.Settings.AnyAsync(x => x.ManqiyaOpeningPrice == 3000m));
        }

        var rep = await Run(true);
        Assert.Equal(4, rep.Counts["members"]);
        Assert.Equal(1, rep.Warnings["members.duplicatePhone (placeholder phone)"]);
        Assert.Equal(1, rep.Warnings["members.duplicateNationalId"]);
        // C_Settings: the guarantee, both opening prices, the minimum raise and the auction length
        Assert.Equal(5, rep.Counts["settings.fromLegacy"]);
        Assert.Equal(1, rep.Warnings["settings.guaranteeWithdrawnIgnored"]);
        // ContactUs → closed enquiry tickets; the one from a member's mobile is linked to the account
        Assert.Equal(2, rep.Counts["tickets"]);
        Assert.Equal(1, rep.Warnings["tickets.emptySkipped"]);
        Assert.Equal(1, rep.Warnings["tickets.noMatchingAccount"]);

        // the old password still works
        var saad = await app.SignInAsync("0551110001", "Old@pass1");
        Assert.Equal(250.5m, await saad.Balance());

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SammhaDbContext>();
            var s = await db.Users.SingleAsync(u => u.Phone == "0551110001");
            var m = await db.Users.SingleAsync(u => u.Phone == "0551110002");
            Assert.Equal("سعد محمد علي القحطاني", s.FullName);
            Assert.Equal("SA", s.Nationality);
            Assert.Equal("male", s.Gender);
            Assert.StartsWith("member2@", m.Email);
            Assert.False((await db.Users.SingleAsync(u => u.Phone == "0551110003")).Active);
            Assert.True(await db.Users.AnyAsync(u => u.Phone == "L-4" && u.NationalId == null));
            Assert.True(await db.BankAccounts.AnyAsync(b => b.UserId == s.Id && b.Status == BankAccountStatuses.Pending));
            Assert.Equal(0m, await db.BalanceOfAsync(m.Id));

            var r1 = await db.Requests.Include(r => r.Candidates).SingleAsync(r => r.Number == "SM-5001");
            Assert.Equal(RequestStatuses.Accepted, r1.Status);
            Assert.Equal(NameTypes.Qaadan, r1.Type);
            Assert.Equal(CandidateStatuses.Rejected, r1.Candidates.Single(c => c.Position == 2).Status);
            Assert.Equal(NameTypes.Bakkar, (await db.Requests.SingleAsync(r => r.Number == "SM-5002")).Type);
            Assert.Equal(DepositStates.Forfeited, (await db.Requests.SingleAsync(r => r.Number == "SM-5002")).DepositState);
            Assert.Equal(NameTypes.Manqiya, (await db.Requests.SingleAsync(r => r.Number == "SM-5003")).Type);

            var won = await db.ReservedNames.SingleAsync(n => n.Name == "شعار الغيم البعيد");
            Assert.Equal(s.Id, won.OwnerId);
            Assert.Equal(1200m, won.Price);
            Assert.True(await db.Invoices.AnyAsync(i => i.AuctionId == won.AuctionId && i.Status == InvoiceStatuses.Paid && i.UserId == s.Id));
            Assert.Equal(BidDepositStates.Refunded, (await db.BidDeposits.SingleAsync(d => d.AuctionId == won.AuctionId)).State);
            Assert.Equal(2, await db.Bids.CountAsync(b => b.AuctionId == won.AuctionId));

            var ico = await db.ReservedNames.SingleAsync(n => n.Name == "شعار وادي القمر");
            Assert.Null(ico.OwnerId);
            Assert.Equal(ReservedSources.Exception, ico.Source);
            Assert.Equal(m.Id, ico.ForfeitedFromId);
            Assert.Equal(5000m, ico.ForfeitedDeposit);

            var live = await db.Auctions.SingleAsync(a => a.Number == "AU-7003");
            Assert.False(live.Settled);
            Assert.Equal(m.Id, live.LeaderId);
            Assert.Equal(5000m, live.BidDeposit);
            Assert.Equal(500m, live.MinIncrement);

            var cfg = await db.Settings.SingleAsync();
            Assert.Equal(5000m, cfg.DepositAmount);
            Assert.Equal(50m, cfg.OpeningPrice);
            Assert.Equal(3000m, cfg.ManqiyaOpeningPrice);
            Assert.Equal(500m, cfg.MinIncrement);
            Assert.Equal(2, cfg.AuctionDays);

            var comp = await db.ReservedNames.SingleAsync(n => n.Name == "شعار الغيم البعيد الثاني");
            Assert.Equal(ReservedSources.Compound, comp.Source);
            Assert.Equal("شعار الغيم البعيد", comp.BaseName);
            var moved = await db.ReservedNames.SingleAsync(n => n.Name == "شعار الغيم البعيد الثالث");
            Assert.Equal(m.Id, moved.OwnerId);
            Assert.Equal(ReservedSources.Transfer, moved.Source);
            Assert.True(await db.Transfers.AnyAsync(t => t.Number == "NT-L1" && t.Status == TransferStatuses.Completed && t.FromId == s.Id));

            // «طلب اسم مستثنى» → exemption requests; a done one carries a signed document
            var ex = await db.Exemptions.SingleAsync(x => x.Number == "EX-L3");
            Assert.Equal(ExemptionStatuses.Paid, ex.Status);
            Assert.Equal("900115000123456", ex.CamelChip);
            Assert.Equal(NameTypes.Bakkar, ex.CamelType);
            Assert.Equal("فردي بكار", ex.FestivalName);
            Assert.Equal(5000m, ex.Amount);
            Assert.Equal(s.Id, ex.UserId);
            Assert.False(string.IsNullOrEmpty(ex.Signature));
            var rejected = await db.Exemptions.SingleAsync(x => x.Number == "EX-L4");
            Assert.Equal(ExemptionStatuses.Rejected, rejected.Status);
            Assert.Equal("سيف الملك", rejected.FestivalName);
            Assert.Equal("الشريحة مربوطة باسم", rejected.RejectReason);

            var notes = await db.Notifications.Where(n => n.UserId == s.Id && n.Kind == "legacy").ToListAsync();
            Assert.Equal(2, notes.Count);
            Assert.Contains(notes, n => n.Read && n.CreatedAt.Year == 2025);
            Assert.True(await db.NextNumberAsync("RequestNo") >= 5004);

            var tickets = await db.Tickets.Include(t => t.Messages).Where(t => t.Topic == "النظام السابق").ToListAsync();
            Assert.Equal(2, tickets.Count);
            Assert.All(tickets, t => Assert.Equal(TicketStatuses.Closed, t.Status));
            var mine = tickets.Single(t => t.UserId == s.Id);
            Assert.Equal("استفسار عن المزاد", mine.Subject);
            Assert.Equal(2024, mine.CreatedAt.Year);
            Assert.StartsWith("BL-24", mine.Number);
            Assert.Equal("رسالة تواصل", tickets.Single(t => t.UserId == null).Subject);
        }

        // a second run adds nothing
        var again = await Run(true);
        Assert.False(again.Counts.ContainsKey("members"));
        Assert.False(again.Counts.ContainsKey("requests"));
        Assert.False(again.Counts.ContainsKey("settings.fromLegacy"));
        Assert.False(again.Counts.ContainsKey("tickets"));
        Assert.False(again.Counts.Keys.Any(k => k.StartsWith("exemptions.")));
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<SammhaDbContext>().Requests.CountAsync(r => r.Number == "SM-5001"));
    }

    [Fact]
    public void Reads_old_text_dates()
    {
        Assert.Equal(new DateTimeOffset(2025, 3, 15, 7, 30, 0, TimeSpan.Zero), LegacyImporter.ParseDate("15/03/2025 10:30:00"));
        Assert.Equal(new DateTimeOffset(2025, 3, 15, 19, 30, 0, TimeSpan.Zero), LegacyImporter.ParseDate("15/03/2025 10:30 م"));
        Assert.Null(LegacyImporter.ParseDate("garbage"));
    }
}

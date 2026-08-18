using System.Globalization;
using Application.Abstractions.Authentication;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Statements.List;

namespace IntegrationTests;

// Proves the customer-facing statement list filtering: preset windows (last N completed months), a
// custom inclusive YYYY-MM range (either bound optional; equal bounds = a single month), and
// validation of malformed bounds. Driven at the handler level with a stubbed user context — the HTTP
// list requires a Keycloak token the offline suite can't mint, and the ownership filter is exercised
// by the stubbed customer id.
public sealed class ListStatementsFilterTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task PeriodRange_Should_ReturnOnlyStatementsWithinInclusiveRange()
    {
        Guid customerId = await SeedAsync("2024-01", "2024-02", "2024-03", "2024-04");

        PagedStatementResponse page = await ListAsync(
            customerId, new GetStatementsQuery(null, null, "2024-02", "2024-03"));

        page.Items.Select(i => i.Period).OrderBy(p => p).ToArray()
            .ShouldBe(["2024-02", "2024-03"]);
    }

    [Fact]
    public async Task PeriodFromOnly_Should_ReturnStatementsAtOrAfterBound()
    {
        Guid customerId = await SeedAsync("2024-01", "2024-02", "2024-03");

        PagedStatementResponse page = await ListAsync(
            customerId, new GetStatementsQuery(null, null, "2024-02", null));

        page.Items.Select(i => i.Period).OrderBy(p => p).ToArray()
            .ShouldBe(["2024-02", "2024-03"]);
    }

    [Fact]
    public async Task CustomRange_WithEqualBounds_Should_ReturnSingleMonth()
    {
        Guid customerId = await SeedAsync("2024-01", "2024-02", "2024-03");

        PagedStatementResponse page = await ListAsync(
            customerId, new GetStatementsQuery(null, StatementPeriodRange.Custom, "2024-02", "2024-02"));

        page.Items.Select(i => i.Period).ToArray().ShouldBe(["2024-02"]);
    }

    [Fact]
    public async Task LastMonth_Should_ReturnOnlyThePreviousCompletedMonth()
    {
        Guid customerId = await SeedAsync(MonthsAgo(2), MonthsAgo(1), MonthsAgo(0));

        PagedStatementResponse page = await ListAsync(
            customerId, new GetStatementsQuery(null, StatementPeriodRange.LastMonth, null, null));

        // Excludes the current (incomplete) month and anything older than last month.
        page.Items.Select(i => i.Period).ToArray().ShouldBe([MonthsAgo(1)]);
    }

    [Fact]
    public async Task Last3Months_Should_ReturnPreviousThreeCompletedMonths_ExcludingCurrent()
    {
        Guid customerId = await SeedAsync(
            MonthsAgo(4), MonthsAgo(3), MonthsAgo(2), MonthsAgo(1), MonthsAgo(0));

        PagedStatementResponse page = await ListAsync(
            customerId, new GetStatementsQuery(null, StatementPeriodRange.Last3Months, null, null));

        page.Items.Select(i => i.Period).OrderBy(p => p).ToArray()
            .ShouldBe(new[] { MonthsAgo(3), MonthsAgo(2), MonthsAgo(1) }.OrderBy(p => p).ToArray());
    }

    // Same UTC-anchored month arithmetic the handler uses, so preset expectations line up.
    private static string MonthsAgo(int n) =>
        DateTime.UtcNow.AddMonths(-n).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task InvalidRangeBound_Should_Fail()
    {
        Guid customerId = await SeedAsync("2024-01");

        Result<PagedStatementResponse> result = await ListRawAsync(
            customerId, new GetStatementsQuery(null, null, "2024-13", null));

        result.IsFailure.ShouldBeTrue();
    }

    private async Task<PagedStatementResponse> ListAsync(Guid customerId, GetStatementsQuery query)
    {
        Result<PagedStatementResponse> result = await ListRawAsync(customerId, query);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<Result<PagedStatementResponse>> ListRawAsync(Guid customerId, GetStatementsQuery query)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new GetStatementsQueryHandler(db, new StubUserContext(customerId, isAdmin: false));
        return await handler.Handle(query, CancellationToken.None);
    }

    private async Task<Guid> SeedAsync(params string[] periods)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customerId = Guid.NewGuid();
        User user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", ValidSaId).Value;
        db.Users.Add(user);

        foreach (string period in periods)
        {
            Statement statement = Statement.Create(
                customerId, Guid.NewGuid(), "s.pdf", $"statements/{customerId}/{period}.pdf",
                "application/pdf", 1024, period, "test").Value;
            db.Statements.Add(statement);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return customerId;
    }

    private sealed class StubUserContext(Guid userId, bool isAdmin) : IUserContext
    {
        public Guid UserId { get; } = userId;
        public bool IsAdmin { get; } = isAdmin;
    }
}

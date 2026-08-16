using Application.Abstractions.Authentication;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Statements.List;

namespace IntegrationTests;

// Proves the customer-facing statement list filtering: an exact single-month match, an inclusive
// YYYY-MM range (either bound optional), exact-takes-precedence, and validation of malformed bounds.
// Driven at the handler level with a stubbed user context — the HTTP list requires a Keycloak token
// the offline suite can't mint, and the ownership filter is exercised by the stubbed customer id.
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
    public async Task ExactPeriod_Should_TakePrecedence_OverRange()
    {
        Guid customerId = await SeedAsync("2024-01", "2024-02", "2024-03");

        PagedStatementResponse page = await ListAsync(
            customerId, new GetStatementsQuery(null, "2024-01", "2024-02", "2024-03"));

        page.Items.Select(i => i.Period).ToArray().ShouldBe(["2024-01"]);
    }

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
        var user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", ValidSaId);
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

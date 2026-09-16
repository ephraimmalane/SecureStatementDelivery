using Domain.Users;
using Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Admin.Customers;

namespace IntegrationTests;

public sealed class LookupCustomerByEmailTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Handle_Should_ReturnCustomer_ForExactEmail_CaseInsensitive()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string email = $"Cust.{tag}@Example.com";
        Guid id = await SeedUserAsync("Cust", "One", email);

        Result<CustomerLookupResponse> result = await LookupAsync(email.ToUpperInvariant());

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(id);
        result.Value.Email.ShouldBe(email);
        result.Value.FirstName.ShouldBe("Cust");
        result.Value.LastName.ShouldBe("One");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_ForUnknownEmail()
    {
        Result<CustomerLookupResponse> result =
            await LookupAsync($"nobody.{Guid.NewGuid():N}@example.com");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.CustomerNotFound);
    }

    [Fact]
    public async Task Handle_Should_NotResolveCustomer_BySouthAfricanIdNumber()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        await SeedUserAsync("Cust", "Two", $"cust.{tag}@example.com");

        Result<CustomerLookupResponse> result = await LookupAsync(ValidSaId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.CustomerNotFound);
    }

    private async Task<Result<CustomerLookupResponse>> LookupAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var handler = new LookupCustomerByEmailQueryHandler(db);
        return await handler.Handle(new LookupCustomerByEmailQuery(email), CancellationToken.None);
    }

    private async Task<Guid> SeedUserAsync(string firstName, string lastName, string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        User user = User.Create(Guid.NewGuid(), email, firstName, lastName, ValidSaId).Value;
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);

        return user.Id;
    }
}

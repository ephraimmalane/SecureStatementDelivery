using Application.Abstractions.Messaging;
using Microsoft.AspNetCore.Http;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Admin.Customers;

namespace Web.Api.UnitTests.Features.Admin.Customers;

public sealed class LookupCustomerByEmailEndpointTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_Should_Return400_AndNotCallHandler_When_EmailMissing(string? email)
    {
        var handler = new RecordingHandler();

        IResult result = await LookupCustomerByEmailEndpoint.HandleAsync(
            email, handler, CancellationToken.None);

        IStatusCodeHttpResult status = result.ShouldBeAssignableTo<IStatusCodeHttpResult>();
        status.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);

        handler.WasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_CallHandler_AndReturnOk_When_EmailProvided()
    {
        var response = new CustomerLookupResponse(
            Guid.NewGuid(), "Cust", "One", "cust@test.com", IsActive: true, DateTime.UtcNow);
        var handler = new RecordingHandler(Result.Success(response));

        IResult result = await LookupCustomerByEmailEndpoint.HandleAsync(
            "cust@test.com", handler, CancellationToken.None);

        handler.ReceivedEmail.ShouldBe("cust@test.com");
        IStatusCodeHttpResult status = result.ShouldBeAssignableTo<IStatusCodeHttpResult>();
        status.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    private sealed class RecordingHandler(Result<CustomerLookupResponse>? response = null)
        : IQueryHandler<LookupCustomerByEmailQuery, CustomerLookupResponse>
    {
        public bool WasCalled { get; private set; }
        public string? ReceivedEmail { get; private set; }

        public Task<Result<CustomerLookupResponse>> Handle(
            LookupCustomerByEmailQuery query, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedEmail = query.Email;
            return Task.FromResult(
                response ?? Result.Failure<CustomerLookupResponse>(
                    Error.NotFound("Test.Unset", "No response configured.")));
        }
    }
}

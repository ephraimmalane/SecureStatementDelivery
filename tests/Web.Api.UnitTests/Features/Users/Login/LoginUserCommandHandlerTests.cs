using System.Net;
using Application.Abstractions.Authentication;
using Domain.Users;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Users.Login;

namespace Web.Api.UnitTests.Features.Users.Login;

public sealed class LoginUserCommandHandlerTests
{
    private static readonly LoginUserCommand Command = new("user@example.com", "correct-horse");

    private static LoginUserCommandHandler CreateHandler(IIdentityProviderClient identityProvider) =>
        new(identityProvider, TimeProvider.System);

    [Fact]
    public async Task Handle_Should_ReturnInvalidCredentials_When_KeycloakReturns401()
    {
        LoginUserCommandHandler handler = CreateHandler(
            new ThrowingIdentityProvider(new IdentityProviderException(HttpStatusCode.Unauthorized, "invalid")));

        Result<LoginResponse> result = await handler.Handle(Command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Handle_Should_ReturnAccountInactive_When_KeycloakReturns403()
    {
        LoginUserCommandHandler handler = CreateHandler(
            new ThrowingIdentityProvider(new IdentityProviderException(HttpStatusCode.Forbidden, "disabled")));

        Result<LoginResponse> result = await handler.Handle(Command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.AccountInactive);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Handle_Should_Propagate_OtherIdentityProviderFailures_Unchanged(HttpStatusCode statusCode)
    {
        LoginUserCommandHandler handler = CreateHandler(
            new ThrowingIdentityProvider(new IdentityProviderException(statusCode, "upstream failure")));

        IdentityProviderException exception = await Should.ThrowAsync<IdentityProviderException>(
            () => handler.Handle(Command, CancellationToken.None));

        exception.StatusCode.ShouldBe(statusCode);
    }

    [Fact]
    public async Task Handle_Should_Propagate_WhenIdentityProviderCallFails()
    {
        LoginUserCommandHandler handler = CreateHandler(
            new ThrowingIdentityProvider(new HttpRequestException("connection refused")));

        await Should.ThrowAsync<HttpRequestException>(
            () => handler.Handle(Command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Should_Propagate_UnexpectedExceptions()
    {
        LoginUserCommandHandler handler = CreateHandler(
            new ThrowingIdentityProvider(new InvalidOperationException("boom")));

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.Handle(Command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Should_ReturnTokens_When_LoginSucceeds()
    {
        var token = new AuthenticationResult("access-token", "refresh-token", 3600);
        LoginUserCommandHandler handler = CreateHandler(new SuccessfulIdentityProvider(token));

        Result<LoginResponse> result = await handler.Handle(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-token");
        result.Value.RefreshToken.ShouldBe("refresh-token");
    }

    private sealed class ThrowingIdentityProvider(Exception exception) : StubIdentityProvider
    {
        public override Task<AuthenticationResult> LoginAsync(
            string email, string password, CancellationToken cancellationToken) =>
            Task.FromException<AuthenticationResult>(exception);
    }

    private sealed class SuccessfulIdentityProvider(AuthenticationResult token) : StubIdentityProvider
    {
        public override Task<AuthenticationResult> LoginAsync(
            string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(token);
    }

    private abstract class StubIdentityProvider : IIdentityProviderClient
    {
        public virtual Task<AuthenticationResult> LoginAsync(
            string email, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticationResult> RefreshTokenAsync(
            string refreshToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> RegisterUserAsync(
            string email, string firstName, string lastName, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

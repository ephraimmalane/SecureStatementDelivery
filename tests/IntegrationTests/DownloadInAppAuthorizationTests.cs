using Application.Abstractions.Authentication;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Statements.Download;
using Web.Api.Features.Statements.DownloadInApp;

namespace IntegrationTests;

public sealed class DownloadInAppAuthorizationTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_StatementDoesNotExist()
    {
        Result<StatementFileResponse> result = await HandleAsync(
            new StubUserContext(Guid.NewGuid(), isAdmin: false),
            new DownloadInAppQuery(Guid.NewGuid()));

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_Should_ReturnForbidden_When_CallerIsNotOwner()
    {
        (Guid ownerId, Guid statementId) = await SeedStatementAsync(revoke: false);

        Result<StatementFileResponse> result = await HandleAsync(
            new StubUserContext(Guid.NewGuid(), isAdmin: false),
            new DownloadInAppQuery(statementId));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(StatementErrors.AccessDenied);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        ownerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_Should_ReturnForbidden_When_NonOwnerTargetsRevokedStatement()
    {
        (_, Guid statementId) = await SeedStatementAsync(revoke: true);

        Result<StatementFileResponse> result = await HandleAsync(
            new StubUserContext(Guid.NewGuid(), isAdmin: false),
            new DownloadInAppQuery(statementId));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(StatementErrors.AccessDenied);
    }

    [Fact]
    public async Task Handle_Should_ReturnAlreadyRevoked_When_OwnerTargetsRevokedStatement()
    {
        (Guid ownerId, Guid statementId) = await SeedStatementAsync(revoke: true);

        Result<StatementFileResponse> result = await HandleAsync(
            new StubUserContext(ownerId, isAdmin: false),
            new DownloadInAppQuery(statementId));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(StatementErrors.AlreadyRevoked);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_Should_CommitAuthorizedAudit_BeforeStorageIsInvoked()
    {
        (Guid ownerId, Guid statementId) = await SeedStatementAsync(revoke: false);

        await Should.ThrowAsync<NotSupportedException>(() => HandleAsync(
            new StubUserContext(ownerId, isAdmin: false),
            new DownloadInAppQuery(statementId)));

        (await HasAuditAsync(statementId, AuditAction.DownloadAuthorized)).ShouldBeTrue();
    }

    private async Task<Result<StatementFileResponse>> HandleAsync(
        IUserContext userContext, DownloadInAppQuery query)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new DownloadInAppQueryHandler(db, new UnreachableFileStorage(), userContext);
        return await handler.Handle(query, CancellationToken.None);
    }

    private async Task<(Guid OwnerId, Guid StatementId)> SeedStatementAsync(bool revoke)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ownerId = Guid.NewGuid();
        User user = User.Create(ownerId, $"{ownerId:N}@example.com", "Test", "Customer", ValidSaId).Value;
        db.Users.Add(user);

        Statement statement = Statement.Create(
            ownerId, Guid.NewGuid(), "s.pdf", $"statements/{ownerId}/2024-01.pdf",
            "application/pdf", 1024, "2024-01", "test").Value;

        if (revoke)
        {
            statement.Revoke(Guid.NewGuid(), "superseded").IsSuccess.ShouldBeTrue();
        }

        db.Statements.Add(statement);
        await db.SaveChangesAsync(CancellationToken.None);

        return (ownerId, statement.Id);
    }

    private async Task<bool> HasAuditAsync(Guid statementId, AuditAction action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .AnyAsync(a => a.StatementId == statementId && a.Action == action);
    }

    private sealed class StubUserContext(Guid userId, bool isAdmin) : IUserContext
    {
        public Guid UserId { get; } = userId;
        public bool IsAdmin { get; } = isAdmin;
    }

    private sealed class UnreachableFileStorage : IFileStorageService
    {
        public Task<StoredFile> StoreAsync(
            string fileName, Stream content, string contentType, string directory, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> RetrieveAsync(string storagePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Uri?> GeneratePresignedDownloadUriAsync(
            string storagePath, TimeSpan expiry, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

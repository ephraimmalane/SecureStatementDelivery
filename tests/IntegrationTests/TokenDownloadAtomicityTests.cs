using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Web.Api.Features.Statements.Download;

namespace IntegrationTests;

public sealed class TokenDownloadAtomicityTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Handle_Should_NotConsumeToken_When_AuditSaveFails()
    {
        (string token, Guid tokenId, Guid statementId) = await SeedSingleUseTokenAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ApplicationDbContext real = sp.GetRequiredService<ApplicationDbContext>();
        IDownloadTokenService tokenService = sp.GetRequiredService<IDownloadTokenService>();

        var handler = new DownloadStatementQueryHandler(
            new ThrowingSaveDbContext(real),
            tokenService,
            new UnreachableFileStorage(),
            TimeProvider.System);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            handler.Handle(new DownloadStatementQuery(token), CancellationToken.None));

        (await TokenIsUsedAsync(tokenId)).ShouldBeFalse();
        (await HasAuditAsync(statementId, AuditAction.DownloadAuthorized)).ShouldBeFalse();
    }

    private async Task<(string Token, Guid TokenId, Guid StatementId)> SeedSingleUseTokenAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
        IDownloadTokenService tokenService = sp.GetRequiredService<IDownloadTokenService>();

        var customerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        User user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", ValidSaId).Value;
        Statement statement = Statement.Create(
            customerId, adminId, "s.pdf", $"statements/{customerId}/2024-01.pdf",
            "application/pdf", 1024, "2024-01", "test").Value;

        DateTime expiry = DateTime.UtcNow.AddMinutes(5);
        (string token, Guid tokenId) = tokenService.GenerateToken(statement.Id, customerId, expiry);
        var downloadToken = DownloadToken.Create(
            tokenId, statement.Id, customerId, tokenService.HashToken(token),
            expiry, DateTime.UtcNow, isSingleUse: true, ipAddress: null);

        db.Users.Add(user);
        db.Statements.Add(statement);
        db.DownloadTokens.Add(downloadToken);
        await db.SaveChangesAsync(CancellationToken.None);

        return (token, tokenId, statement.Id);
    }

    private async Task<bool> TokenIsUsedAsync(Guid tokenId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.DownloadTokens
            .AsNoTracking()
            .Where(t => t.Id == tokenId)
            .Select(t => t.IsUsed)
            .SingleAsync();
    }

    private async Task<bool> HasAuditAsync(Guid statementId, AuditAction action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .AnyAsync(a => a.StatementId == statementId && a.Action == action);
    }

    private sealed class ThrowingSaveDbContext(ApplicationDbContext inner) : IApplicationDbContext
    {
        public DbSet<User> Users => inner.Users;
        public DbSet<Statement> Statements => inner.Statements;
        public DbSet<DownloadToken> DownloadTokens => inner.DownloadTokens;
        public DbSet<AuditLog> AuditLogs => inner.AuditLogs;

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task<T?>>? verifyCommitted = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            IExecutionStrategy strategy = inner.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(async ct =>
            {
                await using IDbContextTransaction transaction = await inner.Database.BeginTransactionAsync(ct);
                T result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            }, cancellationToken);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
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

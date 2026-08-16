using System.Net;
using Application.Abstractions.Ingestion;
using Application.Abstractions.Messaging;
using Domain.AuditLogs;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf;
using SharedKernel;
using Shouldly;
using Web.Api.Features.Statements.Ingestion;
using Web.Api.Features.Statements.Upload;

namespace IntegrationTests;

// Proves the production ingestion paths. The HTTP push endpoint is locked to the service-account
// policy (an anonymous caller is rejected before any work). The pull path is proven functionally by
// driving the shared processor + funnel end-to-end: a machine-ingested statement is stored,
// encrypted, attributed to the ingestion service principal, audited, and idempotent on redelivery —
// exactly like an admin upload but with no human actor.
public sealed class StatementIngestionTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    // Valid SA ID (DOB 1980-01-01, correct Luhn) — required before a statement can be ingested,
    // because every statement is AES-encrypted with the customer's ID as the open password.
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task IngestEndpoint_Should_RejectAnonymousCaller()
    {
        HttpClient client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/statements/ingest", UriKind.Relative), content);

        // The endpoint requires the statement-ingest service-account policy; no token => 401.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Processor_Should_IngestStatement_ThroughSharedFunnel()
    {
        Guid customerId = await SeedCustomerAsync();
        StatementIngestionProcessor processor = CreateProcessor(out IServiceScope scope);
        using IServiceScope _ = scope;

        StatementIngestionMessage message = BuildMessage(customerId, "2024-03", Guid.NewGuid().ToString());

        Result<Guid> result = await processor.ProcessAsync(message, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        using IServiceScope verify = _factory.Services.CreateScope();
        ApplicationDbContext db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Statement statement = await db.Statements.AsNoTracking().SingleAsync(s => s.Id == result.Value);
        statement.CustomerId.ShouldBe(customerId);
        // No human uploaded it — the actor is the reserved ingestion service principal.
        statement.UploadedByAdminId.ShouldBe(SystemPrincipals.StatementIngestionService);
        statement.IsActive.ShouldBeTrue();
        statement.IsPasswordProtected.ShouldBeTrue();

        bool audited = await db.DownloadAuditLogs.AsNoTracking()
            .AnyAsync(a => a.StatementId == result.Value && a.Action == AuditAction.StatementUploaded);
        audited.ShouldBeTrue();
    }

    [Fact]
    public async Task Processor_Should_BeIdempotent_OnRedelivery()
    {
        Guid customerId = await SeedCustomerAsync();
        string idempotencyKey = Guid.NewGuid().ToString();

        StatementIngestionProcessor processor = CreateProcessor(out IServiceScope scope);
        using IServiceScope _ = scope;

        // At-least-once delivery: the same message arriving twice must not create two statements.
        Result<Guid> first = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-04", idempotencyKey), CancellationToken.None);
        Result<Guid> second = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-04", idempotencyKey), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);

        using IServiceScope verify = _factory.Services.CreateScope();
        ApplicationDbContext db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        int count = await db.Statements.AsNoTracking().CountAsync(s => s.IdempotencyKey == idempotencyKey);
        count.ShouldBe(1);
    }

    private StatementIngestionProcessor CreateProcessor(out IServiceScope scope)
    {
        scope = _factory.Services.CreateScope();
        ICommandHandler<UploadStatementCommand, Guid> handler =
            scope.ServiceProvider.GetRequiredService<ICommandHandler<UploadStatementCommand, Guid>>();
        return new StatementIngestionProcessor(handler);
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customerId = Guid.NewGuid();
        var user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", ValidSaId);

        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);

        return customerId;
    }

    private static StatementIngestionMessage BuildMessage(Guid customerId, string period, string idempotencyKey) =>
        new()
        {
            CustomerId = customerId,
            Period = period,
            FileName = "statement.pdf",
            ContentType = "application/pdf",
            IdempotencyKey = idempotencyKey,
            Description = "machine ingested",
            ReceiptHandle = "test-receipt",
            OpenContentAsync = _ => Task.FromResult<Stream>(MakePdf())
        };

    // A structurally valid single-page PDF the encryption step can open (a fake byte string can't).
    private static MemoryStream MakePdf()
    {
        using var document = new PdfDocument();
        document.AddPage();

        var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        stream.Position = 0;
        return stream;
    }
}

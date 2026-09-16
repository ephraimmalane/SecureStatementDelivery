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

public sealed class StatementIngestionTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task IngestEndpoint_Should_RejectAnonymousCaller()
    {
        HttpClient client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/statements/ingest", UriKind.Relative), content);

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
        statement.UploadedByAdminId.ShouldBe(SystemPrincipals.StatementIngestionService);
        statement.IsActive.ShouldBeTrue();
        statement.IsPasswordProtected.ShouldBeTrue();

        bool audited = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.StatementId == result.Value && a.Action == AuditAction.StatementUploaded);
        audited.ShouldBeTrue();
    }

    [Fact]
    public async Task Processor_Should_BeIdempotent_OnRedelivery()
    {
        Guid customerId = await SeedCustomerAsync();
        string documentId = Guid.NewGuid().ToString();

        StatementIngestionProcessor processor = CreateProcessor(out IServiceScope scope);
        using IServiceScope _ = scope;

        Result<Guid> first = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-04", documentId), CancellationToken.None);
        Result<Guid> second = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-04", documentId), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);

        using IServiceScope verify = _factory.Services.CreateScope();
        ApplicationDbContext db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        int count = await db.Statements.AsNoTracking().CountAsync(s => s.DocumentId == documentId);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task Processor_Should_Dedup_SameDocumentId_Even_When_FileNameDiffers()
    {
        Guid customerId = await SeedCustomerAsync();
        string documentId = Guid.NewGuid().ToString();

        StatementIngestionProcessor processor = CreateProcessor(out IServiceScope scope);
        using IServiceScope _ = scope;

        Result<Guid> first = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-05", documentId, "january.pdf"), CancellationToken.None);
        Result<Guid> second = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-05", documentId, "jan_statement_final.pdf"), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);

        using IServiceScope verify = _factory.Services.CreateScope();
        ApplicationDbContext db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        int count = await db.Statements.AsNoTracking().CountAsync(s => s.DocumentId == documentId);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task Processor_Should_Dedup_IdenticalBytes_SamePeriod_AcrossDifferentDocumentIds()
    {
        Guid customerId = await SeedCustomerAsync();
        byte[] pdfBytes = MakePdf().ToArray();

        StatementIngestionProcessor processor = CreateProcessor(out IServiceScope scope);
        using IServiceScope _ = scope;

        Result<Guid> first = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-06", "DOC-A", content: () => new MemoryStream(pdfBytes)),
            CancellationToken.None);
        Result<Guid> second = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-06", "DOC-B", content: () => new MemoryStream(pdfBytes)),
            CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);

        using IServiceScope verify = _factory.Services.CreateScope();
        ApplicationDbContext db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        int count = await db.Statements.AsNoTracking().CountAsync(s => s.CustomerId == customerId);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task Processor_Should_NotMerge_IdenticalBytes_AcrossDifferentPeriods()
    {
        Guid customerId = await SeedCustomerAsync();
        byte[] pdfBytes = MakePdf().ToArray();

        StatementIngestionProcessor processor = CreateProcessor(out IServiceScope scope);
        using IServiceScope _ = scope;

        Result<Guid> june = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-06", "DOC-A", content: () => new MemoryStream(pdfBytes)),
            CancellationToken.None);
        Result<Guid> july = await processor.ProcessAsync(
            BuildMessage(customerId, "2024-07", "DOC-B", content: () => new MemoryStream(pdfBytes)),
            CancellationToken.None);

        june.IsSuccess.ShouldBeTrue();
        july.IsSuccess.ShouldBeTrue();
        july.Value.ShouldNotBe(june.Value);

        using IServiceScope verify = _factory.Services.CreateScope();
        ApplicationDbContext db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        int count = await db.Statements.AsNoTracking().CountAsync(s => s.CustomerId == customerId);
        count.ShouldBe(2);
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
        User user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", ValidSaId).Value;

        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);

        return customerId;
    }

    private static StatementIngestionMessage BuildMessage(
        Guid customerId, string period, string documentId,
        string fileName = "statement.pdf",
        Func<Stream>? content = null) =>
        new()
        {
            CustomerId = customerId,
            Period = period,
            FileName = fileName,
            ContentType = "application/pdf",
            DocumentId = documentId,
            Description = "machine ingested",
            ReceiptHandle = "test-receipt",
            OpenContentAsync = _ => Task.FromResult<Stream>(content?.Invoke() ?? MakePdf())
        };

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

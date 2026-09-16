using Application.Abstractions.Notifications;
using Domain.AuditLogs;
using Domain.Statements;
using Domain.Statements.Events;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Web.Api.Features.Statements;
using Web.Api.Features.Statements.Upload;

namespace IntegrationTests;

public sealed class StatementUploadedNotificationTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Handle_Should_QueueSafeNotification_AndWriteAudit()
    {
        (Guid customerId, Guid statementId) = await SeedStatementAsync();
        var notifications = new RecordingNotificationService();

        await InvokeHandlerAsync(notifications, new StatementUploadedDomainEvent(statementId, customerId, Guid.NewGuid()));

        notifications.Sent.Count.ShouldBe(1);
        StatementAvailableNotification sent = notifications.Sent[0];
        sent.CustomerId.ShouldBe(customerId);
        sent.StatementId.ShouldBe(statementId);
        sent.Period.ShouldBe("2024-01");
        sent.PasswordHint.ShouldBe(StatementMessages.PasswordHint);

        (await NotifiedAuditCountAsync(statementId)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_BeIdempotent_OnRedelivery()
    {
        (Guid customerId, Guid statementId) = await SeedStatementAsync();
        var notifications = new RecordingNotificationService();
        var domainEvent = new StatementUploadedDomainEvent(statementId, customerId, Guid.NewGuid());

        await InvokeHandlerAsync(notifications, domainEvent);
        await InvokeHandlerAsync(notifications, domainEvent);

        notifications.Sent.Count.ShouldBe(1);
        (await NotifiedAuditCountAsync(statementId)).ShouldBe(1);
    }

    private async Task InvokeHandlerAsync(
        INotificationService notificationService, StatementUploadedDomainEvent domainEvent)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var handler = new StatementUploadedDomainEventHandler(
            db,
            notificationService,
            NullLogger<StatementUploadedDomainEventHandler>.Instance);

        await handler.Handle(domainEvent, CancellationToken.None);
    }

    private async Task<int> NotifiedAuditCountAsync(Guid statementId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .CountAsync(a =>
                a.StatementId == statementId &&
                a.Action == AuditAction.StatementAvailableNotified);
    }

    private async Task<(Guid CustomerId, Guid StatementId)> SeedStatementAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customerId = Guid.NewGuid();
        User user = User.Create(customerId, $"{customerId:N}@example.com", "Test", "Customer", ValidSaId).Value;
        Statement statement = Statement.Create(
            customerId, Guid.NewGuid(), "s.pdf", $"statements/{customerId}/2024-01.pdf",
            "application/pdf", 1024, "2024-01", "test").Value;

        db.Users.Add(user);
        db.Statements.Add(statement);
        await db.SaveChangesAsync(CancellationToken.None);

        return (customerId, statement.Id);
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<StatementAvailableNotification> Sent { get; } = [];

        public Task NotifyStatementAvailableAsync(
            StatementAvailableNotification notification, CancellationToken cancellationToken)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }

        public Task NotifyUserRegisteredAsync(
            WelcomeNotification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

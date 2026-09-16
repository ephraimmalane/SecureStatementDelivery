using Application.Abstractions.Notifications;
using Domain.AuditLogs;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Web.Api.Features.Users.Register;

namespace IntegrationTests;

public sealed class UserRegisteredNotificationTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private const string ValidSaId = "8001015009087";

    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Handle_Should_QueueWelcomeNotification_AndWriteAudit()
    {
        Guid userId = await SeedUserAsync();
        var notifications = new RecordingNotificationService();

        await InvokeHandlerAsync(notifications, new UserRegisteredDomainEvent(userId));

        notifications.Sent.Count.ShouldBe(1);
        notifications.Sent[0].CustomerId.ShouldBe(userId);

        (await NotifiedAuditCountAsync(userId)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_BeIdempotent_OnRedelivery()
    {
        Guid userId = await SeedUserAsync();
        var notifications = new RecordingNotificationService();
        var domainEvent = new UserRegisteredDomainEvent(userId);

        await InvokeHandlerAsync(notifications, domainEvent);
        await InvokeHandlerAsync(notifications, domainEvent);

        notifications.Sent.Count.ShouldBe(1);
        (await NotifiedAuditCountAsync(userId)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_Skip_When_UserNoLongerExists()
    {
        var notifications = new RecordingNotificationService();

        await InvokeHandlerAsync(notifications, new UserRegisteredDomainEvent(Guid.NewGuid()));

        notifications.Sent.ShouldBeEmpty();
    }

    private async Task InvokeHandlerAsync(
        INotificationService notificationService, UserRegisteredDomainEvent domainEvent)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var handler = new UserRegisteredDomainEventHandler(
            db,
            notificationService,
            NullLogger<UserRegisteredDomainEventHandler>.Instance);

        await handler.Handle(domainEvent, CancellationToken.None);
    }

    private async Task<int> NotifiedAuditCountAsync(Guid userId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .CountAsync(a =>
                a.UserId == userId &&
                a.Action == AuditAction.WelcomeNotified);
    }

    private async Task<Guid> SeedUserAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userId = Guid.NewGuid();
        User user = User.Create(userId, $"{userId:N}@example.com", "Test", "Customer", ValidSaId).Value;

        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);

        return userId;
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<WelcomeNotification> Sent { get; } = [];

        public Task NotifyStatementAvailableAsync(
            StatementAvailableNotification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task NotifyUserRegisteredAsync(
            WelcomeNotification notification, CancellationToken cancellationToken)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }
}

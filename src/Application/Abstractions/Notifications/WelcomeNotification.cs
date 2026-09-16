namespace Application.Abstractions.Notifications;

/// <summary>
/// The payload for a "welcome" notification sent when a customer registers. By design it carries
/// only the customer id — the downstream channel resolves the customer's contact details — so a
/// forwarded or compromised message exposes no personal data.
/// </summary>
public sealed record WelcomeNotification(Guid CustomerId);

namespace Application.Abstractions.Notifications;

/// <summary>
/// The payload for a "your statement is available — sign in to view" notification. By design it
/// carries no document and no download link: only enough for the customer to recognise the message
/// and know how to open the PDF once they authenticate. A forwarded or compromised inbox therefore
/// cannot yield access to the statement.
/// </summary>
public sealed record StatementAvailableNotification(
    Guid CustomerId,
    Guid StatementId,
    string Period,
    string PasswordHint);

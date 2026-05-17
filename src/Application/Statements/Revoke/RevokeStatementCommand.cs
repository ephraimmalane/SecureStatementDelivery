using Application.Abstractions.Messaging;

namespace Application.Statements.Revoke;

public sealed record RevokeStatementCommand(Guid StatementId, string Reason) : ICommand;

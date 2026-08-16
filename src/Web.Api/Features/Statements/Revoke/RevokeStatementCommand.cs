using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.Revoke;

public sealed record RevokeStatementCommand(Guid StatementId, string Reason) : ICommand;

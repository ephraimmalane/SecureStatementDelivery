using Application.Abstractions.Messaging;

namespace Application.Statements.GetById;

public sealed record GetStatementByIdQuery(Guid StatementId) : IQuery<StatementDetailResponse>;

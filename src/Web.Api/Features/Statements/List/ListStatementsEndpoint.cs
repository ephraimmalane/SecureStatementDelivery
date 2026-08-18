using System.ComponentModel;
using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.List;

internal sealed class ListStatementsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements", async (
            [Description("Admin only: filter to a specific customer's statements. Ignored for non-admin callers (scoped to your own).")]
            Guid? customerId,
            [Description("Preset period window: LastMonth, Last3Months, Last6Months, Last12Months, or Custom (then supply periodFrom/periodTo).")]
            StatementPeriodRange? range,
            [Description("Inclusive start month in YYYY-MM format, e.g. 2024-01. Applies when range=Custom or range is omitted.")]
            string? periodFrom,
            [Description("Inclusive end month in YYYY-MM format, e.g. 2024-03. Applies when range=Custom or range is omitted.")]
            string? periodTo,
            [Description("1-based page number (defaults to 1).")]
            int page,
            [Description("Items per page, 1-100 (defaults to 20).")]
            int pageSize,
            IQueryHandler<GetStatementsQuery, PagedStatementResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetStatementsQuery(
                customerId,
                range,
                periodFrom,
                periodTo,
                page <= 0 ? 1 : page,
                pageSize is <= 0 or > 100 ? 20 : pageSize);

            Result<PagedStatementResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .WithSummary("List statements (paginated), filtered by a preset or custom period range.")
        .WithDescription(
            "Returns the caller's statements; an admin may target another customer via customerId. " +
            "Use 'range' for a preset window (LastMonth, Last3Months, Last6Months, Last12Months) — each " +
            "resolves to the last N completed months, ending with the previous month. Use 'range=Custom' " +
            "with 'periodFrom'/'periodTo' as canonical YYYY-MM months (e.g. 2024-01); equal bounds select a " +
            "single month. Omitting 'range' applies any supplied periodFrom/periodTo directly.")
        .HasPermission(Permissions.StatementsReadOwn);
    }
}

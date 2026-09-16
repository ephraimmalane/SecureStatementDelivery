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
            [Description("Preset period window; takes precedence over periodFrom/periodTo when supplied. " +
                "LastMonth, Last3Months, Last6Months, or Last12Months each resolve to the last N completed " +
                "calendar months ending with the previous month (UTC). Use Custom — or omit range — to filter " +
                "by periodFrom/periodTo instead.")]
            StatementPeriodRange? range,
            [Description("Inclusive start month, canonical YYYY-MM (e.g. 2024-01). Applied only when range=Custom " +
                "or range is omitted (ignored for presets). Omit for no lower bound. A malformed value returns 400.")]
            string? periodFrom,
            [Description("Inclusive end month, canonical YYYY-MM (e.g. 2024-03). Applied only when range=Custom or " +
                "range is omitted (ignored for presets). Omit for no upper bound. A malformed value returns 400. " +
                "If periodTo is earlier than periodFrom the range is empty and no statements match.")]
            string? periodTo,
            [Description("1-based page number (optional; defaults to 1).")]
            int? page,
            [Description("Items per page, 1-100 (optional; defaults to 20).")]
            int? pageSize,
            IQueryHandler<GetStatementsQuery, PagedStatementResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetStatementsQuery(
                customerId,
                range,
                periodFrom,
                periodTo,
                page is null or <= 0 ? 1 : page.Value,
                pageSize is null or <= 0 or > 100 ? 20 : pageSize.Value);

            Result<PagedStatementResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .WithSummary("List statements (paginated), filtered by a preset or custom period range.")
        .WithDescription(
            "Returns the caller's statements; an admin may target another customer via customerId. " +
            "Period filtering uses canonical YYYY-MM months (ISO 8601), matched against each statement's " +
            "period label — no time-zone conversion is applied. Both bounds are inclusive, so equal bounds " +
            "select a single month. " +
            "Precedence: a preset 'range' (LastMonth, Last3Months, Last6Months, Last12Months) wins and resolves " +
            "to the last N completed months ending with the previous month (UTC); periodFrom/periodTo are then " +
            "ignored. Use 'range=Custom', or omit 'range', to apply periodFrom/periodTo directly. " +
            "Either bound may be omitted (open-ended on that side); omitting range and both bounds returns all " +
            "statements. A malformed month returns 400 (InvalidPeriodFormat); a periodTo earlier than periodFrom " +
            "yields an empty result. Results are always paginated (page defaults to 1; pageSize defaults to 20, " +
            "max 100), ordered by upload time descending.")
        .HasPermission(Permissions.StatementsReadOwn);
    }
}

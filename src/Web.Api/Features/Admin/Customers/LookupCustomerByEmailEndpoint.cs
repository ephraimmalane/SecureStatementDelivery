using System.ComponentModel;
using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Admin.Customers;

internal sealed class LookupCustomerByEmailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/customers", (
            [Description("Exact email address of the customer to look up (a unique key). Required.")]
            string? email,
            IQueryHandler<LookupCustomerByEmailQuery, CustomerLookupResponse> handler,
            CancellationToken cancellationToken) =>
                HandleAsync(email, handler, cancellationToken))
        .WithTags(Tags.Admin)
        .WithSummary("Look up a customer by exact email (admin).")
        .WithDescription(
            "Resolves a customer's id from their exact email address (a unique key) so an admin can act " +
            "on their behalf — for example, to upload a statement. Returns 404 if no customer matches. " +
            "By design there is no name or partial-match search, to prevent customer enumeration; the " +
            "South African ID number is never accepted as a filter nor returned.")
        .HasPermission(Permissions.AdminUsers);
    }

    internal static async Task<IResult> HandleAsync(
        string? email,
        IQueryHandler<LookupCustomerByEmailQuery, CustomerLookupResponse> handler,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest(new { Error = "An 'email' query parameter is required." });
        }

        Result<CustomerLookupResponse> result =
            await handler.Handle(new LookupCustomerByEmailQuery(email), cancellationToken);

        return result.Match(Results.Ok, CustomResults.Problem);
    }
}

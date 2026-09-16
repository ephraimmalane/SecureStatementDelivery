using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Admin.Customers;

internal sealed class LookupCustomerByEmailQueryHandler(IApplicationDbContext context)
    : IQueryHandler<LookupCustomerByEmailQuery, CustomerLookupResponse>
{
    public async Task<Result<CustomerLookupResponse>> Handle(
        LookupCustomerByEmailQuery query,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA1304, CA1308, CA1311, CA1862
        string email = query.Email.Trim().ToLowerInvariant();

        CustomerLookupResponse? customer = await context.Users
            .AsNoTracking()
            .Where(u => u.Email.ToLower() == email)
            .Select(u => new CustomerLookupResponse(
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.IsActive,
                u.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore CA1304, CA1308, CA1311, CA1862

        return customer is null
            ? Result.Failure<CustomerLookupResponse>(UserErrors.CustomerNotFound)
            : Result.Success(customer);
    }
}

using Application.Abstractions.Messaging;

namespace Web.Api.Features.Admin.Customers;

public sealed record LookupCustomerByEmailQuery(string Email) : IQuery<CustomerLookupResponse>;

public sealed record CustomerLookupResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    DateTime CreatedAt);

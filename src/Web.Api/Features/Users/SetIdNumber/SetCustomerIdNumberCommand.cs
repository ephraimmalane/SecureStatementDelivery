using Application.Abstractions.Messaging;

namespace Web.Api.Features.Users.SetIdNumber;

public sealed record SetCustomerIdNumberCommand(Guid CustomerId, string SouthAfricanIdNumber) : ICommand
{
    // The ID number is sensitive PII (and a PDF password), so keep it out of all diagnostics.
#pragma warning disable S2068
    public override string ToString() =>
        $"SetCustomerIdNumberCommand {{ CustomerId = {CustomerId}, SouthAfricanIdNumber = [REDACTED] }}";
#pragma warning restore S2068
}

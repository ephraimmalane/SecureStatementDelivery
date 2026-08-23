using Application.Abstractions.Messaging;

namespace Web.Api.Features.Users.SetIdNumber;

public sealed record SetCustomerIdNumberCommand(Guid CustomerId, string SouthAfricanIdNumber) : ICommand
{
#pragma warning disable S2068
    public override string ToString() =>
        $"SetCustomerIdNumberCommand {{ CustomerId = {CustomerId}, SouthAfricanIdNumber = [REDACTED] }}";
#pragma warning restore S2068
}

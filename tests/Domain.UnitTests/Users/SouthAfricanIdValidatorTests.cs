using Domain.Users;
using Shouldly;

namespace Domain.UnitTests.Users;

public class SouthAfricanIdValidatorTests
{
    [Theory]
    [InlineData("8001015009087")] // citizenship digit 0 (SA citizen)
    [InlineData("  8001015009087  ")]
    [InlineData("8001015009186")] // citizenship digit 1 (permanent resident)
    [InlineData("8001015009285")] // citizenship digit 2 (refugee)
    public void IsValid_Should_Accept_WellFormedId(string id)
    {
        SouthAfricanIdValidator.IsValid(id).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("800101500908")]
    [InlineData("80010150090870")]
    [InlineData("800101500908A")]
    [InlineData("8001015009088")]
    [InlineData("8013015009087")]
    [InlineData("8000015009087")]
    [InlineData("8002305009087")]
    [InlineData("8001015009384")] // invalid citizenship digit 3 (passes DOB + Luhn)
    public void IsValid_Should_Reject_InvalidId(string? id)
    {
        SouthAfricanIdValidator.IsValid(id).ShouldBeFalse();
    }
}

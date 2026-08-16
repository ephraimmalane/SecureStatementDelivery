using Domain.Users;
using Shouldly;

namespace Domain.UnitTests.Users;

public class SouthAfricanIdValidatorTests
{
    [Theory]
    [InlineData("8001015009087")] // valid: DOB 1980-01-01, correct Luhn check digit
    [InlineData("  8001015009087  ")] // surrounding whitespace is trimmed
    public void IsValid_Should_Accept_WellFormedId(string id)
    {
        SouthAfricanIdValidator.IsValid(id).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("800101500908")]    // 12 digits — too short
    [InlineData("80010150090870")]  // 14 digits — too long
    [InlineData("800101500908A")]   // non-digit
    [InlineData("8001015009088")]   // wrong Luhn check digit
    [InlineData("8013015009087")]   // month 13 — invalid DOB
    [InlineData("8000015009087")]   // month 00 — invalid DOB
    [InlineData("8002305009087")]   // 30 February — invalid DOB
    public void IsValid_Should_Reject_InvalidId(string? id)
    {
        SouthAfricanIdValidator.IsValid(id).ShouldBeFalse();
    }
}

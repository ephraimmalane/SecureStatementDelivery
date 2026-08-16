using Domain.Users;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.Users;

public class UserTests
{
    [Fact]
    public void SetSouthAfricanIdNumber_Should_Store_When_Valid()
    {
        var user = User.Create(Guid.NewGuid(), "c@example.com", "Test", "Customer");

        Result result = user.SetSouthAfricanIdNumber("  8001015009087  ");

        result.IsSuccess.ShouldBeTrue();
        user.SouthAfricanIdNumber.ShouldBe("8001015009087");
    }

    [Fact]
    public void SetSouthAfricanIdNumber_Should_Fail_And_NotChange_When_Invalid()
    {
        var user = User.Create(Guid.NewGuid(), "c@example.com", "Test", "Customer", "8001015009087");

        Result result = user.SetSouthAfricanIdNumber("8001015009088"); // bad Luhn

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidIdNumber);
        user.SouthAfricanIdNumber.ShouldBe("8001015009087");
    }

    [Fact]
    public void Create_Should_StoreTrimmedIdNumber()
    {
        var user = User.Create(Guid.NewGuid(), "c@example.com", "Test", "Customer", "  9001015800086  ");

        user.SouthAfricanIdNumber.ShouldBe("9001015800086");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_StoreNullIdNumber_When_NotProvided(string? input)
    {
        var user = User.Create(Guid.NewGuid(), "c@example.com", "Test", "Customer", input);

        user.SouthAfricanIdNumber.ShouldBeNull();
    }
}

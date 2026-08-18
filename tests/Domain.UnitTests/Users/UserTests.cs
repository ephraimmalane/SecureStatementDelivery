using Domain.Users;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.Users;

public class UserTests
{
    private const string ValidSaId = "8001015009087";

    private static User CreateUser(string saId = ValidSaId) =>
        User.Create(Guid.NewGuid(), "c@example.com", "Test", "Customer", saId).Value;

    [Fact]
    public void Create_Should_Succeed_And_StoreTrimmedIdNumber_When_Valid()
    {
        Result<User> result = User.Create(
            Guid.NewGuid(), "c@example.com", "Test", "Customer", "  8001015009087  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.SouthAfricanIdNumber.ShouldBe("8001015009087");
    }

    [Theory]
    [InlineData(null)]          // missing
    [InlineData("")]            // empty
    [InlineData("   ")]         // whitespace
    [InlineData("8001015009088")] // valid length/date but bad Luhn check digit
    [InlineData("8013015009087")] // invalid month (13)
    public void Create_Should_Fail_When_IdNumber_Missing_Or_Invalid(string? input)
    {
        Result<User> result = User.Create(
            Guid.NewGuid(), "c@example.com", "Test", "Customer", input!);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidIdNumber);
    }

    [Fact]
    public void SetSouthAfricanIdNumber_Should_Store_When_Valid()
    {
        User user = CreateUser();

        Result result = user.SetSouthAfricanIdNumber("  8001015009087  ");

        result.IsSuccess.ShouldBeTrue();
        user.SouthAfricanIdNumber.ShouldBe("8001015009087");
    }

    [Fact]
    public void SetSouthAfricanIdNumber_Should_Fail_And_NotChange_When_Invalid()
    {
        User user = CreateUser("8001015009087");

        Result result = user.SetSouthAfricanIdNumber("8001015009088"); // bad Luhn

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidIdNumber);
        user.SouthAfricanIdNumber.ShouldBe("8001015009087");
    }
}

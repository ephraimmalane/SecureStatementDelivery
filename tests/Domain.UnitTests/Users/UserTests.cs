using Domain.Users;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.Users;

public class UserTests
{
    [Fact]
    public void Create_Should_Succeed_And_StoreTrimmedIdNumber_When_Valid()
    {
        Result<User> result = User.Create(
            Guid.NewGuid(), "c@example.com", "Test", "Customer", "  8001015009087  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.SouthAfricanIdNumber.ShouldBe("8001015009087");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8001015009088")]
    [InlineData("8013015009087")]
    public void Create_Should_Fail_When_IdNumber_Missing_Or_Invalid(string? input)
    {
        Result<User> result = User.Create(
            Guid.NewGuid(), "c@example.com", "Test", "Customer", input!);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidIdNumber);
    }
}

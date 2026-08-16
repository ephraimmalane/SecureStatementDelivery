using Domain.DownloadTokens;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.DownloadTokens;

public class DownloadTokenTests
{
    private static DownloadToken Create(DateTime expiresAt) =>
        DownloadToken.Create(
            id: Guid.NewGuid(),
            statementId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            tokenHash: "hash",
            expiresAt: expiresAt);

    private static DownloadToken CreateValid() =>
        Create(DateTime.UtcNow.AddMinutes(5));

    [Fact]
    public void MarkAsUsed_Should_Succeed_OnFirstUse()
    {
        DownloadToken token = CreateValid();

        Result result = token.MarkAsUsed();

        result.IsSuccess.ShouldBeTrue();
        token.IsUsed.ShouldBeTrue();
        token.UsedAt.ShouldNotBeNull();
    }

    [Fact]
    public void MarkAsUsed_Should_Fail_OnSecondUse_EnforcingSingleUse()
    {
        DownloadToken token = CreateValid();
        token.MarkAsUsed();

        Result second = token.MarkAsUsed();

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBe(DownloadTokenErrors.TokenAlreadyUsed);
    }

    [Fact]
    public void MarkAsUsed_Should_Fail_When_Expired()
    {
        DownloadToken token = Create(DateTime.UtcNow.AddMinutes(-1));

        Result result = token.MarkAsUsed();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DownloadTokenErrors.TokenExpired);
        token.IsUsed.ShouldBeFalse();
    }

    [Fact]
    public void IsValid_Should_BeFalse_AfterUse()
    {
        DownloadToken token = CreateValid();
        token.MarkAsUsed();

        token.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void IsValid_Should_BeFalse_When_Expired()
    {
        DownloadToken token = Create(DateTime.UtcNow.AddSeconds(-1));

        token.IsValid.ShouldBeFalse();
        token.IsExpired.ShouldBeTrue();
    }
}

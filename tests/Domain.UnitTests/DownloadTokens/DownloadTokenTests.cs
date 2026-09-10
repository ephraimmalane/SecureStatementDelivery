using Domain.DownloadTokens;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.DownloadTokens;

public class DownloadTokenTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static DownloadToken Create(DateTime expiresAt) =>
        DownloadToken.Create(
            id: Guid.NewGuid(),
            statementId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            tokenHash: "hash",
            expiresAt: expiresAt,
            createdAt: Now);

    private static DownloadToken CreateValid() =>
        Create(Now.AddMinutes(5));

    [Fact]
    public void MarkAsUsed_Should_Succeed_OnFirstUse()
    {
        DownloadToken token = CreateValid();

        Result result = token.MarkAsUsed(Now);

        result.IsSuccess.ShouldBeTrue();
        token.IsUsed.ShouldBeTrue();
        token.UsedAt.ShouldBe(Now);
    }

    [Fact]
    public void MarkAsUsed_Should_Fail_OnSecondUse_EnforcingSingleUse()
    {
        DownloadToken token = CreateValid();
        token.MarkAsUsed(Now);

        Result second = token.MarkAsUsed(Now);

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBe(DownloadTokenErrors.TokenAlreadyUsed);
    }

    [Fact]
    public void MarkAsUsed_Should_Fail_When_Expired()
    {
        DownloadToken token = Create(Now.AddMinutes(-1));

        Result result = token.MarkAsUsed(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DownloadTokenErrors.TokenExpired);
        token.IsUsed.ShouldBeFalse();
    }

    [Fact]
    public void IsValid_Should_BeFalse_AfterUse()
    {
        DownloadToken token = CreateValid();
        token.MarkAsUsed(Now);

        token.IsValid(Now).ShouldBeFalse();
    }

    [Fact]
    public void IsValid_Should_BeFalse_When_Expired()
    {
        DownloadToken token = Create(Now.AddSeconds(-1));

        token.IsValid(Now).ShouldBeFalse();
        token.IsExpired(Now).ShouldBeTrue();
    }

    [Fact]
    public void IsExpired_Should_BeFalse_JustBefore_Expiry()
    {
        DownloadToken token = Create(Now.AddSeconds(1));

        token.IsExpired(Now).ShouldBeFalse();
        token.IsValid(Now).ShouldBeTrue();
    }
}

using Domain.Statements;
using Domain.Statements.Events;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.Statements;

public class StatementTests
{
    private static Result<Statement> CreateValid(
        string period = "2024-01",
        string? idempotencyKey = null) =>
        Statement.Create(
            customerId: Guid.NewGuid(),
            uploadedByAdminId: Guid.NewGuid(),
            originalFileName: "statement.pdf",
            storagePath: "statements/x/statement.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            period: period,
            description: "January statement",
            isPasswordProtected: false,
            idempotencyKey: idempotencyKey);

    [Theory]
    [InlineData("2024-13")] // month out of range
    [InlineData("2024-00")] // month zero
    [InlineData("24-01")]   // two-digit year
    [InlineData("2024-1")]  // single-digit month
    [InlineData("not-a-period")]
    [InlineData("")]
    public void Create_Should_Fail_When_PeriodIsInvalid(string period)
    {
        Result<Statement> result = CreateValid(period: period);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(StatementErrors.InvalidPeriodFormat);
    }

    [Fact]
    public void Create_Should_TrimPeriod_So_ReadSideExactMatchSucceeds()
    {
        Result<Statement> result = CreateValid(period: "  2024-07  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Period.ShouldBe("2024-07");
    }

    [Fact]
    public void Create_Should_Succeed_AndStartActive_With_ValidInput()
    {
        Result<Statement> result = CreateValid();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(StatementStatus.Active);
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Create_Should_Raise_StatementUploadedDomainEvent()
    {
        Statement statement = CreateValid().Value;

        statement.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<StatementUploadedDomainEvent>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  key-123  ", "key-123")]
    public void Create_Should_NormalizeIdempotencyKey(string? input, string? expected)
    {
        Statement statement = CreateValid(idempotencyKey: input).Value;

        statement.IdempotencyKey.ShouldBe(expected);
    }

    [Fact]
    public void Revoke_Should_Succeed_FirstTime_And_RecordReason()
    {
        Statement statement = CreateValid().Value;
        var adminId = Guid.NewGuid();

        Result result = statement.Revoke(adminId, "Superseded");

        result.IsSuccess.ShouldBeTrue();
        statement.Status.ShouldBe(StatementStatus.Revoked);
        statement.RevokedReason.ShouldBe("Superseded");
        statement.RevokedByAdminId.ShouldBe(adminId);
    }

    [Fact]
    public void Revoke_Should_Fail_When_AlreadyRevoked()
    {
        Statement statement = CreateValid().Value;
        statement.Revoke(Guid.NewGuid(), "first");

        Result second = statement.Revoke(Guid.NewGuid(), "second");

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBe(StatementErrors.AlreadyRevoked);
    }
}

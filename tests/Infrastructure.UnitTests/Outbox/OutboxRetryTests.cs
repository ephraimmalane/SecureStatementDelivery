using Infrastructure.Outbox;
using Shouldly;

namespace Infrastructure.UnitTests.Outbox;

public sealed class OutboxRetryTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static OutboxOptions Options(int maxRetries = 3, int baseDelay = 10, int maxDelay = 600) =>
        new() { MaxRetries = maxRetries, BaseRetryDelaySeconds = baseDelay, MaxRetryDelaySeconds = maxDelay };

    [Fact]
    public void RecordFailure_Should_ScheduleRetry_And_NotMarkProcessed_WhenUnderMax()
    {
        var message = new OutboxMessage();

        bool deadLettered = OutboxProcessor.RecordFailure(message, "boom", Now, Options());

        deadLettered.ShouldBeFalse();
        message.RetryCount.ShouldBe(1);
        message.Error.ShouldBe("boom");
        message.ProcessedOnUtc.ShouldBeNull();
        message.NextAttemptUtc.ShouldBe(Now.AddSeconds(10));
    }

    [Fact]
    public void RecordFailure_Should_DeadLetter_WhenMaxRetriesReached()
    {
        var message = new OutboxMessage { RetryCount = 2 };

        bool deadLettered = OutboxProcessor.RecordFailure(message, "still failing", Now, Options());

        deadLettered.ShouldBeTrue();
        message.RetryCount.ShouldBe(3);
        message.Error.ShouldBe("still failing");
        message.ProcessedOnUtc.ShouldBe(Now);
        message.NextAttemptUtc.ShouldBeNull();
    }

    [Fact]
    public void RecordFailure_Should_ApplyExponentialBackoff_CappedAtMax()
    {
        OutboxOptions options = Options(maxRetries: 10, baseDelay: 10, maxDelay: 50);
        var message = new OutboxMessage();

        OutboxProcessor.RecordFailure(message, "e", Now, options);
        message.NextAttemptUtc.ShouldBe(Now.AddSeconds(10));

        OutboxProcessor.RecordFailure(message, "e", Now, options);
        message.NextAttemptUtc.ShouldBe(Now.AddSeconds(20));

        OutboxProcessor.RecordFailure(message, "e", Now, options);
        message.NextAttemptUtc.ShouldBe(Now.AddSeconds(40));

        OutboxProcessor.RecordFailure(message, "e", Now, options);
        message.NextAttemptUtc.ShouldBe(Now.AddSeconds(50));
    }
}

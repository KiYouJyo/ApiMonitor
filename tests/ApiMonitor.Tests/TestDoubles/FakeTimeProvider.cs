namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } =
        new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void AdvanceMinutes(int minutes) => UtcNow = UtcNow.AddMinutes(minutes);
}

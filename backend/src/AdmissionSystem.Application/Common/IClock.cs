namespace AdmissionSystem.Application.Common;

public interface IClock
{
    /// <summary>
    /// Returns PostgreSQL transaction timestamp (<c>SELECT now()</c>) for lock-free read queries.
    /// </summary>
    Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns real-time PostgreSQL wall clock (<c>SELECT clock_timestamp()</c>) evaluated AFTER row locks are acquired.
    /// </summary>
    Task<DateTimeOffset> GetClockTimestampAsync(CancellationToken cancellationToken = default);
}


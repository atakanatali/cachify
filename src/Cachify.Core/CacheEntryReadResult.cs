namespace Cachify.Core;

/// <summary>
/// Represents the outcome of reading a cache entry including freshness information.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
internal readonly struct CacheEntryReadResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheEntryReadResult{T}"/> struct.
    /// </summary>
    /// <param name="value">The cached value.</param>
    /// <param name="state">The entry state.</param>
    /// <param name="reason">The stale reason, when applicable.</param>
    public CacheEntryReadResult(T? value, CacheEntryState state, CacheEntryStaleReason reason)
    {
        Value = value;
        State = state;
        StaleReason = reason;
    }

    /// <summary>
    /// Gets the cached value when present.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets the cache entry state.
    /// </summary>
    public CacheEntryState State { get; }

    /// <summary>
    /// Gets the reason the entry was marked stale.
    /// </summary>
    public CacheEntryStaleReason StaleReason { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is fresh.
    /// </summary>
    public bool IsFresh => State == CacheEntryState.Fresh;

    /// <summary>
    /// Gets a value indicating whether the entry is stale.
    /// </summary>
    public bool IsStale => State == CacheEntryState.Stale;
}

/// <summary>
/// Represents the freshness state of a cache entry.
/// </summary>
internal enum CacheEntryState
{
    /// <summary>
    /// The entry is fresh and eligible to be served normally.
    /// </summary>
    Fresh,

    /// <summary>
    /// The entry is stale but still within the fail-safe window.
    /// </summary>
    Stale,

    /// <summary>
    /// The entry is missing or fully expired.
    /// </summary>
    Miss
}

/// <summary>
/// Indicates why a stale value was returned.
/// </summary>
internal enum CacheEntryStaleReason
{
    /// <summary>
    /// The entry is not stale.
    /// </summary>
    None,

    /// <summary>
    /// The entry exceeded its logical TTL but is within the fail-safe window.
    /// </summary>
    Expired,

    /// <summary>
    /// The entry was returned due to an L2 failure.
    /// </summary>
    L2Failure,

    /// <summary>
    /// The entry was returned because the factory threw.
    /// </summary>
    FactoryFailure,

    /// <summary>
    /// The entry was returned because a soft timeout elapsed.
    /// </summary>
    SoftTimeout,

    /// <summary>
    /// The entry was returned because a hard timeout elapsed.
    /// </summary>
    HardTimeout
}

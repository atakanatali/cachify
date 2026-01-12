namespace Cachify.AspNetCore;

/// <summary>
/// Maintains a bounded, bucketed index for similarity request caching.
/// </summary>
public sealed class SimilarityRequestIndex : ISimilarityRequestIndex
{
    private readonly object _lock = new();
    private readonly int _maxEntries;
    private readonly Dictionary<string, SimilarityIndexEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, HashSet<string>> _buckets = new();
    private readonly LinkedList<string> _lru = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SimilarityRequestIndex"/> class.
    /// </summary>
    /// <param name="options">The request cache options.</param>
    public SimilarityRequestIndex(Microsoft.Extensions.Options.IOptions<RequestCacheOptions> options)
    {
        _maxEntries = Math.Max(options.Value.Similarity.MaxIndexEntries, 1);
    }

    /// <inheritdoc />
    public IReadOnlyList<SimilarityIndexEntry> GetCandidates(ulong signature, int maxCandidates)
    {
        var candidates = new List<SimilarityIndexEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var buckets = GetBuckets(signature);

        lock (_lock)
        {
            foreach (var bucket in buckets)
            {
                if (!_buckets.TryGetValue(bucket, out var keys))
                {
                    continue;
                }

                foreach (var key in keys)
                {
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    if (_entries.TryGetValue(key, out var entry))
                    {
                        candidates.Add(entry);
                    }

                    if (candidates.Count >= maxCandidates)
                    {
                        return candidates;
                    }
                }
            }
        }

        return candidates;
    }

    /// <inheritdoc />
    public void AddOrUpdate(SimilarityIndexEntry entry)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(entry.CacheKey, out var existing))
            {
                RemoveFromBuckets(entry.CacheKey, existing.Signature);
                _entries[entry.CacheKey] = entry;
                AddToBuckets(entry.CacheKey, entry.Signature);
                MoveToFront(entry.CacheKey);
                return;
            }

            _entries[entry.CacheKey] = entry;
            AddToBuckets(entry.CacheKey, entry.Signature);
            _lru.AddFirst(entry.CacheKey);

            while (_entries.Count > _maxEntries && _lru.Last is not null)
            {
                Remove(_lru.Last.Value);
            }
        }
    }

    /// <inheritdoc />
    public void Remove(string cacheKey)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(cacheKey, out var entry))
            {
                return;
            }

            RemoveFromBuckets(cacheKey, entry.Signature);
            _entries.Remove(cacheKey);
            var node = _lru.Find(cacheKey);
            if (node is not null)
            {
                _lru.Remove(node);
            }
        }
    }

    /// <summary>
    /// Moves a cache key to the front of the LRU list.
    /// </summary>
    /// <param name="cacheKey">The cache key to move.</param>
    private void MoveToFront(string cacheKey)
    {
        var node = _lru.Find(cacheKey);
        if (node is null)
        {
            return;
        }

        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    /// <summary>
    /// Adds a cache key to the similarity buckets for the provided signature.
    /// </summary>
    /// <param name="cacheKey">The cache key to add.</param>
    /// <param name="signature">The request signature.</param>
    private void AddToBuckets(string cacheKey, ulong signature)
    {
        foreach (var bucket in GetBuckets(signature))
        {
            if (!_buckets.TryGetValue(bucket, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                _buckets[bucket] = keys;
            }

            keys.Add(cacheKey);
        }
    }

    /// <summary>
    /// Removes a cache key from the similarity buckets for the provided signature.
    /// </summary>
    /// <param name="cacheKey">The cache key to remove.</param>
    /// <param name="signature">The request signature.</param>
    private void RemoveFromBuckets(string cacheKey, ulong signature)
    {
        foreach (var bucket in GetBuckets(signature))
        {
            if (_buckets.TryGetValue(bucket, out var keys))
            {
                keys.Remove(cacheKey);
                if (keys.Count == 0)
                {
                    _buckets.Remove(bucket);
                }
            }
        }
    }

    /// <summary>
    /// Splits a 64-bit signature into four bucket identifiers.
    /// </summary>
    /// <param name="signature">The request signature.</param>
    /// <returns>The bucket identifiers.</returns>
    private static ushort[] GetBuckets(ulong signature)
    {
        return
        [
            (ushort)(signature & 0xFFFF),
            (ushort)((signature >> 16) & 0xFFFF),
            (ushort)((signature >> 32) & 0xFFFF),
            (ushort)((signature >> 48) & 0xFFFF)
        ];
    }
}

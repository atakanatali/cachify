using Cachify.AspNetCore;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cachify.Tests;

public sealed class SimilarityRequestIndexTests
{
    [Fact]
    public void IndexReturnsCandidatesFromMatchingBuckets()
    {
        var options = Options.Create(new RequestCacheOptions
        {
            Similarity = { MaxIndexEntries = 10 }
        });
        var index = new SimilarityRequestIndex(options);

        var entry = new SimilarityIndexEntry
        {
            CacheKey = "cache:key:1",
            Signature = 0x0000_1111_2222_3333,
            TokenCount = 10,
            HashPrefix = 1,
            CachedAt = DateTimeOffset.UtcNow
        };

        index.AddOrUpdate(entry);

        var candidates = index.GetCandidates(0x0000_1111_2222_3333, maxCandidates: 5);

        candidates.Should().ContainSingle(candidate => candidate.CacheKey == entry.CacheKey);
    }

    [Fact]
    public void IndexEvictsLeastRecentlyUsedEntries()
    {
        var options = Options.Create(new RequestCacheOptions
        {
            Similarity = { MaxIndexEntries = 1 }
        });
        var index = new SimilarityRequestIndex(options);

        var first = new SimilarityIndexEntry
        {
            CacheKey = "cache:key:1",
            Signature = 0xAAAA_BBBB_CCCC_DDDD,
            TokenCount = 5,
            HashPrefix = 1,
            CachedAt = DateTimeOffset.UtcNow
        };

        var second = new SimilarityIndexEntry
        {
            CacheKey = "cache:key:2",
            Signature = 0x1111_2222_3333_4444,
            TokenCount = 5,
            HashPrefix = 2,
            CachedAt = DateTimeOffset.UtcNow
        };

        index.AddOrUpdate(first);
        index.AddOrUpdate(second);

        var candidates = index.GetCandidates(first.Signature, maxCandidates: 5);

        candidates.Should().BeEmpty();
    }
}

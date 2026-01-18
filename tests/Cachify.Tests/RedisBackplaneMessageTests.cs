using System.Collections.Generic;
using Cachify.Abstractions;
using Cachify.Redis;
using FluentAssertions;
using Xunit;

namespace Cachify.Tests;

public sealed class RedisBackplaneMessageTests
{
    [Fact]
    public void SerializeDeserialize_SingleKey_RoundTrips()
    {
        var invalidation = CacheInvalidation.ForKey("user:1", "node-a");
        var message = RedisBackplaneMessage.CreateSingle(invalidation);

        var payload = message.Serialize();

        RedisBackplaneMessage.TryDeserialize(payload, out var parsed).Should().BeTrue();
        var results = parsed.ToInvalidations();

        results.Should().ContainSingle(result =>
            result.Key == "user:1" &&
            result.Tag == null &&
            result.SourceId == "node-a");
    }

    [Fact]
    public void SerializeDeserialize_Batch_RoundTrips()
    {
        var invalidations = new List<CacheInvalidation>
        {
            CacheInvalidation.ForKey("user:1", "node-b"),
            CacheInvalidation.ForTag("users", "node-b")
        };

        var message = RedisBackplaneMessage.CreateBatch("node-b", invalidations);
        var payload = message.Serialize();

        RedisBackplaneMessage.TryDeserialize(payload, out var parsed).Should().BeTrue();
        parsed.ToInvalidations().Should().HaveCount(2);
    }

    [Fact]
    public void TryDeserialize_InvalidVersion_ReturnsFalse()
    {
        var payload = "{\"v\":99,\"src\":\"node\",\"key\":\"user:1\"}";

        RedisBackplaneMessage.TryDeserialize(payload, out _).Should().BeFalse();
    }
}

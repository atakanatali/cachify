using Cachify.AspNetCore;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cachify.Tests;

public sealed class SimilarityCanonicalizerTests
{
    [Fact]
    public void CanonicalizerSortsJsonFieldsAndRemovesNoise()
    {
        var options = Options.Create(new RequestCacheOptions());
        var canonicalizer = new JsonRequestCanonicalizer(options);

        var payloadA = """{"id":"123","prompt":"Hello","temperature":0.2}""";
        var payloadB = """{"temperature":0.2,"prompt":"Hello","id":"999"}""";

        var canonicalA = canonicalizer.Canonicalize(payloadA, "application/json");
        var canonicalB = canonicalizer.Canonicalize(payloadB, "application/json");

        canonicalA.Should().Be(canonicalB);
        canonicalA.Should().Be("""{"prompt":"Hello","temperature":0.2}""");
    }

    [Fact]
    public void CanonicalizerNormalizesTextPayloads()
    {
        var options = Options.Create(new RequestCacheOptions());
        var canonicalizer = new JsonRequestCanonicalizer(options);

        var canonical = canonicalizer.Canonicalize("  Hello \n WORLD  ", "text/plain");

        canonical.Should().Be("hello world");
    }
}

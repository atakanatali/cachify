using BenchmarkDotNet.Attributes;
using Cachify.AspNetCore;

namespace Cachify.Tests;

/// <summary>
/// Benchmarks SimHash signature generation for canonical payloads.
/// </summary>
[MemoryDiagnoser]
public sealed class SimilaritySignatureBenchmarks
{
    private readonly SimHashSignatureBuilder _builder = new();
    private readonly string _payload = "{\"prompt\":\"Explain caching\",\"temperature\":0.2}";

    /// <summary>
    /// Measures signature generation for a typical LLM payload.
    /// </summary>
    [Benchmark]
    public (ulong Signature, int TokenCount) BuildSignature()
    {
        return _builder.BuildSignature(_payload, maxTokens: 128);
    }
}

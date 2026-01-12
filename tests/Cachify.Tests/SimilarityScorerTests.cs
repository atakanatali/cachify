using Cachify.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Cachify.Tests;

public sealed class SimilarityScorerTests
{
    [Fact]
    public void SimHashScoresSimilarPayloadsHighly()
    {
        var builder = new SimHashSignatureBuilder();
        var scorer = new SimHashSimilarityScorer();

        var (signatureA, tokensA) = builder.BuildSignature("hello world", maxTokens: 64);
        var (signatureB, tokensB) = builder.BuildSignature("hello world!", maxTokens: 64);

        var score = scorer.Score(
            new SimilarityRequestFeatures(signatureA, tokensA, 0),
            new SimilarityRequestFeatures(signatureB, tokensB, 0));

        score.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void SimHashScoresDifferentPayloadsLower()
    {
        var builder = new SimHashSignatureBuilder();
        var scorer = new SimHashSimilarityScorer();

        var (signatureA, tokensA) = builder.BuildSignature("hello world", maxTokens: 64);
        var (signatureB, tokensB) = builder.BuildSignature("completely different text", maxTokens: 64);

        var score = scorer.Score(
            new SimilarityRequestFeatures(signatureA, tokensA, 0),
            new SimilarityRequestFeatures(signatureB, tokensB, 0));

        score.Should().BeLessThan(0.8);
    }
}

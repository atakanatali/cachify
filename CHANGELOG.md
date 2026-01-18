# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-01-18

### Fixed
- Fixed synchronous IO bug in `RequestCacheService.ApplyCachedResponse` that caused `InvalidOperationException` in TestHost
- Fixed expression tree pattern matching issue in `RedisBackplaneMessageTests`
- Fixed missing `Microsoft.AspNetCore.Http` using directive in `RequestCachingMiddlewareTests`
- Removed `ConfigureAwait(false)` calls from test methods to comply with xUnit1030 analyzer

### Changed
- Updated FluentAssertions to 8.0.1 for .NET 9 compatibility
- Updated BenchmarkDotNet to 0.15.0 for .NET 9 compatibility  
- Added Microsoft.NET.Test.Sdk 17.12.0 for proper test execution
- Separated benchmarks into dedicated `Cachify.Benchmarks` project
- Updated package metadata with proper author info and description
- Added SourceLink support for debugging NuGet packages

## [0.1.0] - Unreleased

### Added
- Resiliency MVP in the composite cache: fail-safe stale fallback, soft/hard factory timeouts, background refresh,
  and observability counters/tags.
- Similarity request caching for LLM/AI workloads (optional)
- Request/response caching middleware for ASP.NET Core
- Redis backplane support for distributed L1 invalidation
- Comprehensive observability with metrics and tracing
- Layered L1/L2 caching with Memory and Redis providers
- Stampede protection for concurrent cache miss scenarios
- TTL jitter to prevent synchronized expirations

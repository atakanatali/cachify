using Microsoft.AspNetCore.Builder;

namespace Cachify.AspNetCore;

/// <summary>
/// Provides endpoint convention extensions for request caching.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    /// <summary>
    /// Adds request cache metadata to an endpoint.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="configure">The policy configuration action.</param>
    public static TBuilder WithRequestCaching<TBuilder>(
        this TBuilder builder,
        Action<RequestCachePolicy> configure)
        where TBuilder : IEndpointConventionBuilder
    {
        var policy = new RequestCachePolicy();
        configure(policy);
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(policy));
        return builder;
    }
}

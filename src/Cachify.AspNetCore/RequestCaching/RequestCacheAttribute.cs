using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;

namespace Cachify.AspNetCore;

/// <summary>
/// Attribute that enables request caching for an endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequestCacheAttribute : Attribute, IEndpointMetadataProvider
{
    /// <summary>
    /// Gets or sets the cache duration in seconds.
    /// </summary>
    public int DurationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets a value indicating whether request bodies are included in cache keys.
    /// </summary>
    public bool IncludeRequestBody { get; set; }

    /// <summary>
    /// Gets or sets the header names to vary the cache by.
    /// </summary>
    public string[]? VaryByHeaders { get; set; }

    /// <summary>
    /// Gets or sets the HTTP methods eligible for caching.
    /// </summary>
    public string[]? CacheableMethods { get; set; }

    /// <summary>
    /// Adds request cache metadata to the endpoint builder for the annotated method.
    /// </summary>
    /// <param name="method">The reflected method info.</param>
    /// <param name="builder">The endpoint builder.</param>
    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        foreach (var attribute in method.GetCustomAttributes<RequestCacheAttribute>(inherit: true))
        {
            builder.Metadata.Add(attribute.ToPolicy());
        }
    }

    /// <summary>
    /// Converts the attribute values into a request cache policy.
    /// </summary>
    /// <returns>The request cache policy derived from the attribute.</returns>
    internal RequestCachePolicy ToPolicy()
    {
        return new RequestCachePolicy
        {
            Duration = TimeSpan.FromSeconds(DurationSeconds),
            IncludeRequestBody = IncludeRequestBody,
            VaryByHeaders = VaryByHeaders,
            CacheableMethods = CacheableMethods
        };
    }
}

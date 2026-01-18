using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Cachify.AspNetCore;

internal sealed class RequestCacheKeyBuilder : IRequestCacheKeyBuilder
{
    /// <inheritdoc />
    public async Task<string?> BuildCacheKeyAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var builder = new StringBuilder();

        if (decision.KeyOptions.IncludeMethod)
        {
            builder.Append(request.Method);
        }

        if (decision.KeyOptions.IncludePath)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var path = request.Path.Value ?? string.Empty;
            if (decision.KeyOptions.NormalizePathToLowercase)
            {
                path = path.ToLowerInvariant();
            }

            builder.Append(path);
        }

        if (decision.KeyOptions.IncludeQueryString)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var queryPairs = request.Query
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value.Select(value => (pair.Key, Value: value)))
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var queryBuilder = new StringBuilder();
            foreach (var (key, value) in queryPairs)
            {
                if (queryBuilder.Length > 0)
                {
                    queryBuilder.Append('&');
                }

                queryBuilder.Append(key).Append('=').Append(value);
            }

            builder.Append(queryBuilder);
        }

        if (decision.KeyOptions.IncludeHeaders)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var headerBuilder = new StringBuilder();
            foreach (var header in decision.KeyOptions.VaryByHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
            {
                if (!request.Headers.TryGetValue(header, out var values))
                {
                    continue;
                }

                if (headerBuilder.Length > 0)
                {
                    headerBuilder.Append('&');
                }

                headerBuilder.Append(header!.ToLowerInvariant()).Append('=');
                headerBuilder.Append(string.Join(',', values!.Where(v => v is not null).Select(v => v!.Trim()!)));
            }

            builder.Append(headerBuilder);
        }

        if (decision.KeyOptions.IncludeBody)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var bodyHash = await RequestUtilities.ReadRequestBodyHashAsync(request, decision.MaxRequestBodySizeBytes, cancellationToken).ConfigureAwait(false);
            if (bodyHash is null)
            {
                return null;
            }

            builder.Append(bodyHash);
        }

        var canonical = builder.ToString();
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"http:req:{Convert.ToHexString(hash)}";
    }
}

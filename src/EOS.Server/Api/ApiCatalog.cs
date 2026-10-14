using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace EOS.Server.Api;

/// <summary>
/// A small OpenAPI 3.0 document generated from the endpoint table (paths, methods, route/query parameters, tags). It is enough for
/// agents and generated clients to discover every endpoint; request-body schemas are documented by the typed bodies in the source.
/// (The stock OpenAPI packages currently carry advisories, so none is used.)
/// </summary>
public static class ApiCatalog
{
    public static object Build(EndpointDataSource source, string version)
    {
        var paths = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var e in source.Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = e.RoutePattern.RawText ?? "";
            if (!pattern.StartsWith("/api/v1", StringComparison.Ordinal)) continue;
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            var openApiPath = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(\w+)(:\w+)?\??\}", "{$1}");
            var tag = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(2).FirstOrDefault() ?? "root";
            var routeParams = e.RoutePattern.Parameters.Select(p => new { name = p.Name, @in = "path", required = true, schema = new { type = p.ParameterPolicies.Any(x => x.Content == "int") ? "integer" : "string" } }).ToList<object>();

            foreach (var method in methods)
            {
                if (!paths.TryGetValue(openApiPath, out var item)) paths[openApiPath] = item = new Dictionary<string, object>();
                item[method.ToLowerInvariant()] = new
                {
                    tags = new[] { tag },
                    operationId = $"{method.ToLowerInvariant()}_{string.Join('_', openApiPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(2).Select(s => s.Trim('{', '}')))}",
                    parameters = routeParams,
                    responses = new Dictionary<string, object> { ["200"] = new { description = "OK" }, ["400"] = new { description = "Validation problem" }, ["401"] = new { description = "Missing or invalid token" } },
                    security = new[] { new Dictionary<string, string[]> { ["bearer"] = [] } },
                };
            }
        }
        return new
        {
            openapi = "3.0.3",
            info = new { title = "EOS V2 API", version, description = "Authenticate with `Authorization: Bearer eos_...`. Dates are ISO yyyy-MM-dd. See docs/SPEC.md." },
            paths,
            components = new { securitySchemes = new { bearer = new { type = "http", scheme = "bearer" } } },
        };
    }
}

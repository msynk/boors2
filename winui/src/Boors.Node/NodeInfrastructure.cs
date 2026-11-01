using System.Security.Cryptography;
using System.Text;
using Boors.Market.Filters;
using Boors.Market.Ingestion;
using Boors.Market.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Boors.Node;

/// <summary>The optional operator bearer token (BOORS_OPERATOR_TOKEN). When empty, operator endpoints are open.</summary>
public sealed class OperatorToken(string? value)
{
    private readonly byte[]? expected = string.IsNullOrWhiteSpace(value) ? null : Encoding.UTF8.GetBytes(value.Trim());

    public bool Required => expected is not null;

    public bool Accepts(string? authorization)
    {
        if (expected is null)
            return true;
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorization["Bearer ".Length..].Trim()), expected);
    }
}

/// <summary>Guards RunJob and the sync endpoints with <see cref="OperatorToken"/>.</summary>
public sealed class OperatorTokenFilter(OperatorToken token, ILogger<OperatorTokenFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (token.Accepts(context.HttpContext.Request.Headers.Authorization))
            return await next(context);

        logger.LogWarning("Rejected operator request {Method} {Path} from {Remote}: missing or wrong bearer token",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path, context.HttpContext.Connection.RemoteIpAddress);
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "This endpoint needs the node's operator token (Authorization: Bearer <BOORS_OPERATOR_TOKEN>).");
    }
}

/// <summary>Turns API refusals into RFC 9457 problem details (400/404/503), and anything else into a 500 without internals.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            MarketApiException e => (e.StatusCode, e.StatusCode switch { 404 => "Not found", 503 => "Unavailable", _ => "Bad request" }, e.Message),
            FilterSyntaxException e => (400, "Bad request", $"Filter error: {e.Message}"),
            BadHttpRequestException e => (e.StatusCode, "Bad request", e.Message),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Client closed request", null),
            _ => (500, "Server error", "An unexpected error occurred; see the node's log."),
        };
        if (status == 500)
            logger.LogError(exception, "Unhandled error on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}

/// <summary>Degraded when a dataset is stale or a job failed; never Unhealthy, since the node still serves what it has.</summary>
public sealed class MarketDataHealthCheck(DataHealthService healthService, IOptions<Boors.Market.MarketDataOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var report = healthService.GetReport();
        if (report.Healthy)
            return HealthCheckResult.Healthy($"Market data current to {report.ExpectedSession:yyyy-MM-dd}.");

        // Freshness only means something on a node that collects; a read-only replica reports it as information.
        var problems = report.Datasets.Where(d => d.Status is DatasetStatus.Stale or DatasetStatus.Empty).Select(d => $"{d.Dataset}: {d.Status}")
            .Concat(report.LatestRuns.Where(r => r.Status is "failed" or "partial").Select(r => $"job {r.Job}: {r.Status}"))
            .Concat(report.Sources.Where(s => s.Verdict is not ("ok" or "reachable")).Select(s => $"source {s.Name}: {s.Verdict}"));
        var description = string.Join("; ", problems);
        return options.Value.Collect ? HealthCheckResult.Degraded(description) : HealthCheckResult.Healthy($"Replica (not collecting). {description}");
    }, cancellationToken);
}

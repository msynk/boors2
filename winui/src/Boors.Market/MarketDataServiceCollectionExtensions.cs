using System.Net;
using System.Threading.RateLimiting;
using Boors.Market.RawCache;
using Boors.Market.Ingestion;
using Boors.Market.Rules;
using Boors.Market.Storage;
using Boors.Market.Sources.Codal;
using Boors.Market.Sources.Funds;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Boors.Market;

public static class MarketDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the raw cache, market rules and the TSETMC/Codal clients. Each source gets its own HttpClient with a
    /// browser User-Agent and a resilience pipeline: total timeout → retry (exponential + jitter, also on 403) →
    /// circuit breaker → token-bucket rate limiter → per-attempt timeout.
    /// </summary>
    public static IServiceCollection AddBoorsMarketData(this IServiceCollection services, Action<MarketDataOptions>? configure = null)
    {
        var optionsBuilder = services.AddOptions<MarketDataOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IRawCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;
            return new FileSystemRawCache(new RawCacheOptions { Root = options.DataRoot, Node = options.Node });
        });

        services.TryAddSingleton(sp =>
        {
            var path = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value.RulesPath;
            return string.IsNullOrWhiteSpace(path) ? MarketRules.LoadDefault() : MarketRules.LoadFile(path);
        });

        // The store is a single DuckDB file per node; one instance per process (DuckDB allows one writing process).
        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;
            return MarketStore.Open(options.StorePath ?? Path.Combine(options.DataRoot, "market.duckdb"));
        });
        services.TryAddTransient<MarketCollector>();
        services.TryAddTransient<DataHealthService>();
        services.TryAddTransient<Contracts.SourceContracts>();
        services.TryAddTransient<Analytics.MarketAnalytics>();
        services.TryAddTransient<Analytics.MarketWatchService>();
        services.TryAddTransient<Analytics.MarketReports>();

        AddSourceClient<TsetmcClient>(services, "tsetmc", o => o.TsetmcRequestsPerSecond);
        AddSourceClient<CodalClient>(services, "codal", o => o.CodalRequestsPerSecond);
        AddSourceClient<TgjuClient>(services, "tgju", o => o.OtherSourcesRequestsPerSecond);
        AddSourceClient<FipiranClient>(services, "fipiran", o => o.OtherSourcesRequestsPerSecond);

        return services;
    }

    private static void AddSourceClient<TClient>(IServiceCollection services, string name, Func<MarketDataOptions, double> requestsPerSecond)
        where TClient : class
    {
        services.AddHttpClient<TClient>(ConfigureBrowserLikeClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler)
            .AddResilienceHandler(name, (builder, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<MarketDataOptions>>().Value;
                ConfigurePipeline(builder, options, requestsPerSecond(options));
            });
    }

    private static void ConfigureBrowserLikeClient(IServiceProvider services, HttpClient client)
    {
        var options = services.GetRequiredService<IOptions<MarketDataOptions>>().Value;

        // Timeouts are owned by the resilience pipeline.
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fa-IR,fa;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.tsetmc.com/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.tsetmc.com");
    }

    private static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private static void ConfigurePipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder, MarketDataOptions options, double requestsPerSecond)
    {
        // Rates below 1/s become one permit per (1 / rate) seconds; rates above 1/s become N permits per second.
        var permits = requestsPerSecond >= 1 ? (int)Math.Round(requestsPerSecond) : 1;
        var period = requestsPerSecond >= 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1 / Math.Max(requestsPerSecond, 0.01));

        builder
            .AddTimeout(options.TotalTimeout)
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                // Transient failures plus 403: TSETMC also answers 403 when it is shedding load.
                ShouldHandle = args => ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome)
                    || args.Outcome.Result?.StatusCode == HttpStatusCode.Forbidden),
            })
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromMinutes(1),
                BreakDuration = TimeSpan.FromMinutes(1),
            })
            .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = permits,
                TokensPerPeriod = permits,
                ReplenishmentPeriod = period,
                QueueLimit = 100_000,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }))
            .AddTimeout(options.AttemptTimeout);
    }
}

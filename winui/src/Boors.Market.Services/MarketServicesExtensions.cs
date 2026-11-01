using Boors.Market.Services.Jobs;
using Boors.Market.Services.Local;
using Boors.Market.Services.Remote;
using Boors.Market.Services.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Boors.Market.Services;

public static class MarketServicesExtensions
{
    /// <summary>
    /// The in-process API: Boors.Market (store, collectors, sources), <see cref="LocalMarketApi"/>, the single-writer job
    /// queue and its worker, the Iran-time scheduler (idle unless MarketData:Collect), node sync, and the job monitoring
    /// feeds (each job's log lines, HTTP traffic per host).
    /// </summary>
    public static IServiceCollection AddBoorsLocalApi(this IServiceCollection services, Action<MarketDataOptions>? configureData = null, Action<MarketApiOptions>? configureApi = null)
    {
        services.AddBoorsMarketData(configureData);
        var api = services.AddOptions<MarketApiOptions>();
        if (configureApi is not null)
            api.Configure(configureApi);

        services.TryAddSingleton<MarketJobQueue>();
        services.TryAddSingleton<MarketJobScheduler>();
        services.AddHostedService<MarketJobWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<MarketJobScheduler>());
        services.TryAddSingleton<SourceTraffic>();
        services.AddHostedService(sp => sp.GetRequiredService<SourceTraffic>());
        services.TryAddTransient<NodeSync>();
        services.AddHttpClient(NodeSync.HttpClientName, client =>
        {
            // Bundles can be hundreds of MB over a slow international link.
            client.Timeout = TimeSpan.FromHours(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Boors.Node/1.0");
        });
        services.TryAddSingleton<LocalMarketApi>();
        services.TryAddSingleton<IMarketApi>(sp => sp.GetRequiredService<LocalMarketApi>());

        // Job progress and logs come from the collectors' own log lines (Information and up) and the retry policy's
        // events (Warning and up), whatever the global log level is.
        services.AddSingleton<ILoggerProvider, JobProgressLoggerProvider>();
        services.Configure<LoggerFilterOptions>(o =>
        {
            o.Rules.Add(new LoggerFilterRule(typeof(JobProgressLoggerProvider).FullName, "Boors.Market", LogLevel.Information, null));
            o.Rules.Add(new LoggerFilterRule(typeof(JobProgressLoggerProvider).FullName, "Polly", LogLevel.Warning, null));
        });
        return services;
    }

    /// <summary>The HTTP client of a Boors.Node, registered as <see cref="IMarketApi"/>.</summary>
    public static IServiceCollection AddBoorsRemoteApi(this IServiceCollection services, RemoteMarketApiOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpClient<RemoteMarketApi>(client =>
        {
            client.BaseAddress = options.BaseAddress;
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddSingleton<IMarketApi>(sp => sp.GetRequiredService<RemoteMarketApi>());
        return services;
    }
}

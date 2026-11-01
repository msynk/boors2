using System.IO.Compression;
using Boors.Market;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Boors.Market.Storage;
using Boors.Node;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// Market data: appsettings "MarketData" section, then the BOORS_DATA / BOORS_NODE environment variables (as the CLI).
builder.Services.AddOptions<MarketDataOptions>()
    .Bind(builder.Configuration.GetSection(MarketDataOptions.SectionName))
    .PostConfigure(options =>
    {
        options.DataRoot = Environment.GetEnvironmentVariable("BOORS_DATA") is { Length: > 0 } data ? data : options.DataRoot;
        options.Node = Environment.GetEnvironmentVariable("BOORS_NODE") is { Length: > 0 } node ? node
            : string.IsNullOrWhiteSpace(options.Node) ? Environment.MachineName : options.Node;
    });
builder.Services.AddOptions<MarketApiOptions>().Bind(builder.Configuration.GetSection(MarketApiOptions.SectionName));
builder.Services.AddBoorsLocalApi();
builder.Services.AddSingleton(new OperatorToken(
    Environment.GetEnvironmentVariable("BOORS_OPERATOR_TOKEN") is { Length: > 0 } token ? token : builder.Configuration["Node:OperatorToken"]));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, MarketJsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddHealthChecks()
    .AddCheck<MarketDataHealthCheck>("marketData", tags: ["data"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseResponseCompression();

// Open the store before taking traffic: fail fast when the file is locked by another process.
var opening = Stopwatch.StartNew();
var store = app.Services.GetRequiredService<MarketStore>();
app.Logger.LogInformation("Market store {StorePath} opened in {Elapsed} ms (node {Node}, collect {Collect}, operator token {TokenState})",
    store.Path_, opening.ElapsedMilliseconds,
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataOptions>>().Value.Node,
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataOptions>>().Value.Collect,
    app.Services.GetRequiredService<OperatorToken>().Required ? "required" : "not set");

app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new() { Predicate = _ => false });
app.MapGet("/", () => Results.Text("Boors.Node — see /health and api/v1/*", "text/plain"));
app.MapMarketApi();

app.Run();

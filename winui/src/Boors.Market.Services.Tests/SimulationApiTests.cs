using Boors.Market.Services.Local;
using Boors.Market.Simulation;
using Microsoft.Extensions.Hosting;

namespace Boors.Market.Services.Tests;

/// <summary>The simulation endpoints: contract defaults, validation and an empty store.</summary>
[TestClass]
public sealed class SimulationApiTests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public void TheContractDefaultsAreTheEngineDefaults()
    {
        // A definition saved before a parameter existed reads back with that parameter's engine default.
        var json = MarketJsonContext.Default.SimulationSpecDto;
        Assert.AreEqual(Serialize(new SimulationSettings().ToDto()), Serialize(new SimulationSettingsDto()));
        Assert.AreEqual(Serialize(new SimulationSettings()), Serialize(new SimulationSettingsDto().ToDomain()));

        var old = JsonSerializer.Deserialize("""{"id":"a","capital":1000,"start":"2026-01-01","revisions":[{"from":"2026-01-01","settings":{"positions":3}}]}""", json)!;
        Assert.AreEqual(3, old.Revisions[0].Settings.Positions);
        Assert.AreEqual(new SimulationSettingsDto().TrailingStop, old.Revisions[0].Settings.TrailingStop);
        Assert.IsEmpty(old.Orders);
        Assert.AreEqual("Automatic", old.Mode);

        static string Serialize(object settings) => JsonSerializer.Serialize(settings, settings.GetType(), new JsonSerializerOptions { WriteIndented = false });
    }

    [TestMethod]
    public void Only_a_sell_with_no_amount_means_everything()
    {
        var day = new DateOnly(2026, 1, 3);
        ManualOrderDto Sell(decimal? value, decimal? shares) => new("o", day, 1, "S", "Sell", value, shares, null, 5, null, null);
        Boors.Market.Simulation.ManualOrder Mapped(ManualOrderDto order) => new SimulationSpecDto
        {
            Capital = 1,
            Start = day,
            Revisions = [new(day, new SimulationSettingsDto())],
            Orders = [order],
        }.ToDomain().Orders.Single();

        Assert.IsNull(Mapped(Sell(null, null)).Shares);
        Assert.AreEqual(5m, Mapped(Sell(null, 5)).Shares);
        Assert.AreEqual(0m, Mapped(Sell(null, 0)).Shares); // a ticket that rounded to nothing: never "everything"

        var byValue = Assert.ThrowsExactly<MarketApiException>(() => Mapped(Sell(1_000_000, null)));
        Assert.AreEqual(400, byValue.StatusCode);
    }

    [TestMethod]
    public void A_definition_on_the_old_defaults_moves_to_fills_at_once_and_a_chosen_open_stays()
    {
        var spec = new SimulationSpecDto
        {
            Revisions =
            [
                new(new DateOnly(2026, 1, 3), new SimulationSettingsDto { Fill = "NextClose", InstantSaleProceeds = false }),
                new(new DateOnly(2026, 2, 3), new SimulationSettingsDto { Fill = "NextOpen", InstantSaleProceeds = false }),
            ],
        };

        spec.UseFillsAtOnce();

        Assert.AreEqual("Immediate", spec.Revisions[0].Settings.Fill);
        Assert.IsTrue(spec.Revisions[0].Settings.InstantSaleProceeds);
        Assert.AreEqual("NextOpen", spec.Revisions[1].Settings.Fill);
        Assert.IsFalse(spec.Revisions[1].Settings.InstantSaleProceeds);
    }

    [TestMethod]
    public void Settings_round_trip_and_out_of_range_numbers_are_clamped()
    {
        foreach (var (key, settings) in SimulationPresets.All)
            Assert.AreEqual(Json(settings), Json(settings.ToDto().ToDomain()), key);

        var wild = new SimulationSettingsDto { Positions = 500, StopLoss = 3, ParticipationCap = -1, Weights = new() { ["Momentum"] = 99, ["Trend"] = 0 } }.ToDomain();
        Assert.AreEqual(50, wild.Positions);
        Assert.AreEqual(0.9, wild.StopLoss);
        Assert.AreEqual(0, wild.ParticipationCap);
        Assert.AreEqual(10, wild.Weights[FactorKind.Momentum]);
        Assert.IsFalse(wild.Weights.ContainsKey(FactorKind.Trend));

        static string Json(SimulationSettings s) => JsonSerializer.Serialize(s with { Weights = s.Weights.Where(w => w.Value > 0).OrderBy(w => w.Key).ToDictionary() });
    }

    [TestMethod]
    public async Task An_empty_store_answers_and_invalid_definitions_are_rejected()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddBoorsLocalApi(o =>
        {
            o.DataRoot = Path.Combine(Path.GetTempPath(), "boors-services-tests", Guid.NewGuid().ToString("N"));
            o.StorePath = ":memory:";
            o.Node = "test";
        }, api => api.Offline = true);
        using var host = builder.Build();
        var api = host.Services.GetRequiredService<IMarketApi>();
        var ct = TestContext.CancellationToken;

        var presets = await api.GetSimulationPresetsAsync(ct);
        Assert.AreSequenceEqual(SimulationPresets.All.Select(p => p.Key).ToArray(), presets.Select(p => p.Key).ToArray());
        Assert.IsTrue(presets.Single(p => p.Key == "adaptive").Settings.Adaptive);

        var spec = new SimulationSpecDto
        {
            Id = "s",
            Capital = 1_000_000_000,
            Start = new DateOnly(2026, 1, 1),
            Revisions = [new(new DateOnly(2026, 1, 1), new SimulationSettingsDto())],
        };
        var result = await api.RunSimulationAsync(new SimulationRunRequestDto(spec, Detail: true), ct);
        Assert.IsNull(result.AsOf);
        Assert.IsEmpty(result.Equity);
        Assert.IsEmpty((await api.TuneSimulationAsync(new SimulationTuneRequestDto(spec, 3, "Calmar"), ct)).Rows);

        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.RunSimulationAsync(new(null!, true), ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.TuneSimulationAsync(new(null!, 3, null!), ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.RunSimulationAsync(new(spec with { Capital = 0 }, true), ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.RunSimulationAsync(new(spec with { Revisions = [] }, true), ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.RunSimulationAsync(new(spec with { Mode = "Robot" }, true), ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.RunSimulationAsync(
            new(spec with { Revisions = [new(spec.Start, new SimulationSettingsDto { Weights = new() { ["Astrology"] = 1 } })] }, true), ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.TuneSimulationAsync(new(spec, 3, "Luck"), ct)).StatusCode);
    }
}

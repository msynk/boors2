namespace Boors.Market.Services;

/// <summary>Options of the API layer (section "MarketApi").</summary>
public sealed class MarketApiOptions
{
    public const string SectionName = "MarketApi";

    /// <summary>Never call a source while serving a read (shareholder refresh). The desktop's offline mode.</summary>
    public bool Offline { get; set; }

    /// <summary>Longest a read waits for an on-demand refresh (shareholders) before serving what is stored.</summary>
    public TimeSpan LiveRefreshTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

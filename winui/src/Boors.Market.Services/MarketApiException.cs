namespace Boors.Market.Services;

/// <summary>A request the API refuses (bad input, unknown instrument...). Boors.Node turns it into a problem-details response.</summary>
public sealed class MarketApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static MarketApiException BadRequest(string message) => new(400, message);

    public static MarketApiException NotFound(string message) => new(404, message);

    public static MarketApiException Unavailable(string message) => new(503, message);
}

namespace Boors.Market.Tests;

internal static class Golden
{
    public const long FooladInsCode = 46348559193224090;

    public static byte[] Read(string relativePath) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", relativePath));
}

namespace Boors.Desktop.Infrastructure;

/// <summary>Small x:Bind helpers.</summary>
public static class Bools
{
    public static bool HasText(string? text) => !string.IsNullOrWhiteSpace(text);

    public static bool Not(bool value) => !value;

    public static bool Any<T>(IReadOnlyCollection<T>? items) => items is { Count: > 0 };

    public static bool AnyText(IReadOnlyList<string>? items) => items is { Count: > 0 };
}

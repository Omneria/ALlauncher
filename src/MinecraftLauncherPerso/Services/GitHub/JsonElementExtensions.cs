using System.Text.Json;

namespace MinecraftLauncherPerso.Services.GitHub;

/// <summary>Lecture tolérante des champs d'une release GitHub : un champ absent ou d'un autre
/// type donne null/false au lieu d'une exception.</summary>
internal static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool GetBoolOrDefault(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        && value.GetBoolean();
}

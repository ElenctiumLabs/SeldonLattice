using System.Text.Json;
using SeldonLattice.Domain.Models.Settings;

namespace SeldonLattice.Domain.Validation;

public sealed record SettingsValidationError(string Field, string Message);

/// <summary>
/// Hard validation for a persisted <see cref="FrameworkSettings"/> row.
///
/// Settings are either complete or the application must say so loudly. There is no
/// fallback to appsettings at runtime: appsettings and the bound options are used only by
/// the first-run seeder, and the seeder validates what it is about to write.
/// </summary>
public static class FrameworkSettingsValidator
{
    private static readonly string[] ValidLayoutModes = ["helical", "dialectical"];
    private static readonly string[] ValidFontSizes = ["small", "medium", "large"];
    private static readonly string[] ValidExportFormats = ["markdown", "pdf", "json"];

    public static IReadOnlyList<SettingsValidationError> Validate(FrameworkSettings settings)
    {
        var errors = new List<SettingsValidationError>();

        RequiredText(settings.Provider, nameof(settings.Provider), errors);
        RequiredText(settings.Model, nameof(settings.Model), errors);
        RequiredText(settings.ApiEndpoint, nameof(settings.ApiEndpoint), errors);

        Positive(settings.RateLimit, nameof(settings.RateLimit), errors);
        Positive(settings.MaxDepth, nameof(settings.MaxDepth), errors);
        Positive(settings.SessionMaxDepth, nameof(settings.SessionMaxDepth), errors);
        Positive(settings.AutoArchiveDays, nameof(settings.AutoArchiveDays), errors);

        if (settings.QualityThreshold is <= 0 or > 1)
            errors.Add(new(nameof(settings.QualityThreshold),
                "QualityThreshold must be greater than 0 and at most 1."));

        OneOf(settings.LayoutMode, nameof(settings.LayoutMode), ValidLayoutModes, errors);
        OneOf(settings.FontSize, nameof(settings.FontSize), ValidFontSizes, errors);
        OneOf(settings.ExportFormat, nameof(settings.ExportFormat), ValidExportFormats, errors);

        JsonObject(settings.AgentsJson, nameof(settings.AgentsJson), requireMembers: true, errors);
        JsonObject(settings.QualityGatesJson, nameof(settings.QualityGatesJson), requireMembers: true, errors);
        JsonObject(settings.DialecticsJson, nameof(settings.DialecticsJson), requireMembers: true, errors);

        RequirePositiveProperties(
            settings.QualityGatesJson,
            nameof(settings.QualityGatesJson),
            ["MaxTurns", "SimilarityThreshold", "QualityScoreExitThreshold"],
            errors);

        RequirePositiveProperties(
            settings.DialecticsJson,
            nameof(settings.DialecticsJson),
            ["MaxTurns", "StabilityWindow", "StabilityThreshold", "MaxAscentLevels",
             "ModeratorThemeMaxLength", "ModeratorConnectiveMaxLength"],
            errors);

        return errors;
    }

    public static string Describe(IEnumerable<SettingsValidationError> errors) =>
        string.Join("; ", errors.Select(error => $"{error.Field}: {error.Message}"));

    private static void RequiredText(string? value, string field, List<SettingsValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(new(field, $"{field} is required."));
    }

    private static void Positive(int value, string field, List<SettingsValidationError> errors)
    {
        if (value <= 0)
            errors.Add(new(field, $"{field} must be greater than 0."));
    }

    private static void OneOf(
        string? value,
        string field,
        IReadOnlyCollection<string> allowed,
        List<SettingsValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new(field, $"{field} must be one of: {string.Join(", ", allowed)}."));
        }
    }

    private static void RequirePositiveProperties(
        string? json,
        string field,
        IReadOnlyCollection<string> propertyNames,
        List<SettingsValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(json)) return; // the JsonObject check already reported it

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var name in propertyNames)
            {
                var found = false;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found = property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetDouble(out var value) &&
                            value > 0;
                    break;
                }

                if (!found)
                    errors.Add(new(field, $"{field}.{name} must be a number greater than 0."));
            }
        }
        catch (JsonException)
        {
            // the JsonObject check already reported the parse failure
        }
    }

    private static void JsonObject(
        string? json,
        string field,
        bool requireMembers,
        List<SettingsValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add(new(field, $"{field} is required."));
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(field, $"{field} must be a JSON object."));
                return;
            }

            if (requireMembers && !document.RootElement.EnumerateObject().Any())
                errors.Add(new(field, $"{field} must not be empty."));
        }
        catch (JsonException ex)
        {
            errors.Add(new(field, $"{field} is not valid JSON: {ex.Message}"));
        }
    }
}

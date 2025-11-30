using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace Baketa.UI.Resources;

/// <summary>
/// Validates resource files for consistency and completeness.
/// </summary>
public partial class ResourceValidator
{
    private readonly ResourceManager _resourceManager;
    private readonly CultureInfo[] _supportedCultures;

    /// <summary>
    /// Initializes a new instance of the ResourceValidator.
    /// </summary>
    public ResourceValidator()
    {
        _resourceManager = Strings.ResourceManager;
        _supportedCultures =
        [
            CultureInfo.InvariantCulture, // Default (Japanese)
            new CultureInfo("en")
        ];
    }

    /// <summary>
    /// Validates all resource files and returns validation results.
    /// </summary>
    /// <returns>Validation result containing any errors or warnings.</returns>
    public ResourceValidationResult Validate()
    {
        var result = new ResourceValidationResult();

        // Get all keys from the default (Japanese) resource
        var defaultKeys = GetResourceKeys(CultureInfo.InvariantCulture);

        foreach (var culture in _supportedCultures.Where(c => !Equals(c, CultureInfo.InvariantCulture)))
        {
            var cultureKeys = GetResourceKeys(culture);

            // Check for missing keys
            var missingKeys = defaultKeys.Except(cultureKeys).ToList();
            foreach (var key in missingKeys)
            {
                result.AddError($"Missing key '{key}' in culture '{culture.Name}'");
            }

            // Check for extra keys
            var extraKeys = cultureKeys.Except(defaultKeys).ToList();
            foreach (var key in extraKeys)
            {
                result.AddWarning($"Extra key '{key}' in culture '{culture.Name}' not in default");
            }
        }

        // Validate format string placeholders
        ValidateFormatStrings(defaultKeys, result);

        // Validate naming conventions
        ValidateNamingConventions(defaultKeys, result);

        return result;
    }

    /// <summary>
    /// Gets all resource keys for a specific culture.
    /// </summary>
    private HashSet<string> GetResourceKeys(CultureInfo culture)
    {
        var keys = new HashSet<string>();

        try
        {
            var resourceSet = _resourceManager.GetResourceSet(culture, true, false);
            if (resourceSet is null) return keys;

            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is string key)
                {
                    keys.Add(key);
                }
            }
        }
        catch (MissingManifestResourceException)
        {
            // Resource set not found for this culture
        }

        return keys;
    }

    /// <summary>
    /// Validates that format string placeholders are consistent across cultures.
    /// </summary>
    private void ValidateFormatStrings(HashSet<string> keys, ResourceValidationResult result)
    {
        foreach (var key in keys)
        {
            var defaultValue = _resourceManager.GetString(key, CultureInfo.InvariantCulture);
            if (defaultValue is null) continue;

            var defaultPlaceholders = ExtractPlaceholders(defaultValue);
            if (defaultPlaceholders.Count == 0) continue;

            foreach (var culture in _supportedCultures.Where(c => !Equals(c, CultureInfo.InvariantCulture)))
            {
                var localizedValue = _resourceManager.GetString(key, culture);
                if (localizedValue is null) continue;

                var localizedPlaceholders = ExtractPlaceholders(localizedValue);

                // Check for missing placeholders
                var missingPlaceholders = defaultPlaceholders.Except(localizedPlaceholders).ToList();
                foreach (var placeholder in missingPlaceholders)
                {
                    result.AddError($"Key '{key}' in culture '{culture.Name}' is missing placeholder '{placeholder}'");
                }

                // Check for extra placeholders
                var extraPlaceholders = localizedPlaceholders.Except(defaultPlaceholders).ToList();
                foreach (var placeholder in extraPlaceholders)
                {
                    result.AddWarning($"Key '{key}' in culture '{culture.Name}' has extra placeholder '{placeholder}'");
                }
            }
        }
    }

    /// <summary>
    /// Extracts format placeholders (e.g., {0}, {1}) from a string.
    /// </summary>
    private static HashSet<string> ExtractPlaceholders(string value)
    {
        var placeholders = new HashSet<string>();
        var matches = PlaceholderRegex().Matches(value);

        foreach (Match match in matches)
        {
            placeholders.Add(match.Value);
        }

        return placeholders;
    }

    /// <summary>
    /// Validates that resource keys follow naming conventions.
    /// </summary>
    private static void ValidateNamingConventions(HashSet<string> keys, ResourceValidationResult result)
    {
        foreach (var key in keys)
        {
            // Keys should use PascalCase with underscores for hierarchy
            if (!NamingConventionRegex().IsMatch(key))
            {
                result.AddWarning($"Key '{key}' does not follow naming convention (Category_SubCategory_Name)");
            }

            // Check for common prefixes
            if (!HasValidPrefix(key))
            {
                result.AddWarning($"Key '{key}' does not have a recognized prefix category");
            }
        }
    }

    /// <summary>
    /// Checks if a key has a valid category prefix.
    /// </summary>
    private static bool HasValidPrefix(string key)
    {
        string[] validPrefixes =
        [
            "App_",
            "Common_",
            "MainOverlay_",
            "Home_",
            "Settings_",
            "Auth_",
            "Premium_",
            "Error_",
            "Dialog_"
        ];

        return validPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks if a specific key exists in all supported cultures.
    /// </summary>
    /// <param name="key">The resource key to check.</param>
    /// <returns>True if the key exists in all cultures.</returns>
    public bool KeyExistsInAllCultures(string key)
    {
        return _supportedCultures.All(culture =>
        {
            var value = _resourceManager.GetString(key, culture);
            return !string.IsNullOrEmpty(value);
        });
    }

    /// <summary>
    /// Gets the list of all resource keys.
    /// </summary>
    public IReadOnlyList<string> GetAllKeys()
    {
        return [.. GetResourceKeys(CultureInfo.InvariantCulture).OrderBy(k => k)];
    }

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^[A-Z][a-zA-Z0-9]*(_[A-Z][a-zA-Z0-9]*)*$")]
    private static partial Regex NamingConventionRegex();
}

/// <summary>
/// Result of resource file validation.
/// </summary>
public class ResourceValidationResult
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Gets the validation warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Gets whether the validation passed (no errors).
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Adds an error message.
    /// </summary>
    public void AddError(string message)
    {
        _errors.Add(message);
    }

    /// <summary>
    /// Adds a warning message.
    /// </summary>
    public void AddWarning(string message)
    {
        _warnings.Add(message);
    }

    /// <summary>
    /// Gets a summary of the validation result.
    /// </summary>
    public string GetSummary()
    {
        return $"Validation Result: {(IsValid ? "PASSED" : "FAILED")} - {_errors.Count} errors, {_warnings.Count} warnings";
    }
}

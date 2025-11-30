using System.ComponentModel;
using System.Globalization;
using Baketa.UI.Resources;

namespace Baketa.UI.Services;

/// <summary>
/// Singleton class that provides localized string access with dynamic update support.
/// Implements INotifyPropertyChanged to enable XAML bindings to automatically refresh
/// when the language is changed.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    private ILocalizationService? _localizationService;

    /// <summary>
    /// Gets the singleton instance of the LocalizationManager.
    /// </summary>
    public static LocalizationManager Instance => _instance.Value;

    /// <summary>
    /// Private constructor to enforce singleton pattern.
    /// </summary>
    private LocalizationManager()
    {
    }

    /// <summary>
    /// Initializes the LocalizationManager with the localization service.
    /// This should be called once during application startup.
    /// </summary>
    /// <param name="localizationService">The localization service to subscribe to.</param>
    public void Initialize(ILocalizationService localizationService)
    {
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Initialize called with LocalizationService HashCode: {localizationService?.GetHashCode()}");
        Console.WriteLine($"[LocalizationManager] Initialize called with LocalizationService HashCode: {localizationService?.GetHashCode()}");

        if (_localizationService != null)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Already initialized, unsubscribing from old service HashCode: {_localizationService.GetHashCode()}");
            // Unsubscribe from the old service if already initialized
            _localizationService.LanguageChanged -= OnLanguageChanged;
        }

        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLanguageChanged;
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Subscribed to LanguageChanged event");
    }

    /// <summary>
    /// Gets the localized string for the specified key.
    /// This indexer enables binding paths like "[KeyName]" in XAML.
    /// </summary>
    /// <param name="key">The resource key to look up.</param>
    /// <returns>The localized string, or "[key]" if not found.</returns>
    public string? this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
            {
                return "[Missing Key]";
            }

            try
            {
                var value = Strings.ResourceManager.GetString(key, Strings.Culture);
                return value ?? $"[{key}]";
            }
            catch
            {
                return $"[{key}]";
            }
        }
    }

    /// <summary>
    /// Forces a refresh of all localized bindings.
    /// Call this when the language has changed externally.
    /// </summary>
    public void RefreshAllBindings()
    {
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] RefreshAllBindings called");
        Console.WriteLine($"[LocalizationManager] RefreshAllBindings called - Strings.Culture={Strings.Culture?.Name}");

        var handlerCount = PropertyChanged?.GetInvocationList().Length ?? 0;
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] PropertyChanged handlers count: {handlerCount}");

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        // 🔥 [GEMINI_FIX] インデクサの変更通知は "Item" を使用（"Item[]"ではない）
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        // 互換性のため "Item[]" も発火
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] RefreshAllBindings completed");
    }

    /// <summary>
    /// Gets a formatted localized string.
    /// </summary>
    /// <param name="key">The resource key to look up.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    public string GetString(string key, params object[] args)
    {
        var value = this[key];
        if (value == null || !value.StartsWith('[') && args.Length > 0)
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, value ?? string.Empty, args);
            }
            catch
            {
                return value ?? $"[{key}]";
            }
        }
        return value;
    }

    /// <summary>
    /// Event handler for language changes.
    /// Fires PropertyChanged with null to refresh all bindings.
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] ========== OnLanguageChanged START ==========");
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Sender HashCode: {sender?.GetHashCode()}, Subscribed to HashCode: {_localizationService?.GetHashCode()}");
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] OldCulture: {e.OldCulture?.Name ?? "(null)"} → NewCulture: {e.NewCulture?.Name ?? "(null)"}");
        Console.WriteLine($"[LocalizationManager] ========== OnLanguageChanged START ==========");
        Console.WriteLine($"[LocalizationManager] OldCulture: {e.OldCulture?.Name ?? "(null)"} → NewCulture: {e.NewCulture?.Name ?? "(null)"}");
        Console.WriteLine($"[LocalizationManager] Strings.Culture = {Strings.Culture?.Name ?? "(null)"}");

        var handlerCount = PropertyChanged?.GetInvocationList().Length ?? 0;
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] PropertyChanged handlers count: {handlerCount}");
        Console.WriteLine($"[LocalizationManager] PropertyChanged handlers count: {handlerCount}");

        // Fire PropertyChanged with null or empty string to refresh all indexed bindings
        // This causes all XAML bindings to re-evaluate their values
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Firing PropertyChanged(null)...");
        Console.WriteLine($"[LocalizationManager] Firing PropertyChanged(null)...");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Firing PropertyChanged(Item[])...");
        Console.WriteLine($"[LocalizationManager] Firing PropertyChanged(Item[])...");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

        // サンプルキーで実際の値を確認
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Sample key 'MainOverlay_Target' = {this["MainOverlay_Target"]}");
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] Sample key 'MainOverlay_Settings' = {this["MainOverlay_Settings"]}");
        System.Diagnostics.Debug.WriteLine($"[LocalizationManager] ========== OnLanguageChanged END ==========");
        Console.WriteLine($"[LocalizationManager] Sample key 'MainOverlay_Target' = {this["MainOverlay_Target"]}");
        Console.WriteLine($"[LocalizationManager] Sample key 'MainOverlay_Settings' = {this["MainOverlay_Settings"]}");
        Console.WriteLine($"[LocalizationManager] ========== OnLanguageChanged END ==========");
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}

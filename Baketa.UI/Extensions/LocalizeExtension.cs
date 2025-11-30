using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Baketa.UI.Services;

namespace Baketa.UI.Extensions;

/// <summary>
/// XAML markup extension for localized string binding with dynamic update support.
/// Usage: Text="{local:Localize MainOverlay_LiveTranslation}"
///
/// This extension creates a compiled binding to LocalizationManager that properly
/// updates when the language is changed at runtime.
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    /// <summary>
    /// The resource key to look up.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public LocalizeExtension()
    {
    }

    /// <summary>
    /// Constructor with key parameter.
    /// </summary>
    /// <param name="key">The resource key to look up.</param>
    public LocalizeExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Returns a Binding that provides the localized string for the specified key.
    /// Uses CompiledBindingExtension for better Avalonia compatibility.
    /// </summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return "[Missing Key]";
        }

        // Create a compiled binding path to the indexer
        // This should work better with Avalonia's binding system
        var binding = new Binding
        {
            Source = LocalizationManager.Instance,
            Path = $"[{Key}]",
            Mode = BindingMode.OneWay,
            // Force binding to use property change notifications
            Priority = BindingPriority.LocalValue
        };

        return binding;
    }
}

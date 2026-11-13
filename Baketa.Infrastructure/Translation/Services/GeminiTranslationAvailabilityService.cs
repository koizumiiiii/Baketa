using Baketa.Core.Abstractions.Translation;

namespace Baketa.Infrastructure.Translation.Services;

public sealed class GeminiTranslationAvailabilityService : ICloudTranslationAvailabilityService, IDisposable
{
    private readonly IGeminiSettingsService _settings;
    private readonly Baketa.Core.Translation.Abstractions.IEngineStatusManager? _engines;
    private readonly ITranslationMethodService? _methods;
    private bool _lastEnabled;
    public GeminiTranslationAvailabilityService(IGeminiSettingsService settings, Baketa.Core.Translation.Abstractions.IEngineStatusManager? engines = null, ITranslationMethodService? methods = null)
    {
        _settings = settings;
        _methods = methods;
        if (methods != null) methods.Changed += OnChanged;
        _engines = engines;
        _lastEnabled = IsEffectivelyEnabled;
        settings.Changed += OnChanged;
    }
    public bool IsEntitled => _settings.HasApiKey;
    public bool IsPreferred => _methods != null ? _methods.Current == Baketa.Core.Models.TranslationMethod.Gemini : _settings.PreferGemini;
    public bool IsEffectivelyEnabled => IsEntitled && IsPreferred;
    public event EventHandler<CloudTranslationAvailabilityChangedEventArgs>? AvailabilityChanged;
    private void OnChanged(object? sender, EventArgs args)
    {
        _engines?.MarkEngineAvailable("primary");
        var previous = _lastEnabled;
        _lastEnabled = IsEffectivelyEnabled;
        AvailabilityChanged?.Invoke(this, new()
        {
            WasEnabled = previous, IsEnabled = _lastEnabled, IsEntitled = IsEntitled,
            IsPreferred = IsPreferred, Reason = CloudTranslationChangeReason.UserPreferenceChanged
        });
    }
    public Task RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        OnChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
    public Task SetPreferredAsync(bool preferred, CancellationToken cancellationToken = default) =>
        _methods != null ? _methods.SelectAsync(preferred ? Baketa.Core.Models.TranslationMethod.Gemini : Baketa.Core.Models.TranslationMethod.Local, cancellationToken) :
        _settings.SetOptionsAsync(_settings.Model, preferred, cancellationToken);
    public void Dispose()
    {
        _settings.Changed -= OnChanged;
        if (_methods != null) _methods.Changed -= OnChanged;
    }
}

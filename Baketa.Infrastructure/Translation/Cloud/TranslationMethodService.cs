using System.IO;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>Single selection authority. Legacy Gemini preference is read only when no selection exists.</summary>
public sealed class TranslationMethodService : ITranslationMethodService, IDisposable
{
    private readonly IGeminiSettingsService _gemini;
    private readonly IGoogleTranslationSettingsService _google;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TranslationMethod _selected;

    public TranslationMethodService(IGeminiSettingsService gemini, IGoogleTranslationSettingsService google, string? baseDirectory = null)
    {
        _gemini = gemini;
        _google = google;
        _path = Path.Combine(baseDirectory ?? BaketaSettingsPaths.BaseDirectory, "byok", "translation-method.json");
        _selected = gemini.PreferGemini && gemini.HasApiKey ? TranslationMethod.Gemini : TranslationMethod.Local;
        try
        {
            if (File.Exists(_path)) _selected = JsonSerializer.Deserialize<TranslationMethod>(File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _selected = TranslationMethod.Local;
        }
        if (!CanSelect(_selected)) _selected = TranslationMethod.Local;
        gemini.Changed += OnCredentialsChanged;
        google.Changed += OnCredentialsChanged;
    }

    public TranslationMethod Current => CanSelect(_selected) ? _selected : TranslationMethod.Local;
    public bool CanSelect(TranslationMethod method) => method switch
    {
        TranslationMethod.Local => true,
        TranslationMethod.Gemini => _gemini.HasApiKey,
        TranslationMethod.Google => _google.HasApiKey,
        _ => false
    };
    public event EventHandler? Changed;

    public Task SelectAsync(TranslationMethod method, CancellationToken cancellationToken = default)
        => SelectCoreAsync(null, method, cancellationToken);

    public Task SelectIfCurrentAsync(TranslationMethod expected, TranslationMethod method, CancellationToken cancellationToken = default)
        => SelectCoreAsync(expected, method, cancellationToken);

    private async Task SelectCoreAsync(TranslationMethod? expected, TranslationMethod method, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (expected.HasValue && _selected != expected.Value) return;
            if (!CanSelect(method)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(method), cancellationToken).ConfigureAwait(false);
            File.Move(_path + ".tmp", _path, true);
            _selected = method;
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCredentialsChanged(object? sender, EventArgs args)
    {
        // Current immediately reflects eligibility; retain the stored selection so
        // conditional persistence can compare it under the same gate as selection.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _gemini.Changed -= OnCredentialsChanged;
        _google.Changed -= OnCredentialsChanged;
        _gate.Dispose();
    }
}

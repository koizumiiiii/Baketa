using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Baketa.UI.ViewModels.Settings;

public sealed class GeminiSettingsViewModel : ViewModelBase
{
    private readonly IGeminiSettingsService _settings;
    private readonly IGeminiConnectionService _connection;
    private readonly ITranslationMethodService? _methods;
    private readonly Action<string> _openUrl;
    // Fody weaving is disabled in this project; explicitly use ReactiveUI notifications.
    private string _apiKey = "";
    [Reactive]
    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }
    private bool _preferGemini = false;
    [Reactive]
    public bool PreferGemini
    {
        get => _preferGemini;
        private set => this.RaiseAndSetIfChanged(ref _preferGemini, value);
    }
    private bool _hasApiKey = false;
    [Reactive]
    public bool HasApiKey
    {
        get => _hasApiKey;
        private set => this.RaiseAndSetIfChanged(ref _hasApiKey, value);
    }
    private string _status = "";
    [Reactive]
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }
    private bool _isStatusError = false;
    [Reactive]
    public bool IsStatusError
    {
        get => _isStatusError;
        private set => this.RaiseAndSetIfChanged(ref _isStatusError, value);
    }
    private bool _isStatusSuccess = false;
    [Reactive]
    public bool IsStatusSuccess
    {
        get => _isStatusSuccess;
        private set => this.RaiseAndSetIfChanged(ref _isStatusSuccess, value);
    }
    private bool _isBusy = false;
    [Reactive]
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }
    public ReactiveCommand<Unit, Unit> SaveKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectGeminiCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectLocalCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenStudioCommand { get; }

    public GeminiSettingsViewModel(IGeminiSettingsService settings, IGeminiConnectionService connection, IEventAggregator events, ITranslationMethodService? methods = null, Action<string>? openUrl = null) : base(events)
    {
        _settings = settings;
        _methods = methods;
        _openUrl = openUrl ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        _connection = connection;
        HasApiKey = settings.HasApiKey;
        PreferGemini = HasApiKey && settings.PreferGemini;
        SetStatus(settings.HasApiKey ? "Gemini_KeySaved" : "Gemini_NoKey");
        var ready = this.WhenAnyValue(x => x.IsBusy, busy => !busy);
        var canSave = this.WhenAnyValue(x => x.IsBusy, x => x.ApiKey,
            (busy, key) => !busy && !string.IsNullOrWhiteSpace(key));
        SaveKeyCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            var key = ApiKey.Trim();
            if (key.Length == 0) return;
            try
            {
                SetStatus("Gemini_Checking");
                var result = await _connection.TestConnectionAsync(key);
                if (result != GeminiConnectionResult.Success)
                {
                    SetStatus(result switch
                    {
                        GeminiConnectionResult.InvalidKey => "Gemini_TestInvalidKey",
                        GeminiConnectionResult.RateLimited => "Gemini_TestRateLimited",
                        GeminiConnectionResult.NetworkError => "Gemini_TestNetworkError",
                        GeminiConnectionResult.Timeout => "Gemini_TestTimeout",
                        _ => "Gemini_TestFailed"
                    }, error: true);
                    return;
                }
                await _settings.SaveApiKeyAsync(key);
                HasApiKey = _settings.HasApiKey;
                try
                {
                    if (_methods != null) await _methods.SelectAsync(TranslationMethod.Gemini);
                }
                catch (Exception)
                {
                    SetStatus("BYOK_KeySavedMethodFailed", error: true);
                    return;
                }
                PreferGemini = HasApiKey && (_methods?.Current == TranslationMethod.Gemini || (_methods == null && _settings.PreferGemini));
                SetStatus("Gemini_TestSuccess", success: true);
            }
            finally { ApiKey = ""; }
        }), canSave).DisposeWith(Disposables);
        DeleteKeyCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            await _settings.DeleteApiKeyAsync();
            ApiKey = "";
            HasApiKey = _settings.HasApiKey;
            if (_methods != null) await _methods.SelectIfCurrentAsync(TranslationMethod.Gemini, TranslationMethod.Local);
            PreferGemini = false;
            SetStatus("Gemini_NoKey");
        }), this.WhenAnyValue(x => x.IsBusy, x => x.HasApiKey, (busy, hasKey) => !busy && hasKey))
            .DisposeWith(Disposables);
        SelectGeminiCommand = ReactiveCommand.CreateFromTask(() => SelectMethodAsync(true),
            this.WhenAnyValue(x => x.IsBusy, x => x.HasApiKey, (busy, hasKey) => !busy && hasKey))
            .DisposeWith(Disposables);
        SelectLocalCommand = ReactiveCommand.CreateFromTask(() => SelectMethodAsync(false), ready).DisposeWith(Disposables);
        OpenStudioCommand = ReactiveCommand.Create(() =>
        {
            // Navigation must not release a state-changing operation's busy flag.
            try { _openUrl("https://aistudio.google.com/apikey"); }
            catch (Exception)
            {
                if (!IsBusy) SetStatus("Gemini_SettingsFailed", error: true);
            }
        }).DisposeWith(Disposables);
        Disposable.Create(() => ApiKey = "").DisposeWith(Disposables);
        this.WhenActivated(disposables => Disposable.Create(() => ApiKey = "").DisposeWith(disposables));
    }

    private Task SelectMethodAsync(bool preferGemini) => RunAsync(async () =>
    {
        if (preferGemini && !HasApiKey) return;
        if (_methods != null) await _methods.SelectAsync(preferGemini ? TranslationMethod.Gemini : TranslationMethod.Local);
        else await _settings.SetOptionsAsync(_settings.Model, preferGemini);
        PreferGemini = preferGemini;
        SetStatus(HasApiKey ? "Gemini_KeySaved" : "Gemini_NoKey");
    });

    private void SetStatus(string key, bool error = false, bool success = false)
    {
        Status = Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
        IsStatusError = error;
        IsStatusSuccess = success;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        SetStatus("Gemini_Processing");
        try { await action(); }
        catch (Exception) { SetStatus("Gemini_SettingsFailed", error: true); }
        finally
        {
            HasApiKey = _settings.HasApiKey;
            PreferGemini = HasApiKey && (_methods != null ? _methods.Current == TranslationMethod.Gemini : _settings.PreferGemini);
            IsBusy = false;
            // Reapply the persisted selection if a radio button operation failed.
            this.RaisePropertyChanged(nameof(PreferGemini));
        }
    }
}

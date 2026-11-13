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

public sealed class GoogleTranslationSettingsViewModel : ViewModelBase
{
    private readonly IGoogleTranslationSettingsService _settings;
    private readonly IGoogleTranslationConnectionService _connection;
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
    public ReactiveCommand<Unit, Unit> OpenStudioCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenApiCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPricingCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenQuotaCommand { get; }

    public GoogleTranslationSettingsViewModel(IGoogleTranslationSettingsService settings, IGoogleTranslationConnectionService connection, IEventAggregator events, ITranslationMethodService? methods = null, Action<string>? openUrl = null) : base(events)
    {
        _settings = settings;
        _methods = methods;
        _openUrl = openUrl ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        _connection = connection;
        HasApiKey = settings.HasApiKey;
        SetStatus(settings.HasApiKey ? "Google_KeySaved" : "Google_NoKey");
        var canSave = this.WhenAnyValue(x => x.IsBusy, x => x.ApiKey,
            (busy, key) => !busy && !string.IsNullOrWhiteSpace(key));
        SaveKeyCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            var key = ApiKey.Trim();
            if (key.Length == 0) return;
            try
            {
                SetStatus("Google_Checking");
                var result = await _connection.TestConnectionAsync(key);
                if (result != GoogleTranslationResult.Success)
                {
                    SetStatus(result switch
                    {
                        GoogleTranslationResult.InvalidKey => "Google_TestInvalidKey",
                        GoogleTranslationResult.ServiceDisabled => "Google_ServiceDisabled",
                        GoogleTranslationResult.BillingDisabled => "Google_BillingDisabled",
                        GoogleTranslationResult.Forbidden => "Google_Forbidden",
                        GoogleTranslationResult.RateLimited => "Google_TestRateLimited",
                        GoogleTranslationResult.NetworkError => "Google_TestNetworkError",
                        GoogleTranslationResult.Timeout => "Google_TestTimeout",
                        _ => "Google_TestFailed"
                    }, error: true);
                    return;
                }
                await _settings.SaveApiKeyAsync(key);
                HasApiKey = _settings.HasApiKey;
                try
                {
                    if (_methods != null) await _methods.SelectAsync(TranslationMethod.Google);
                }
                catch (Exception)
                {
                    SetStatus("BYOK_KeySavedMethodFailed", error: true);
                    return;
                }
                SetStatus("Google_TestSuccess", success: true);
            }
            finally { ApiKey = ""; }
        }), canSave).DisposeWith(Disposables);
        DeleteKeyCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            await _settings.DeleteApiKeyAsync();
            ApiKey = "";
            HasApiKey = _settings.HasApiKey;
            if (_methods != null) await _methods.SelectIfCurrentAsync(TranslationMethod.Google, TranslationMethod.Local);
            SetStatus("Google_NoKey");
        }), this.WhenAnyValue(x => x.IsBusy, x => x.HasApiKey, (busy, hasKey) => !busy && hasKey))
            .DisposeWith(Disposables);
        OpenStudioCommand = OpenLink("https://console.cloud.google.com/apis/credentials");
        OpenApiCommand = OpenLink("https://console.cloud.google.com/apis/library/translate.googleapis.com");
        OpenPricingCommand = OpenLink("https://cloud.google.com/translate/pricing");
        OpenQuotaCommand = OpenLink("https://console.cloud.google.com/apis/api/translate.googleapis.com/quotas");
        Disposable.Create(() => ApiKey = "").DisposeWith(Disposables);
        this.WhenActivated(disposables => Disposable.Create(() => ApiKey = "").DisposeWith(disposables));
    }

    // Navigation must not release a state-changing operation's busy flag.
    private ReactiveCommand<Unit, Unit> OpenLink(string url) => ReactiveCommand.Create(() =>
    {
        try { _openUrl(url); }
        catch (Exception)
        {
            if (!IsBusy) SetStatus("Google_SettingsFailed", error: true);
        }
    }).DisposeWith(Disposables);

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
        SetStatus("Google_Processing");
        try { await action(); }
        catch (Exception) { SetStatus("Google_SettingsFailed", error: true); }
        finally
        {
            HasApiKey = _settings.HasApiKey;
            IsBusy = false;
        }
    }
}

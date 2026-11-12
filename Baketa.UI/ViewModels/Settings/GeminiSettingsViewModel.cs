using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Baketa.UI.ViewModels.Settings;

public sealed class GeminiSettingsViewModel : ViewModelBase
{
    private readonly IGeminiSettingsService _settings;
    private readonly IGeminiConnectionService _connection;
    [Reactive] public string ApiKey { get; set; } = "";
    [Reactive] public string Model { get; set; }
    [Reactive] public bool PreferGemini { get; set; }
    [Reactive] public string Status { get; private set; } = "";
    public string[] Models { get; } = ["gemini-flash-lite-latest", "gemini-flash-latest"];
    public ReactiveCommand<Unit, Unit> SaveKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> TestCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveOptionsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenStudioCommand { get; }

    public GeminiSettingsViewModel(IGeminiSettingsService settings, IGeminiConnectionService connection, IEventAggregator events) : base(events)
    {
        _settings = settings;
        _connection = connection;
        Model = settings.Model;
        PreferGemini = settings.PreferGemini;
        SetStatus(settings.HasApiKey ? "Gemini_KeySaved" : "Gemini_NoKey");
        var ready = this.WhenAnyValue(x => x.IsLoading, busy => !busy);
        SaveKeyCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            try { await _settings.SaveApiKeyAsync(ApiKey); }
            finally { ApiKey = ""; }
            PreferGemini = true;
            SetStatus("Gemini_KeySaved");
        }), ready).DisposeWith(Disposables);
        DeleteKeyCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            await _settings.DeleteApiKeyAsync();
            ApiKey = "";
            SetStatus("Gemini_NoKey");
        }), ready).DisposeWith(Disposables);
        TestCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
            SetStatus(await _connection.TestConnectionAsync() ? "Gemini_TestSuccess" : "Gemini_TestFailed")), ready).DisposeWith(Disposables);
        SaveOptionsCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
        {
            await _settings.SetOptionsAsync(Model, PreferGemini);
            SetStatus("Gemini_OptionsSaved");
        }), ready).DisposeWith(Disposables);
        OpenStudioCommand = ReactiveCommand.CreateFromTask(() => RunAsync(() =>
        {
            Process.Start(new ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true });
            return Task.CompletedTask;
        }), ready).DisposeWith(Disposables);
        Disposable.Create(() => ApiKey = "").DisposeWith(Disposables);
        this.WhenActivated(disposables => Disposable.Create(() => ApiKey = "").DisposeWith(disposables));
    }

    private void SetStatus(string key) => Status = Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
    private async Task RunAsync(Func<Task> action)
    {
        IsLoading = true;
        try { await action(); }
        catch (Exception) { SetStatus("Gemini_SettingsFailed"); }
        finally { IsLoading = false; }
    }
}

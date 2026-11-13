using System;
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

public sealed class TranslationMethodViewModel : ViewModelBase
{
    private readonly ITranslationMethodService _methods;
    public bool IsGemini => _methods.Current == TranslationMethod.Gemini;
    public bool IsGoogle => _methods.Current == TranslationMethod.Google;
    public bool IsLocal => _methods.Current == TranslationMethod.Local;
    public bool CanGemini => _methods.CanSelect(TranslationMethod.Gemini);
    public bool CanGoogle => _methods.CanSelect(TranslationMethod.Google);
    private bool _busy;
    [Reactive] public bool IsBusy { get => _busy; private set => this.RaiseAndSetIfChanged(ref _busy, value); }
    private string _status = "";
    [Reactive] public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
    public ReactiveCommand<Unit, Unit> SelectGeminiCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectGoogleCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectLocalCommand { get; }

    public TranslationMethodViewModel(ITranslationMethodService methods, IEventAggregator events) : base(events)
    {
        _methods = methods;
        SelectGeminiCommand = ReactiveCommand.CreateFromTask(() => SelectAsync(TranslationMethod.Gemini),
            this.WhenAnyValue(x => x.CanGemini, x => x.IsBusy, (can, busy) => can && !busy)).DisposeWith(Disposables);
        SelectGoogleCommand = ReactiveCommand.CreateFromTask(() => SelectAsync(TranslationMethod.Google),
            this.WhenAnyValue(x => x.CanGoogle, x => x.IsBusy, (can, busy) => can && !busy)).DisposeWith(Disposables);
        SelectLocalCommand = ReactiveCommand.CreateFromTask(() => SelectAsync(TranslationMethod.Local),
            this.WhenAnyValue(x => x.IsBusy, busy => !busy)).DisposeWith(Disposables);
        Observable.FromEventPattern(h => methods.Changed += h, h => methods.Changed -= h)
            .ObserveOn(RxApp.MainThreadScheduler).Subscribe(_ => Refresh()).DisposeWith(Disposables);
        this.WhenActivated((CompositeDisposable disposables) => { Refresh(); });
    }

    private async Task SelectAsync(TranslationMethod method)
    {
        IsBusy = true;
        try { await _methods.SelectAsync(method); Status = ""; }
        catch (Exception) { Status = Strings.ResourceManager.GetString("Google_SettingsFailed", Strings.Culture) ?? ""; }
        finally { IsBusy = false; Refresh(); }
    }

    private void Refresh()
    {
        this.RaisePropertyChanged(nameof(IsGemini));
        this.RaisePropertyChanged(nameof(IsGoogle));
        this.RaisePropertyChanged(nameof(IsLocal));
        this.RaisePropertyChanged(nameof(CanGemini));
        this.RaisePropertyChanged(nameof(CanGoogle));
    }
}

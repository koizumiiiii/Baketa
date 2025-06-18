using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Events;
using Baketa.Application.Models;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Services;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Baketa.UI.ViewModels.Controls;

/// <summary>
/// 操作UI（自動/単発翻訳ボタン）のビューモデル
/// </summary>
internal sealed class OperationalControlViewModel : Framework.ViewModelBase
{
    private readonly ITranslationOrchestrationService _translationOrchestrationService;
    private readonly ISettingsService _settingsService;
    
    // 割り込み処理用（統合サービス内で管理されるため簡素化）
    private volatile bool _isSubscribedToTranslationEvents;

    #region Properties

    /// <summary>
    /// 自動翻訳モードが有効かどうか
    /// </summary>
    [Reactive] public bool IsAutomaticMode { get; set; }

    /// <summary>
    /// 翻訳処理中かどうか
    /// </summary>
    [Reactive] public bool IsTranslating { get; private set; }

    /// <summary>
    /// モード切り替えが可能かどうか
    /// </summary>
    [Reactive] public bool CanToggleMode { get; private set; } = true;

    /// <summary>
    /// 単発翻訳が実行可能かどうか
    /// </summary>
    [Reactive] public bool CanTriggerSingleTranslation { get; private set; } = true;

    /// <summary>
    /// 現在の翻訳モード
    /// </summary>
    public TranslationMode CurrentMode => IsAutomaticMode ? TranslationMode.Automatic : TranslationMode.Manual;

    /// <summary>
    /// 現在の状態テキスト
    /// </summary>
    [Reactive] public string CurrentStatus { get; private set; } = "準備完了";

    #endregion

    #region Commands

    /// <summary>
    /// 自動翻訳モード切り替えコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleAutomaticModeCommand { get; }

    /// <summary>
    /// 単発翻訳実行コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> TriggerSingleTranslationCommand { get; }

    #endregion

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="translationOrchestrationService">翻訳統合サービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public OperationalControlViewModel(
        ITranslationOrchestrationService translationOrchestrationService,
        ISettingsService settingsService,
        Baketa.UI.Framework.Events.IEventAggregator eventAggregator,
        ILogger<OperationalControlViewModel>? logger = null)
        : base(eventAggregator, logger)
    {
        _translationOrchestrationService = translationOrchestrationService ?? throw new ArgumentNullException(nameof(translationOrchestrationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        // コマンドの作成（実行可否条件付き）
        var canToggleMode = this.WhenAnyValue(x => x.CanToggleMode);
        ToggleAutomaticModeCommand = ReactiveCommand.CreateFromTask(
            ExecuteToggleAutomaticModeAsync, 
            canToggleMode);

        var canTriggerSingle = this.WhenAnyValue(x => x.CanTriggerSingleTranslation);
        TriggerSingleTranslationCommand = ReactiveCommand.CreateFromTask(
            ExecuteTriggerSingleTranslationAsync, 
            canTriggerSingle);

        // プロパティ変更の監視
        SetupPropertyObservations();
        
        // 翻訳統合サービスのイベント購読
        SubscribeToTranslationEvents();
    }

    /// <summary>
    /// プロパティ変更の監視設定
    /// </summary>
    private void SetupPropertyObservations()
    {
        // 自動翻訳モード変更時の処理
        var subscription1 = this.WhenAnyValue(x => x.IsAutomaticMode)
            .Skip(1) // 初期値をスキップ
            .Subscribe(async isAutomatic => await OnAutomaticModeChangedAsync(isAutomatic).ConfigureAwait(true));
        _disposables.Add(subscription1);

        // 翻訳中状態の変更時にコマンド実行可否を更新
        var subscription2 = this.WhenAnyValue(x => x.IsTranslating)
            .Subscribe(isTranslating =>
            {
                CanToggleMode = !isTranslating;
                CanTriggerSingleTranslation = !isTranslating;
                UpdateCurrentStatus();
            });
        _disposables.Add(subscription2);
    }

    /// <summary>
    /// 翻訳統合サービスのイベント購読
    /// </summary>
    private void SubscribeToTranslationEvents()
    {
        if (_isSubscribedToTranslationEvents) return;

        // 翻訳結果の監視
        var translationResultsSubscription = _translationOrchestrationService.TranslationResults
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(result =>
            {
                _logger?.LogDebug("翻訳結果を受信: ID={Id}, モード={Mode}, テキスト長={Length}",
                    result.Id, result.Mode, result.TranslatedText.Length);
                    
                // UI更新処理（必要に応じて）
                UpdateCurrentStatus();
            });
        _disposables.Add(translationResultsSubscription);

        // 翻訳状態変更の監視
        var statusChangesSubscription = _translationOrchestrationService.StatusChanges
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(status =>
            {
                // 翻訳サービスの状態をViewModelの状態に反映
                IsTranslating = _translationOrchestrationService.IsAnyTranslationActive;
                UpdateCurrentStatus();
            });
        _disposables.Add(statusChangesSubscription);

        // 進行状況の監視
        var progressSubscription = _translationOrchestrationService.ProgressUpdates
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(progress =>
            {
                // 必要に応じて詳細な進行状況をUIに反映
                if (!string.IsNullOrEmpty(progress.Message))
                {
                    CurrentStatus = progress.Message;
                }
            });
        _disposables.Add(progressSubscription);

        _isSubscribedToTranslationEvents = true;
        _logger?.LogDebug("翻訳統合サービスのイベント購読を開始しました");
    }

    /// <summary>
    /// 自動翻訳モードの切り替え実行
    /// </summary>
    private async Task ExecuteToggleAutomaticModeAsync()
    {
        try
        {
            var previousMode = CurrentMode;
            IsAutomaticMode = !IsAutomaticMode;
            var newMode = CurrentMode;

            // モード変更イベントを発行
            await PublishEventAsync(new TranslationModeChangedEvent(newMode, previousMode)).ConfigureAwait(true);

            _logger?.LogInformation(
                "翻訳モードが変更されました: {PreviousMode} → {NewMode}",
                previousMode, newMode);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "翻訳モード切り替え中に無効な操作が発生しました");
            ErrorMessage = $"モード切り替えエラー: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "翻訳モード切り替え中に引数エラーが発生しました");
            ErrorMessage = $"モード切り替えエラー: {ex.Message}";
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "翻訳モード切り替えがタイムアウトしました");
            ErrorMessage = $"モード切り替えエラー: {ex.Message}";
        }
#pragma warning disable CA1031 // ViewModel層でのユーザー体験保護のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "翻訳モード切り替え中に予期しないエラーが発生しました");
            ErrorMessage = $"モード切り替えエラー: {ex.Message}";
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 単発翻訳の実行
    /// </summary>
    private async Task ExecuteTriggerSingleTranslationAsync()
    {
        try
        {
            _logger?.LogInformation("単発翻訳を実行します");

            // 翻訳統合サービス経由で単発翻訳を実行
            await _translationOrchestrationService.TriggerSingleTranslationAsync().ConfigureAwait(true);

            _logger?.LogInformation("単発翻訳コマンドを送信しました");
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogInformation(ex, "単発翻訳がキャンセルされました");
            ErrorMessage = "翻訳がキャンセルされました";
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "単発翻訳実行中に無効な操作が発生しました");
            ErrorMessage = $"翻訳実行エラー: {ex.Message}";
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "単発翻訳がタイムアウトしました");
            ErrorMessage = $"翻訳実行エラー: {ex.Message}";
        }
#pragma warning disable CA1031 // ViewModel層でのユーザー体験保護のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "単発翻訳実行中に予期しないエラーが発生しました");
            ErrorMessage = $"翻訳実行エラー: {ex.Message}";
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 自動翻訳モード変更時の処理
    /// </summary>
    private async Task OnAutomaticModeChangedAsync(bool isAutomatic)
    {
        try
        {
            if (isAutomatic)
            {
                _logger?.LogInformation("自動翻訳モードを開始します");
                await _translationOrchestrationService.StartAutomaticTranslationAsync().ConfigureAwait(true);
            }
            else
            {
                _logger?.LogInformation("自動翻訳モードを停止します");
                await _translationOrchestrationService.StopAutomaticTranslationAsync().ConfigureAwait(true);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "自動翻訳モード変更処理中に無効な操作が発生しました");
            ErrorMessage = $"モード変更エラー: {ex.Message}";
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "自動翻訳モード変更がタイムアウトしました");
            ErrorMessage = $"モード変更エラー: {ex.Message}";
        }
#pragma warning disable CA1031 // ViewModel層でのユーザー体験保護のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "自動翻訳モード変更処理中に予期しないエラーが発生しました");
            ErrorMessage = $"モード変更エラー: {ex.Message}";
        }
#pragma warning restore CA1031
    }



    /// <summary>
    /// 現在の状態表示を更新
    /// </summary>
    private void UpdateCurrentStatus()
    {
        CurrentStatus = _translationOrchestrationService.IsSingleTranslationActive ? "単発翻訳実行中..." :
                        _translationOrchestrationService.IsAutomaticTranslationActive ? "自動翻訳中" :
                        IsAutomaticMode ? "自動翻訳待機中" : "準備完了";
    }



    /// <summary>
    /// アクティベーション時の処理
    /// </summary>
    protected override void HandleActivation()
    {
        // 初期状態の設定
        UpdateCurrentStatus();
        
        // 翻訳統合サービスを開始
        _ = Task.Run(async () =>
        {
            try
            {
                await _translationOrchestrationService.StartAsync().ConfigureAwait(false);
                _logger?.LogDebug("TranslationOrchestrationServiceが開始されました");
            }
#pragma warning disable CA1031 // UIアクティベーション時のアプリケーション安定性のため一般例外をキャッチ
            catch (Exception ex)
            {
                _logger?.LogError(ex, "TranslationOrchestrationServiceの開始中にエラーが発生しました");
            }
#pragma warning restore CA1031
        });
        
        _logger?.LogDebug("OperationalControlViewModelがアクティベートされました");
    }

    /// <summary>
    /// 非アクティベーション時の処理
    /// </summary>
    protected override void HandleDeactivation()
    {
        // 翻訳統合サービスを停止
        _ = Task.Run(async () =>
        {
            try
            {
                await _translationOrchestrationService.StopAsync().ConfigureAwait(false);
                _logger?.LogDebug("TranslationOrchestrationServiceが停止されました");
            }
#pragma warning disable CA1031 // UI非アクティベーション時のアプリケーション安定性のため一般例外をキャッチ
            catch (Exception ex)
            {
                _logger?.LogError(ex, "非アクティベーション時のTranslationOrchestrationService停止でエラーが発生しました");
            }
#pragma warning restore CA1031
        });

        _logger?.LogDebug("OperationalControlViewModelが非アクティベートされました");
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 翻訳統合サービスのリソース解放
            if (_translationOrchestrationService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}

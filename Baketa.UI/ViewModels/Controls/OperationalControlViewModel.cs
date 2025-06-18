using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Events;
using Baketa.Application.Models;
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
    private readonly ICaptureService _captureService;
    private readonly ISettingsService _settingsService;
    
    // 割り込み処理用
    private CancellationTokenSource? _automaticModeCts;
    private Task? _automaticTranslationTask;
    private volatile bool _isSingleTranslationActive;

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
    /// <param name="captureService">キャプチャサービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public OperationalControlViewModel(
        ICaptureService captureService,
        ISettingsService settingsService,
        IEventAggregator eventAggregator,
        ILogger<OperationalControlViewModel>? logger = null)
        : base(eventAggregator, logger)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
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
                CanTriggerSingleTranslation = !isTranslating || !_isSingleTranslationActive;
                UpdateCurrentStatus();
            });
        _disposables.Add(subscription2);
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
        if (_isSingleTranslationActive)
        {
            _logger?.LogWarning("単発翻訳が既に実行中です");
            return;
        }

        _isSingleTranslationActive = true;
        IsTranslating = true;

        try
        {
            CurrentStatus = "単発翻訳実行中...";

            // 翻訳実行イベントを発行
            await PublishEventAsync(new TranslationTriggeredEvent(TranslationMode.Manual)).ConfigureAwait(true);

            // TODO: 実際のキャプチャサービスが実装されたら置き換える
            // 現在のICaptureServiceにはCaptureOnceAsyncがないため、一時的に代替実装
            await SimulateCaptureOnceAsync().ConfigureAwait(true);

            _logger?.LogInformation("単発翻訳が完了しました");
            CurrentStatus = "翻訳完了";

            // 一定時間後にステータスをリセット
            using var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.Delay(2000, delayCts.Token).ConfigureAwait(true);
            if (!IsTranslating) // まだ他の翻訳が実行中でなければリセット
            {
                CurrentStatus = IsAutomaticMode ? "自動翻訳中" : "準備完了";
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogInformation(ex, "単発翻訳がキャンセルされました");
            CurrentStatus = "キャンセル";
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "単発翻訳実行中に無効な操作が発生しました");
            ErrorMessage = $"翻訳実行エラー: {ex.Message}";
            CurrentStatus = "エラー";
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "単発翻訳がタイムアウトしました");
            ErrorMessage = $"翻訳実行エラー: {ex.Message}";
            CurrentStatus = "エラー";
        }
#pragma warning disable CA1031 // ViewModel層でのユーザー体験保護のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "単発翻訳実行中に予期しないエラーが発生しました");
            ErrorMessage = $"翻訳実行エラー: {ex.Message}";
            CurrentStatus = "エラー";
        }
#pragma warning restore CA1031
        finally
        {
            _isSingleTranslationActive = false;
            IsTranslating = _automaticTranslationTask?.Status == TaskStatus.Running;
        }
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
                await StartAutomaticTranslationAsync().ConfigureAwait(true);
            }
            else
            {
                await StopAutomaticTranslationAsync().ConfigureAwait(true);
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
    /// 自動翻訳開始
    /// </summary>
    private async Task StartAutomaticTranslationAsync()
    {
        if (_automaticTranslationTask?.Status == TaskStatus.Running)
        {
            _logger?.LogWarning("自動翻訳が既に実行中です");
            return;
        }

        _automaticModeCts = new CancellationTokenSource();
        IsTranslating = true;
        CurrentStatus = "自動翻訳開始中...";

        _automaticTranslationTask = Task.Run(async () =>
        {
            try
            {
                CurrentStatus = "自動翻訳中";
                
                // TODO: 実際のキャプチャサービスが実装されたら置き換える
                // 現在のICaptureServiceにはStartContinuousCaptureAsyncがないため、一時的に代替実装
                await SimulateStartContinuousCaptureAsync(_automaticModeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("自動翻訳がキャンセルされました");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "自動翻訳中に無効な操作が発生しました");
                ErrorMessage = $"自動翻訳エラー: {ex.Message}";
            }
#pragma warning disable CA1031 // バックグラウンドタスクでのアプリケーション安定性のため一般例外をキャッチ
            catch (Exception ex)
            {
                _logger?.LogError(ex, "自動翻訳中に予期しないエラーが発生しました");
                ErrorMessage = $"自動翻訳エラー: {ex.Message}";
            }
#pragma warning restore CA1031
        }, _automaticModeCts.Token);

        _logger?.LogInformation("自動翻訳を開始しました");
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>
    /// 自動翻訳停止
    /// </summary>
    private async Task StopAutomaticTranslationAsync()
    {
        if (_automaticModeCts == null || _automaticTranslationTask?.Status != TaskStatus.Running)
        {
            _logger?.LogWarning("停止する自動翻訳がありません");
            IsTranslating = false;
            CurrentStatus = "準備完了";
            return;
        }

        CurrentStatus = "自動翻訳停止中...";

        try
        {
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
            _automaticModeCts.Cancel();
#pragma warning restore CA1849
            
            // タスクの完了を待機（タイムアウト付き）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _automaticTranslationTask.WaitAsync(timeoutCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("自動翻訳の停止がタイムアウトしました");
        }
        finally
        {
            _automaticModeCts?.Dispose();
            _automaticModeCts = null;
            _automaticTranslationTask = null;
            IsTranslating = _isSingleTranslationActive;
            CurrentStatus = "準備完了";
        }

        _logger?.LogInformation("自動翻訳を停止しました");
    }

    /// <summary>
    /// 現在の状態表示を更新
    /// </summary>
    private void UpdateCurrentStatus()
    {
        if (IsTranslating)
        {
            if (_isSingleTranslationActive)
            {
                CurrentStatus = "単発翻訳実行中...";
            }
            else if (IsAutomaticMode)
            {
                CurrentStatus = "自動翻訳中";
            }
        }
        else
        {
            CurrentStatus = IsAutomaticMode ? "自動翻訳待機中" : "準備完了";
        }
    }

    #region 一時的な代替実装（TODO: 実際のサービス実装後に削除）

    /// <summary>
    /// CaptureOnceAsyncの代替実装
    /// </summary>
    private async Task SimulateCaptureOnceAsync()
    {
        // 画面全体をキャプチャ（既存のメソッドを使用）
        var image = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
        
        // 実際の翻訳処理はここで行われるが、現在は模擬実装
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.Delay(1000, cts.Token).ConfigureAwait(false); // 翻訳処理時間をシミュレート
        
        // 画像リソースを解放
        image?.Dispose();
    }

    /// <summary>
    /// StartContinuousCaptureAsyncの代替実装
    /// </summary>
    private async Task SimulateStartContinuousCaptureAsync(CancellationToken cancellationToken)
    {
        var options = _captureService.GetCaptureOptions();
        var interval = TimeSpan.FromMilliseconds(options.CaptureInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 単発翻訳が実行中の場合は待機
                while (_isSingleTranslationActive && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                // 連続キャプチャと翻訳を実行
                var image = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
                
                // 翻訳実行イベントを発行
                await PublishEventAsync(new TranslationTriggeredEvent(TranslationMode.Automatic)).ConfigureAwait(false);
                
                // 実際の翻訳処理はここで行われるが、現在は模擬実装
                await Task.Delay(500, cancellationToken).ConfigureAwait(false); // 翻訳処理時間をシミュレート
                
                image?.Dispose();

                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "連続キャプチャ中に無効な操作が発生しました");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // エラー時は少し待機
            }
#pragma warning disable CA1031 // バックグラウンドループでのアプリケーション安定性のため一般例外をキャッチ
            catch (Exception ex)
            {
                _logger?.LogError(ex, "連続キャプチャ中に予期しないエラーが発生しました");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // エラー時は少し待機
            }
#pragma warning restore CA1031
        }
    }

    #endregion

    /// <summary>
    /// アクティベーション時の処理
    /// </summary>
    protected override void HandleActivation()
    {
        // 初期状態の設定
        UpdateCurrentStatus();
        
        _logger?.LogDebug("OperationalControlViewModelがアクティベートされました");
    }

    /// <summary>
    /// 非アクティベーション時の処理
    /// </summary>
    protected override void HandleDeactivation()
    {
        // 自動翻訳を停止
        if (IsAutomaticMode)
        {
#pragma warning disable CA1031 // UI非同期処理でのアプリケーション安定性のため一般例外をキャッチ
            _ = Task.Run(async () =>
            {
                try
                {
                    await StopAutomaticTranslationAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "非アクティベーション時の自動翻訳停止でエラーが発生しました");
                }
            });
#pragma warning restore CA1031
        }

        _logger?.LogDebug("OperationalControlViewModelが非アクティベートされました");
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 自動翻訳の停止
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
            _automaticModeCts?.Cancel();
#pragma warning restore CA1849
            _automaticModeCts?.Dispose();
        }

        base.Dispose(disposing);
    }
}

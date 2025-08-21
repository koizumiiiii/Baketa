using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Models;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using CoreOcrResult = Baketa.Core.Models.OCR.OcrResult;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using CoreTranslationSettings = Baketa.Core.Settings.TranslationSettings;
using Baketa.Core.Utilities;
using Baketa.Core.Performance;
using Baketa.Core.Logging;
using Baketa.Infrastructure.Platform.Adapters;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using TranslationService = Baketa.Core.Abstractions.Translation.ITranslationService;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳オーケストレーションサービス実装
/// キャプチャ、翻訳、UI表示の統合管理を担当
/// </summary>
public sealed class TranslationOrchestrationService : ITranslationOrchestrationService, INotifyPropertyChanged, IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly ISettingsService _settingsService;
    private readonly Baketa.Core.Abstractions.OCR.IOcrEngine _ocrEngine;
    private readonly ITranslationEngineFactory _translationEngineFactory;
    private readonly CoordinateBasedTranslationService? _coordinateBasedTranslation;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<TranslationOrchestrationService>? _logger;

    // 状態管理
    private volatile bool _isAutomaticTranslationActive;
    private volatile bool _isSingleTranslationActive;

    // 実行制御
    private CancellationTokenSource? _automaticTranslationCts;
    private Task? _automaticTranslationTask;
    private readonly SemaphoreSlim _singleTranslationSemaphore = new(1, 1);
    private readonly SemaphoreSlim _ocrExecutionSemaphore = new(1, 1);
    private CancellationTokenSource? _latestOcrRequestCts;

    // Observable ストリーム
    private readonly Subject<TranslationResult> _translationResultsSubject = new();
    private readonly Subject<TranslationStatus> _statusChangesSubject = new();
    private readonly Subject<TranslationProgress> _progressUpdatesSubject = new();

    // 前回のキャプチャ画像（差分検出用）
    private IImage? _previousCapturedImage;
    private readonly object _previousImageLock = new();

    // 翻訳完了後の一時停止制御
    private DateTime _lastTranslationCompletedAt = DateTime.MinValue;
    private readonly object _lastTranslationTimeLock = new();
    
    // 前回の翻訳結果（重複チェック用）
    private string _lastTranslatedText = string.Empty;
    private readonly object _lastTranslatedTextLock = new();

    // 翻訳対象ウィンドウハンドル
    private IntPtr? _targetWindowHandle;

    // リソース管理
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    #region コンストラクタ

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="captureService">キャプチャサービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="ocrEngine">OCRエンジン</param>
    /// <param name="translationEngineFactory">翻訳エンジンファクトリー</param>
    /// <param name="coordinateBasedTranslation">座標ベース翻訳サービス</param>
    /// <param name="eventAggregator">イベント集約サービス</param>
    /// <param name="logger">ロガー</param>
    public TranslationOrchestrationService(
        ICaptureService captureService,
        ISettingsService settingsService,
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        ITranslationEngineFactory translationEngineFactory,
        CoordinateBasedTranslationService? coordinateBasedTranslation,
        IEventAggregator eventAggregator,
        ILogger<TranslationOrchestrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(ocrEngine);
        ArgumentNullException.ThrowIfNull(translationEngineFactory);
        ArgumentNullException.ThrowIfNull(eventAggregator);
        
        _captureService = captureService;
        _settingsService = settingsService;
        _ocrEngine = ocrEngine;
        _translationEngineFactory = translationEngineFactory;
        _coordinateBasedTranslation = coordinateBasedTranslation;
        _eventAggregator = eventAggregator;
        _logger = logger;

        // キャプチャオプションの初期設定
        InitializeCaptureOptions();
        
        // 座標ベース翻訳システムが利用可能かログ出力
        if (_coordinateBasedTranslation?.IsCoordinateBasedTranslationAvailable() == true)
        {
            _logger?.LogInformation("🚀 座標ベース翻訳システムが利用可能です - 座標ベース翻訳表示が有効");
        }
        else
        {
            _logger?.LogInformation("📝 座標ベース翻訳システムは利用できません - 従来の翻訳表示を使用");
        }
    }

    #endregion

    #region ITranslationOrchestrationService 実装

    #region 状態プロパティ

    /// <inheritdoc />
    public bool IsAutomaticTranslationActive => _isAutomaticTranslationActive;

    /// <inheritdoc />
    public bool IsSingleTranslationActive => _isSingleTranslationActive;

    /// <inheritdoc />
    public bool IsAnyTranslationActive => _isAutomaticTranslationActive || _isSingleTranslationActive;

    /// <inheritdoc />
    public TranslationMode CurrentMode => _isAutomaticTranslationActive ? TranslationMode.Automatic : TranslationMode.Manual;

    #endregion

    #region INotifyPropertyChanged Implementation

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion

    #region 翻訳実行メソッド

    /// <inheritdoc />
    public async Task StartAutomaticTranslationAsync(IntPtr? targetWindowHandle = null, CancellationToken cancellationToken = default)
    {
        // メソッド呼び出しの絶対最初にファイル直接書き込み（最高優先度）
        try
        {
            // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_1] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🎬 [DIRECT] StartAutomaticTranslationAsync開始 - Hash={Hash}", this.GetHashCode());
            // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_2] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔍 [DEBUG] 開始前OCRエンジン状態: IsInitialized={IsInitialized}", _ocrEngine.IsInitialized);
        }
        catch (Exception directEx)
        {
            System.Diagnostics.Debug.WriteLine($"直接ファイル書き込みエラー: {directEx.Message}");
        }
        
        // 複数の方法でログを記録
        DebugLogUtility.WriteLog($"🎬 StartAutomaticTranslationAsync呼び出し - this={this.GetType().FullName}@{this.GetHashCode()}");
        Console.WriteLine($"🎬 StartAutomaticTranslationAsync呼び出し - this={this.GetType().FullName}@{this.GetHashCode()}");
        
        try
        {
            // 緊急デバッグ: 直接ファイル書き込み
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_3] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] tryブロック開始");
            }
            catch { }
            
            DebugLogUtility.WriteLog($"🎬 StartAutomaticTranslationAsync呼び出し");
            DebugLogUtility.WriteLog($"   🗑️ Disposed: {_disposed.ToString(CultureInfo.InvariantCulture)}");
            DebugLogUtility.WriteLog($"   🔄 すでにアクティブ: {_isAutomaticTranslationActive.ToString(CultureInfo.InvariantCulture)}");
            DebugLogUtility.WriteLog($"   🎯 対象ウィンドウハンドル: {(targetWindowHandle?.ToString(CultureInfo.InvariantCulture) ?? "null (画面全体)")}");
            
            // 翻訳対象ウィンドウハンドルを保存
            _targetWindowHandle = targetWindowHandle;

            // 緊急デバッグ: Disposedチェック前
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_4] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] Disposedチェック前 - _disposed={Disposed}", _disposed);
            }
            catch { }
            
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_5] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] Disposedチェック後");
            }
            catch { }

            // 緊急デバッグ: アクティブチェック前
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_6] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] アクティブチェック前 - IsActive={IsActive}", _isAutomaticTranslationActive);
            }
            catch { }
            
            if (_isAutomaticTranslationActive)
            {
                try
                {
                    // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_7] ファイルアクセス競合回避のためILogger使用
                    _logger?.LogDebug("⚠️ [DEBUG] 既にアクティブなためreturn");
                }
                catch { }
                DebugLogUtility.WriteLog($"⚠️ 自動翻訳は既に実行中です");
                _logger?.LogWarning("自動翻訳は既に実行中です");
                return;
            }

            // 緊急デバッグ: tryブロック終了直前
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_8] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] tryブロック終了直前");
            }
            catch { }

            // 緊急デバッグ: この行に到達するかテスト
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_9] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] 自動翻訳開始直前");
            }
            catch { }

            // 緊急デバッグ: 直接ファイル書き込みで翻訳開始を確認
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_10] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🎬 自動翻訳を開始します（直接書き込み）");
            }
            catch { }
            
            DebugLogUtility.WriteLog($"🎬 自動翻訳を開始します");
            
            // 緊急デバッグ: DebugLogUtility.WriteLog後
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_11] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] DebugLogUtility.WriteLog後");
            }
            catch { }
            
            _logger?.LogInformation("自動翻訳を開始します");

            _automaticTranslationCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disposeCts.Token);

            _isAutomaticTranslationActive = true;
            OnPropertyChanged(nameof(IsAnyTranslationActive));

            // TODO: モード変更イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(
            //     new TranslationModeChangedEvent(TranslationMode.Automatic, TranslationMode.Manual))
            //     .ConfigureAwait(false);

            // バックグラウンドタスクで自動翻訳を実行
            try
            {
                // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 Task.Run開始前（直接書き込み）{Environment.NewLine}");
                // 診断システム実装により debug_app_logs.txt への出力を無効化
            }
            catch { }
            
            DebugLogUtility.WriteLog($"🎬 Task.Run開始前");
            _automaticTranslationTask = Task.Run(async () =>
            {
                try
                {
                    // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 Task.Run内部開始（直接書き込み）{Environment.NewLine}");
                    // 診断システム実装により debug_app_logs.txt への出力を無効化
                }
                catch { }
                
                DebugLogUtility.WriteLog($"🎬 Task.Run内部開始");
                
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }
                
                try
                {
                    await ExecuteAutomaticTranslationLoopAsync(_automaticTranslationCts.Token).ConfigureAwait(false);
                    
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }
                    
                    DebugLogUtility.WriteLog($"💥 ExecuteAutomaticTranslationLoopAsync例外: {ex.Message}");
                    _logger?.LogError(ex, "自動翻訳ループで予期しないエラーが発生しました");
                    throw;
                }
                
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }
                
                DebugLogUtility.WriteLog($"🎬 Task.Run内部終了");
            }, _automaticTranslationCts.Token);
            
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            DebugLogUtility.WriteLog($"🎬 Task.Run開始後");

            // 緊急デバッグ: tryブロック後の実行確認
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }

            await Task.CompletedTask.ConfigureAwait(false);
            
            // 緊急デバッグ: Task.CompletedTask後
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"💥 StartAutomaticTranslationAsync例外: {ex.GetType().Name}: {ex.Message}");
            DebugLogUtility.WriteLog($"💥 スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "StartAutomaticTranslationAsync実行中にエラーが発生しました");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAutomaticTranslationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isAutomaticTranslationActive)
        {
            _logger?.LogWarning("停止する自動翻訳がありません");
            return;
        }

        _logger?.LogInformation("自動翻訳を停止します");
        
        // 直接ファイル書き込みで停止処理開始を記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"翻訳停止ログ書き込みエラー: {fileEx.Message}");
        }

        try
        {
            // キャンセルを要求
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
            _automaticTranslationCts?.Cancel();
#pragma warning restore CA1849

            // タスクの完了を待機（タイムアウト付き）
            if (_automaticTranslationTask != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await _automaticTranslationTask.WaitAsync(combinedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_automaticTranslationCts?.Token.IsCancellationRequested == true)
                {
                    // 内部タスクのキャンセルは正常な停止操作
                    _logger?.LogDebug("自動翻訳タスクが正常にキャンセルされました");
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogWarning("自動翻訳の停止がタイムアウトしました");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 外部からのキャンセルは再スロー
                    _logger?.LogDebug("自動翻訳の停止が外部からキャンセルされました");
                    throw;
                }
            }
        }
        finally
        {
            _automaticTranslationCts?.Dispose();
            _automaticTranslationCts = null;
            _automaticTranslationTask = null;
            _isAutomaticTranslationActive = false;
            OnPropertyChanged(nameof(IsAnyTranslationActive));
            
            // 前回の翻訳結果をリセット（再翻訳時の問題を回避）
            lock (_lastTranslatedTextLock)
            {
                var oldLastText = _lastTranslatedText;
                _lastTranslatedText = string.Empty;
                
                // 直接ファイル書き込みで状態リセットを記録
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"状態リセットログ書き込みエラー: {fileEx.Message}");
                }
            }

            // TODO: モード変更イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(
            //     new TranslationModeChangedEvent(TranslationMode.Manual, TranslationMode.Automatic))
            //     .ConfigureAwait(false);

            _logger?.LogInformation("自動翻訳を停止しました");
        }
    }

    /// <inheritdoc />
    public async Task TriggerSingleTranslationAsync(IntPtr? targetWindowHandle = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // 翻訳対象ウィンドウハンドルを保存
        _targetWindowHandle = targetWindowHandle;

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCts.Token);

        // セマフォを使用して同時実行を制御
        await _singleTranslationSemaphore.WaitAsync(combinedCts.Token).ConfigureAwait(false);

        try
        {
            if (_isSingleTranslationActive)
            {
                _logger?.LogWarning("単発翻訳は既に実行中です");
                return;
            }

            _isSingleTranslationActive = true;
            OnPropertyChanged(nameof(IsAnyTranslationActive));

            _logger?.LogInformation("単発翻訳を実行します");

            // TODO: 翻訳実行イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(new TranslationTriggeredEvent(TranslationMode.Manual))
            //     .ConfigureAwait(false);

            // 単発翻訳を実行
            await ExecuteSingleTranslationAsync(combinedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _isSingleTranslationActive = false;
            OnPropertyChanged(nameof(IsAnyTranslationActive));
            _singleTranslationSemaphore.Release();
        }
    }

    #endregion

    #region Observable ストリーム

    /// <inheritdoc />
    public IObservable<TranslationResult> TranslationResults => _translationResultsSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<TranslationStatus> StatusChanges => _statusChangesSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<TranslationProgress> ProgressUpdates => _progressUpdatesSubject.AsObservable();

    #endregion

    #region 設定管理

    /// <inheritdoc />
    public TimeSpan GetSingleTranslationDisplayDuration()
    {
        var displaySeconds = _settingsService.GetValue("Translation:SingleTranslationDisplaySeconds", 5);
        return TimeSpan.FromSeconds(displaySeconds);
    }

    /// <inheritdoc />
    public TimeSpan GetAutomaticTranslationInterval()
    {
        var intervalMs = _settingsService.GetValue("Translation:AutomaticTranslationIntervalMs", 100);
        return TimeSpan.FromMilliseconds(intervalMs);
    }

    /// <inheritdoc />
    public async Task UpdateTranslationSettingsAsync(TranslationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 設定を保存（実際の実装は設定システムに依存）
        // TODO: 実際の設定保存ロジックを実装
        _logger?.LogInformation("翻訳設定を更新しました");
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    #endregion

    #region リソース管理

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogInformation("TranslationOrchestrationServiceを開始します");
        
        // 初期化処理
        InitializeCaptureOptions();
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        _logger?.LogInformation("TranslationOrchestrationServiceを停止します");

        // 自動翻訳を停止
        await StopAutomaticTranslationAsync(cancellationToken).ConfigureAwait(false);

        // 単発翻訳の完了を待機
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (_isSingleTranslationActive && !combinedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, combinedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 外部からのキャンセルは正常な操作として処理
            _logger?.LogDebug("単発翻訳の停止待機がキャンセルされました");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            // タイムアウトは警告ログを出力
            _logger?.LogWarning("単発翻訳の停止待機がタイムアウトしました");
        }

        _logger?.LogInformation("TranslationOrchestrationServiceを停止しました");
    }

    #endregion

    #endregion

    #region プライベートメソッド

    /// <summary>
    /// キャプチャオプションを初期化
    /// </summary>
    private void InitializeCaptureOptions()
    {
        var intervalMs = _settingsService.GetValue("Translation:AutomaticTranslationIntervalMs", 100);
        var captureOptions = new CaptureOptions
        {
            Quality = 85, // 品質を少し下げてパフォーマンスを向上
            IncludeCursor = false,
            CaptureInterval = intervalMs,
            OptimizationLevel = 2
        };

        _captureService.SetCaptureOptions(captureOptions);
        
        _logger?.LogDebug("キャプチャオプションを初期化しました: 間隔={Interval}ms, 品質={Quality}",
            captureOptions.CaptureInterval, captureOptions.Quality);
    }

    /// <summary>
    /// 翻訳設定を取得
    /// </summary>
    private CoreTranslationSettings GetTranslationSettings()
    {
        // 🚨 CRITICAL FIX: translation-settings.jsonから直接読み取り
        var sourceLanguageFromFile = "English"; // デフォルト値
        var targetLanguageFromFile = "Japanese"; // デフォルト値
        
        try
        {
            // translation-settings.jsonから直接読み取り
            var translationSettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".baketa", "settings", "translation-settings.json");
                
            if (File.Exists(translationSettingsPath))
            {
                var json = File.ReadAllText(translationSettingsPath);
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("sourceLanguage", out var sourceLangElement))
                {
                    sourceLanguageFromFile = sourceLangElement.GetString() ?? "English";
                }
                
                // 🔧 FIX: targetLanguageも読み取るように修正
                if (doc.RootElement.TryGetProperty("targetLanguage", out var targetLangElement))
                {
                    targetLanguageFromFile = targetLangElement.GetString() ?? "Japanese";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [TRANSLATION_SETTINGS_DEBUG] JSON読み取り失敗: {ex.Message}");
        }
        
        // 言語コード変換
        var sourceLanguageCode = GetLanguageCode(sourceLanguageFromFile);
        var targetLanguageCode = GetLanguageCode(targetLanguageFromFile);
        
        // 緊急デバッグ: 設定取得状況を詳細ログ
        Console.WriteLine($"🔍 [TRANSLATION_SETTINGS_DEBUG] 取得した設定:");
        Console.WriteLine($"   - sourceLanguageFromFile: '{sourceLanguageFromFile}' → '{sourceLanguageCode}'");
        Console.WriteLine($"   - targetLanguageFromFile: '{targetLanguageFromFile}' → '{targetLanguageCode}'");
        Console.WriteLine($"   - _settingsService type: {_settingsService?.GetType()?.Name ?? "null"}");
        
        // ファイルログに記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }
        
        Console.WriteLine($"🌍 [LANGUAGE_SETTING] 設定ファイル連携: {sourceLanguageFromFile}→{targetLanguageFromFile} ({sourceLanguageCode}→{targetLanguageCode})");
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化{Environment.NewLine}");
        }
        catch { }
        
        _logger?.LogDebug("🌍 翻訳言語設定取得: {SourceDisplay}→{TargetDisplay} ({SourceCode}→{TargetCode})", 
            sourceLanguageFromFile, targetLanguageFromFile, sourceLanguageCode, targetLanguageCode);
        
        return new CoreTranslationSettings
        {
            // 設定ファイルから読み取った言語設定を使用
            DefaultSourceLanguage = sourceLanguageCode,
            DefaultTargetLanguage = targetLanguageCode,
            // テスト環境では短い間隔を使用して高速化
            TranslationDelayMs = 100 // 100ms間隔でテストを高速化
        };
    }
    
    /// <summary>
    /// 言語コードから表示名を取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>言語の表示名</returns>
    private static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "ja" => "Japanese",
            "en" => "English",
            "zh" or "zh-cn" or "zh-hans" => "Chinese (Simplified)",
            "zh-tw" or "zh-hant" => "Chinese (Traditional)",
            "ko" => "Korean",
            "fr" => "French",
            "de" => "German",
            "es" => "Spanish",
            "pt" => "Portuguese",
            "ru" => "Russian",
            _ => languageCode.ToUpperInvariant()
        };
    }

    /// <summary>
    /// 日本語表示名を言語コードに変換します
    /// </summary>
    /// <param name="displayName">日本語表示名（例：「英語」「簡体字中国語」）</param>
    /// <returns>言語コード（例：「en」「zh-cn」）</returns>
    private static string GetLanguageCode(string displayName)
    {
        return displayName switch
        {
            "日本語" => "ja",
            "英語" => "en",
            "English" => "en",  // 🔧 FIX: 英語表示名追加
            "Japanese" => "ja", // 🔧 FIX: 日本語表示名追加
            "簡体字中国語" => "zh-cn",
            "繁体字中国語" => "zh-tw",
            "韓国語" => "ko",
            "フランス語" => "fr",
            "ドイツ語" => "de",
            "スペイン語" => "es",
            "ポルトガル語" => "pt",
            "ロシア語" => "ru",
            _ => displayName.ToLowerInvariant() // 不明な場合はそのまま小文字で返す
        };
    }

    /// <summary>
    /// 自動翻訳ループを実行
    /// </summary>
    private async Task ExecuteAutomaticTranslationLoopAsync(CancellationToken cancellationToken)
    {
        // 緊急デバッグ: メソッド開始確認
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }
        
        Console.WriteLine($"🔄 ExecuteAutomaticTranslationLoopAsync開始");
        Console.WriteLine($"   ⏱️ 開始時キャンセル要求: {cancellationToken.IsCancellationRequested}");
        
        var intervalMs = _settingsService.GetValue("Translation:AutomaticTranslationIntervalMs", 100);
        var interval = TimeSpan.FromMilliseconds(intervalMs);
        
        // PaddleOCRエラー発生時の遅延調整
        var minInterval = TimeSpan.FromMilliseconds(500); // 最小間隔を500msに設定
        if (interval < minInterval)
        {
            interval = minInterval;
            _logger?.LogWarning("自動翻訳間隔が短すぎるため、{MinInterval}msに調整しました", minInterval.TotalMilliseconds);
        }

        Console.WriteLine($"🔄 自動翻訳ループを開始しました（間隔: {interval.TotalMilliseconds}ms）");
        _logger?.LogDebug("自動翻訳ループを開始しました（間隔: {Interval}ms）", interval.TotalMilliseconds);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 緊急デバッグ: ループ実行確認
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }
                
                Console.WriteLine($"🔄 自動翻訳ループ実行中 - キャンセル: {cancellationToken.IsCancellationRequested}");
                Console.WriteLine($"   🔒 単発翻訳実行中: {_isSingleTranslationActive}");
                
                try
                {
                    // 単発翻訳が実行中の場合は待機
                    while (_isSingleTranslationActive && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return; // キャンセル時は正常終了
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // 自動翻訳を実行
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }
                    
                    await ExecuteAutomaticTranslationStepAsync(cancellationToken).ConfigureAwait(false);
                    
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }

                    // 次の実行まで待機
                    try
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return; // キャンセル時は正常終了
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break; // キャンセル時はループ終了
                }
#pragma warning disable CA1031 // バックグラウンドループでのアプリケーション安定性のため一般例外をキャッチ
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "自動翻訳ループでエラーが発生しました");
                    
                    // エラー時は少し長めに待機
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return; // キャンセル時は正常終了
                    }
                }
#pragma warning restore CA1031
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // キャンセルは正常な終了操作
        }
        finally
        {
            _logger?.LogDebug("自動翻訳ループを終了しました");
        }
    }

    /// <summary>
    /// 自動翻訳の1ステップを実行
    /// </summary>
    private async Task ExecuteAutomaticTranslationStepAsync(CancellationToken cancellationToken)
    {
        // 緊急デバッグ: ExecuteAutomaticTranslationStepAsync開始確認
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }
        
        var translationId = Guid.NewGuid().ToString("N")[..8];
        DebugLogUtility.WriteLog($"🎯 自動翻訳ステップ開始: ID={translationId}");
        DebugLogUtility.WriteLog($"   ⏱️ 開始時キャンセル要求: {cancellationToken.IsCancellationRequested}");
        DebugLogUtility.WriteLog($"   📡 CaptureServiceが利用可能: {_captureService != null}");
        
        // 翻訳完了後のクールダウン期間チェック
        DateTime lastTranslationTime;
        lock (_lastTranslationTimeLock)
        {
            lastTranslationTime = _lastTranslationCompletedAt;
        }
        
        var cooldownSeconds = _settingsService.GetValue("Translation:PostTranslationCooldownSeconds", 3);
        var timeSinceLastTranslation = DateTime.UtcNow - lastTranslationTime;
        
        if (timeSinceLastTranslation.TotalSeconds < cooldownSeconds)
        {
            var remainingCooldown = cooldownSeconds - timeSinceLastTranslation.TotalSeconds;
            
            // 緊急デバッグ: クールダウン中の直接書き込み
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            DebugLogUtility.WriteLog($"⏳ 翻訳完了後のクールダウン中: ID={translationId}, 残り{remainingCooldown:F1}秒");
            return; // クールダウン中はスキップ
        }
        
        // 緊急デバッグ: クールダウン通過確認
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }
        
        IImage? currentImage = null;
        try
        {
            // 進行状況を通知
            PublishProgress(translationId, TranslationStatus.Capturing, 0.1f, "画面キャプチャ中...");

            // 画面またはウィンドウをキャプチャ
            if (_targetWindowHandle.HasValue)
            {
                var windowHandle = _targetWindowHandle.Value;
                DebugLogUtility.WriteLog($"📷 ウィンドウキャプチャ開始: Handle={windowHandle}");
                currentImage = await _captureService!.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
                if (currentImage is null)
                {
                    throw new TranslationException("ウィンドウキャプチャに失敗しました");
                }
                DebugLogUtility.WriteLog($"📷 ウィンドウキャプチャ完了: {(currentImage is not null ? "成功" : "失敗")}");
            }
            else
            {
                DebugLogUtility.WriteLog($"📷 画面全体キャプチャ開始");
                currentImage = await _captureService!.CaptureScreenAsync().ConfigureAwait(false);
                if (currentImage is null)
                {
                    throw new TranslationException("画面キャプチャに失敗しました");
                }
                DebugLogUtility.WriteLog($"📷 画面全体キャプチャ完了: {(currentImage is not null ? "成功" : "失敗")}");
            }
            
            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();
            
            // 画面変化検出による無駄な処理の削減
            IImage? previousImageForComparison = null;
            lock (_previousImageLock)
            {
                if (_previousCapturedImage != null)
                {
                    try
                    {
                        // 比較用に前回画像をクローン（lock外で比較するため）
                        previousImageForComparison = _previousCapturedImage.Clone();
                    }
                    catch (Exception ex)
                    {
                        DebugLogUtility.WriteLog($"⚠️ 前回画像クローン失敗、翻訳処理を継続: {ex.Message}");
                        _logger?.LogWarning(ex, "前回画像のクローンに失敗しましたが、翻訳処理を継続します");
                    }
                }
            }
            
            if (previousImageForComparison != null && currentImage != null)
            {
                try
                {
                    var hasChanges = await _captureService.DetectChangesAsync(
                        previousImageForComparison, currentImage, 0.05f)
                        .ConfigureAwait(false);

                    if (!hasChanges)
                    {
                        DebugLogUtility.WriteLog($"🔄 画面に変化がないため翻訳をスキップ: ID={translationId}");
                        _logger?.LogTrace("画面に変化がないため翻訳をスキップします");
                        currentImage?.Dispose();
                        previousImageForComparison?.Dispose();
                        return;
                    }
                    DebugLogUtility.WriteLog($"📸 画面変化を検出、翻訳処理を継続: ID={translationId}");
                }
                catch (Exception ex)
                {
                    DebugLogUtility.WriteLog($"⚠️ 画面変化検出でエラー、翻訳処理を継続: {ex.Message}");
                    _logger?.LogWarning(ex, "画面変化検出でエラーが発生しましたが、翻訳処理を継続します");
                }
                finally
                {
                    previousImageForComparison?.Dispose();
                }
            }

            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();

            // TODO: 翻訳実行イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(new TranslationTriggeredEvent(TranslationMode.Automatic))
            //     .ConfigureAwait(false);

            // null チェック
            if (currentImage == null)
            {
                DebugLogUtility.WriteLog($"❌ 画面キャプチャが失敗しました: ID={translationId}");
                return;
            }

            // 翻訳を実行
            // 緊急デバッグ: ExecuteTranslationAsync呼び出し前
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            DebugLogUtility.WriteLog($"🌍 翻訳処理開始: ID={translationId}");
            try
            {
                var result = await ExecuteTranslationAsync(translationId, currentImage!, TranslationMode.Automatic, cancellationToken)
                    .ConfigureAwait(false);
                
                // 緊急デバッグ: ExecuteTranslationAsync完了
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }
                
                DebugLogUtility.WriteLog($"🌍 翻訳処理完了: ID={translationId}");

                // キャンセルチェック
                cancellationToken.ThrowIfCancellationRequested();

                // 翻訳結果の重複チェック
                string lastTranslatedText;
                lock (_lastTranslatedTextLock)
                {
                    lastTranslatedText = _lastTranslatedText;
                }
                
                if (!string.IsNullOrEmpty(lastTranslatedText) && 
                    string.Equals(result?.TranslatedText, lastTranslatedText, StringComparison.Ordinal))
                {
                    DebugLogUtility.WriteLog($"🔄 前回と同じ翻訳結果のため発行をスキップ: '{result?.TranslatedText}'");
                    return;
                }
                
                // 座標ベース翻訳モードの場合はObservable発行をスキップ
                if (result?.IsCoordinateBasedMode == true)
                {
                    DebugLogUtility.WriteLog($"🎯 座標ベース翻訳モードのためObservable発行をスキップ");
                    // 翻訳完了時刻を記録
                    lock (_lastTranslationTimeLock)
                    {
                        _lastTranslationCompletedAt = DateTime.UtcNow;
                    }
                    return;
                }
                
                // 翻訳完了時刻と結果を記録（重複翻訳防止用）
                lock (_lastTranslationTimeLock)
                {
                    _lastTranslationCompletedAt = DateTime.UtcNow;
                }
                lock (_lastTranslatedTextLock)
                {
                    _lastTranslatedText = result?.TranslatedText ?? string.Empty;
                }
                
                // 結果を通知（UI層でスケジューラ制御）
                if (result != null)
                {
                    DebugLogUtility.WriteLog($"📤 翻訳結果をObservableに発行: '{result.TranslatedText}'");
                    _translationResultsSubject.OnNext(result);
                    DebugLogUtility.WriteLog($"✅ 翻訳結果発行完了");
                }
                else
                {
                    DebugLogUtility.WriteLog($"⚠️ 翻訳結果がnullのためObservable発行をスキップ");
                }
            }
            catch (Exception translationEx) when (translationEx.Message.Contains("PaddlePredictor") || 
                                                  translationEx.Message.Contains("OCR") ||
                                                  translationEx is OperationCanceledException)
            {
                // OCRエラーの場合は翻訳結果を発行せず、ログ記録のみ
                DebugLogUtility.WriteLog($"🚫 OCRエラーにより翻訳をスキップ: ID={translationId}, Error={translationEx.Message}");
                _logger?.LogWarning(translationEx, "OCRエラーにより翻訳をスキップしました: TranslationId={TranslationId}", translationId);
                
                // PaddleOCRエラーの場合は追加の待機を設定
                if (translationEx.Message.Contains("PaddlePredictor") || translationEx.Message.Contains("run failed"))
                {
                    DebugLogUtility.WriteLog($"⏳ PaddleOCRエラーのため追加待機を実行: 2秒");
                    _logger?.LogInformation("PaddleOCRエラーが発生したため、次のキャプチャまで2秒待機します");
                    
                    // エラー発生時のクールダウンを設定
                    lock (_lastTranslationTimeLock)
                    {
                        _lastTranslationCompletedAt = DateTime.UtcNow.AddSeconds(2);
                    }
                }
                
                // 現在の画像を破棄して早期リターン
                currentImage?.Dispose();
                return;
            }

            // 前回のキャプチャ画像を安全に更新
            lock (_previousImageLock)
            {
                var oldImage = _previousCapturedImage;
                _previousCapturedImage = null; // 一旦クリア
                
                try
                {
                    // 現在の画像のコピーを作成して保持
                    _previousCapturedImage = currentImage.Clone();
                }
                catch (Exception ex)
                {
                    DebugLogUtility.WriteLog($"⚠️ 前回画像の更新に失敗: {ex.Message}");
                    _logger?.LogWarning(ex, "前回キャプチャ画像の更新に失敗しました");
                }
                
                // 古い画像を安全に破棄
                oldImage?.Dispose();
            }
            
            // 現在の画像を破棄
            currentImage?.Dispose();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DebugLogUtility.WriteLog($"❌ 自動翻訳ステップがキャンセルされました: ID={translationId}");
            currentImage?.Dispose(); // キャンセル時のリソース破棄
            PublishProgress(translationId, TranslationStatus.Cancelled, 1.0f, "キャンセルされました");
            throw; // キャンセルは再スロー
        }
#pragma warning disable CA1031 // サービス層でのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            Console.WriteLine($"💥 自動翻訳ステップでエラー: ID={translationId}, エラー={ex.Message}");
            currentImage?.Dispose(); // エラー時のリソース破棄
            _logger?.LogError(ex, "自動翻訳ステップでエラーが発生しました");
            PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"エラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 単発翻訳を実行
    /// </summary>
    private async Task ExecuteSingleTranslationAsync(CancellationToken cancellationToken)
    {
        var translationId = Guid.NewGuid().ToString("N")[..8];
        
        // 🚨 CRITICAL DEBUG: ExecuteSingleTranslationAsync呼び出し確認
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            Console.WriteLine($"🚨 [SINGLE_TRANSLATION] ExecuteSingleTranslationAsync呼び出し開始: ID={translationId}");
        }
        catch { }
        
        try
        {
            // 進行状況を通知
            PublishProgress(translationId, TranslationStatus.Capturing, 0.1f, "画面キャプチャ中...");

            // 画面をキャプチャ
            var currentImage = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
            
            using (currentImage)
            {
                // 翻訳を実行
                var result = await ExecuteTranslationAsync(translationId, currentImage!, TranslationMode.Manual, cancellationToken)
                    .ConfigureAwait(false);

                // 単発翻訳の表示時間を設定
                result = result with { DisplayDuration = GetSingleTranslationDisplayDuration() };

                // 翻訳完了時刻を記録（重複翻訳防止用）
                lock (_lastTranslationTimeLock)
                {
                    _lastTranslationCompletedAt = DateTime.UtcNow;
                }
                
                // 結果を通知（UI層でスケジューラ制御）
                _translationResultsSubject.OnNext(result);

                _logger?.LogInformation("単発翻訳が完了しました: ID={Id}, テキスト長={Length}", 
                    translationId, result.TranslatedText.Length);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishProgress(translationId, TranslationStatus.Cancelled, 1.0f, "キャンセルされました");
            throw;
        }
#pragma warning disable CA1031 // サービス層でのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "単発翻訳でエラーが発生しました");
            PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"エラー: {ex.Message}");
            throw;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 翻訳を実行
    /// </summary>
    private async Task<TranslationResult> ExecuteTranslationAsync(
        string translationId, 
        IImage image, 
        TranslationMode mode, 
        CancellationToken cancellationToken)
    {
        // 🚨 CRITICAL DEBUG: ExecuteTranslationAsync呼び出し確認
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            Console.WriteLine($"🚨 [EXECUTE_TRANSLATION] ExecuteTranslationAsync呼び出し開始: ID={translationId}, Mode={mode}");
        }
        catch { }
        
        // 🚨 CRITICAL DEBUG: PerformanceMeasurement作成前
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }
        
        using var overallMeasurement = new PerformanceMeasurement(
            MeasurementType.OverallProcessing, 
            $"翻訳実行全体 - ID:{translationId}, Mode:{mode}")
            .WithAdditionalInfo($"ImageType:{image?.GetType().Name}");

        // 🚨 CRITICAL DEBUG: PerformanceMeasurement作成完了
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }

        var startTime = DateTime.UtcNow;
        string originalText = string.Empty;
        double ocrConfidence = 0.0;

        try
        {
            // 🚨 CRITICAL DEBUG: try文開始直後
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            // 🚨 CRITICAL DEBUG: DebugLogUtility.WriteLog呼び出し直前
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            // 🚨 CRITICAL DEBUG: 座標ベース翻訳チェック（直接ファイル書き込み）
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化}{Environment.NewLine}");
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            // 座標ベース翻訳システムの利用可能性をチェック
            DebugLogUtility.WriteLog($"🔍 座標ベース翻訳チェック:");
            DebugLogUtility.WriteLog($"   📦 _coordinateBasedTranslation != null: {_coordinateBasedTranslation != null}");
            DebugLogUtility.WriteLog($"   ✅ IsCoordinateBasedTranslationAvailable: {_coordinateBasedTranslation?.IsCoordinateBasedTranslationAvailable()}");
            DebugLogUtility.WriteLog($"   🪟 _targetWindowHandle.HasValue: {_targetWindowHandle.HasValue}");
            DebugLogUtility.WriteLog($"   🪟 _targetWindowHandle: {_targetWindowHandle?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}");
            
            // 🚨 CRITICAL DEBUG: DebugLogUtility.WriteLog呼び出し完了
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            DebugLogUtility.WriteLog($"   🖼️ image is IAdvancedImage: {image is IAdvancedImage}");
            
            // 座標ベース翻訳システムが利用可能な場合は座標ベース処理を実行
            var coordinateAvailable = _coordinateBasedTranslation?.IsCoordinateBasedTranslationAvailable() == true;
            var hasWindowHandle = _targetWindowHandle.HasValue;
            var isAdvancedImage = image is IAdvancedImage;
            var overallCondition = coordinateAvailable && hasWindowHandle && isAdvancedImage;
            
            // 緊急デバッグ: 座標ベース翻訳条件確認
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            
            DebugLogUtility.WriteLog($"🎯 座標ベース翻訳条件評価結果: {overallCondition}");
            DebugLogUtility.WriteLog($"   📋 詳細条件:");
            DebugLogUtility.WriteLog($"     📦 coordinateAvailable: {coordinateAvailable}");
            DebugLogUtility.WriteLog($"     🪟 hasWindowHandle: {hasWindowHandle}");
            DebugLogUtility.WriteLog($"     🖼️ isAdvancedImage: {isAdvancedImage}");
            
            Console.WriteLine($"🎯 座標ベース翻訳条件評価結果: {overallCondition}");
            Console.WriteLine($"   📦 coordinateAvailable: {coordinateAvailable}");
            Console.WriteLine($"   🪟 hasWindowHandle: {hasWindowHandle}");
            Console.WriteLine($"   🖼️ isAdvancedImage: {isAdvancedImage}");
            
            if (overallCondition && image is IAdvancedImage advancedImage)
            {
                // 緊急デバッグ: 座標ベース翻訳実行開始
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }
                
                DebugLogUtility.WriteLog($"🎯 座標ベース翻訳処理を実行開始: ID={translationId}");
                _logger?.LogDebug("🎯 座標ベース翻訳処理を実行: ID={TranslationId}", translationId);
                
                try
                {
                    // 座標ベース翻訳処理を実行（BatchOCR + MultiWindowOverlay）
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }
                    
                    DebugLogUtility.WriteLog($"🔄 ProcessWithCoordinateBasedTranslationAsync呼び出し開始");
                    await _coordinateBasedTranslation!.ProcessWithCoordinateBasedTranslationAsync(
                        advancedImage, 
                        _targetWindowHandle!.Value, 
                        cancellationToken)
                        .ConfigureAwait(false);
                    
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }
                    
                    DebugLogUtility.WriteLog($"✅ ProcessWithCoordinateBasedTranslationAsync呼び出し完了");
                    _logger?.LogInformation("✅ 座標ベース翻訳処理完了: ID={TranslationId}", translationId);
                    
                    // 座標ベース処理が成功した場合、オーバーレイで直接表示されるため、
                    // 従来の翻訳結果は空の結果を返す
                    // ただし、IsCoordinateBasedModeをtrueに設定して、Observableへの発行をスキップする
                    return new TranslationResult
                    {
                        Id = translationId,
                        Mode = mode,
                        OriginalText = "",
                        TranslatedText = "",
                        DetectedLanguage = "ja",
                        TargetLanguage = GetLanguageCode(_settingsService.GetValue("UI:TranslationLanguage", "英語")),
                        Confidence = 1.0f,
                        ProcessingTime = DateTime.UtcNow - startTime,
                        IsCoordinateBasedMode = true // 座標ベースモードを示すフラグ
                    };
                }
                catch (Exception coordinateEx)
                {
                    DebugLogUtility.WriteLog($"❌ 座標ベース処理でエラー発生: {coordinateEx.Message}");
                    DebugLogUtility.WriteLog($"❌ エラーのスタックトレース: {coordinateEx.StackTrace}");
                    _logger?.LogWarning(coordinateEx, "⚠️ 座標ベース処理でエラーが発生、従来のOCR処理にフォールバック: ID={TranslationId}", translationId);
                    // 座標ベース処理でエラーが発生した場合は従来のOCR処理にフォールバック
                }
            }
            else
            {
                DebugLogUtility.WriteLog($"⚠️ 座標ベース翻訳をスキップ（条件不一致）");
                if (_coordinateBasedTranslation == null)
                    DebugLogUtility.WriteLog($"   理由: _coordinateBasedTranslation is null");
                else if (_coordinateBasedTranslation.IsCoordinateBasedTranslationAvailable() != true)
                    DebugLogUtility.WriteLog($"   理由: IsCoordinateBasedTranslationAvailable() = {_coordinateBasedTranslation.IsCoordinateBasedTranslationAvailable()}");
                if (!_targetWindowHandle.HasValue)
                    DebugLogUtility.WriteLog($"   理由: _targetWindowHandle is null");
                if (image is not IAdvancedImage)
                    DebugLogUtility.WriteLog($"   理由: image is not IAdvancedImage (actual type: {image?.GetType()?.Name ?? "null"})");
            }

            // OCR処理
            PublishProgress(translationId, TranslationStatus.ProcessingOCR, 0.3f, "テキスト認識中...");
            
            DebugLogUtility.WriteLog($"🔍 OCRエンジン状態チェック - IsInitialized: {_ocrEngine.IsInitialized}");
            
            // OCRエンジンが初期化されていない場合は初期化
            if (!_ocrEngine.IsInitialized)
            {
                DebugLogUtility.WriteLog($"🛠️ OCRエンジン初期化開始");
                
                var ocrSettings = new OcrEngineSettings
                {
                    Language = "jpn", // 日本語
                    DetectionThreshold = 0.1f, // 緊急対応: より多くの文字領域を検出（0.3→0.1に緩和）
                    RecognitionThreshold = 0.1f // 緊急対応: 認識閾値を大幅緩和でゲームテキスト検出改善（0.3→0.1）
                };
                
                try
                {
                    await _ocrEngine.InitializeAsync(ocrSettings, cancellationToken).ConfigureAwait(false);
                    DebugLogUtility.WriteLog($"✅ OCRエンジン初期化完了");
                }
                catch (Exception initEx)
                {
                    DebugLogUtility.WriteLog($"❌ OCRエンジン初期化エラー: {initEx.Message}");
                    throw;
                }
            }
            else
            {
                // 既に初期化されているが、閾値設定を更新する
                DebugLogUtility.WriteLog($"🔄 既に初期化されたOCRエンジンの設定を更新");
                
                var updatedSettings = new OcrEngineSettings
                {
                    Language = "jpn", // 日本語
                    DetectionThreshold = 0.1f, // 緊急対応: より多くの文字領域を検出（0.3→0.1に緩和）
                    RecognitionThreshold = 0.1f // 緊急対応: 認識閾値を大幅緩和でゲームテキスト検出改善（0.3→0.1）
                };
                
                try
                {
                    await _ocrEngine.ApplySettingsAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
                    DebugLogUtility.WriteLog($"✅ OCRエンジン設定更新完了");
                }
                catch (Exception applyEx)
                {
                    DebugLogUtility.WriteLog($"⚠️ OCRエンジン設定更新エラー: {applyEx.Message}");
                    // 設定更新に失敗しても翻訳処理は続行する
                }
            }
            
            // 実際のOCR処理を実行
            Console.WriteLine($"🔍 画像オブジェクト確認:");
            Console.WriteLine($"   📷 画像オブジェクト: {image?.GetType().Name ?? "null"}");
            Console.WriteLine($"   📊 画像null判定: {image == null}");
            
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 画像オブジェクト確認:{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📷 画像オブジェクト: {image?.GetType().Name ?? "null"}{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📊 画像null判定: {image == null}{Environment.NewLine}");
            
            try
            {
                DebugLogUtility.WriteLog($"🔍 OCR処理開始 - 画像サイズ: {image?.Width ?? 0}x{image?.Height ?? 0}");
                DebugLogUtility.WriteLog($"🖼️ 画像情報: 型={image?.GetType().Name ?? "null"}");
                
                // デバッグ用: キャプチャした画像を保存
                if (image != null)
                {
                    try
                    {
                        var debugImagePath = Path.Combine(Directory.GetCurrentDirectory(), $"debug_captured_{translationId}.png");
                        await SaveImageForDebugAsync(image, debugImagePath).ConfigureAwait(false);
                        DebugLogUtility.WriteLog($"🖼️ デバッグ用画像保存: {debugImagePath}");
                    }
                    catch (Exception saveEx)
                    {
                        DebugLogUtility.WriteLog($"⚠️ デバッグ画像保存エラー: {saveEx.Message}");
                    }
                }
                
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 OCR処理開始 - 画像サイズ: {image?.Width ?? 0}x{image?.Height ?? 0}{Environment.NewLine}");
            }
            catch (Exception sizeEx)
            {
                DebugLogUtility.WriteLog($"❌ 画像サイズ取得エラー: {sizeEx.Message}");
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 画像サイズ取得エラー: {sizeEx.Message}{Environment.NewLine}");
                throw;
            }
            
            ArgumentNullException.ThrowIfNull(image, nameof(image));
            
            // 最新要求優先: 前のOCR要求を強制キャンセル
            var oldCts = _latestOcrRequestCts;
            _latestOcrRequestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            if (oldCts != null)
            {
                try
                {
                    DebugLogUtility.WriteLog($"🛑 前のOCR要求を強制キャンセル: ID={translationId}");
                    oldCts.Cancel();
                    
                    // PaddleOCRエンジンのタイムアウトもキャンセル
                    _ocrEngine.CancelCurrentOcrTimeout();
                }
                catch (Exception cancelEx)
                {
                    DebugLogUtility.WriteLog($"⚠️ OCR強制キャンセル中にエラー: {cancelEx.Message}");
                }
                finally
                {
                    oldCts.Dispose();
                }
            }
            
            var currentRequestToken = _latestOcrRequestCts.Token;
            
            DebugLogUtility.WriteLog($"🤖 OCRエンジン呼び出し開始（排他制御付き）:");
            DebugLogUtility.WriteLog($"   🔧 エンジン名: {_ocrEngine?.EngineName ?? "(null)"}");
            DebugLogUtility.WriteLog($"   ✅ 初期化状態: {_ocrEngine?.IsInitialized ?? false}");
            DebugLogUtility.WriteLog($"   🌐 現在の言語: {_ocrEngine?.CurrentLanguage ?? "(null)"}");
            
            OcrResults ocrResults;
            
            // OCR処理の排他制御
            await _ocrExecutionSemaphore.WaitAsync(currentRequestToken).ConfigureAwait(false);
            try
            {
                // 最新要求かどうかチェック
                if (_latestOcrRequestCts?.Token != currentRequestToken)
                {
                    DebugLogUtility.WriteLog($"🚫 古いOCR要求のためキャンセル: ID={translationId}");
                    currentRequestToken.ThrowIfCancellationRequested();
                }
                
                DebugLogUtility.WriteLog($"🔒 OCR処理を排他実行開始: ID={translationId}");
                ocrResults = await _ocrEngine!.RecognizeAsync(image, cancellationToken: currentRequestToken).ConfigureAwait(false);
                DebugLogUtility.WriteLog($"🔓 OCR処理を排他実行完了: ID={translationId}");
            }
            finally
            {
                _ocrExecutionSemaphore.Release();
            }
            
            DebugLogUtility.WriteLog($"🤖 OCRエンジン呼び出し完了");
            
            // 🚀 [OCR_TRANSLATION_BRIDGE_FIX] OCR完了イベントを発行して翻訳フローを開始
            try
            {
                Console.WriteLine($"🔥 [BRIDGE_FIX] OCR完了イベント発行開始: TextRegions数={ocrResults.TextRegions.Count}");
                
                // OCR結果をOcrResultsコレクションに変換
                var ocrResultsList = ocrResults.TextRegions.Select(region => new CoreOcrResult(
                    text: region.Text,
                    bounds: region.Bounds,
                    confidence: (float)region.Confidence)).ToList().AsReadOnly();

                var ocrCompletedEvent = new OcrCompletedEvent(
                    sourceImage: image,
                    results: ocrResultsList,
                    processingTime: ocrResults.ProcessingTime);

                Console.WriteLine($"🔥 [BRIDGE_FIX] OcrCompletedEvent作成完了 - ID: {ocrCompletedEvent.Id}");
                
                // 🔧 [DUPLICATE_FIX] 重複表示修正: CoordinateBasedTranslationService使用時のみ無効化
                if (_coordinateBasedTranslation == null)
                {
                    // CoordinateBasedTranslationServiceが無効な場合のみイベント発行
                    await _eventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                    Console.WriteLine($"🔥 [BRIDGE_FIX] OcrCompletedEvent発行完了 - 翻訳フロー開始");
                }
                else
                {
                    Console.WriteLine($"🚫 [DUPLICATE_FIX] CoordinateBasedTranslationServiceが有効のため、OcrCompletedEvent発行をスキップ");
                }
                _logger?.LogInformation("🔥 [BRIDGE_FIX] OCR完了イベント発行完了: TextRegions数={Count}, ID={EventId}", 
                    ocrResults.TextRegions.Count, ocrCompletedEvent.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔥 [BRIDGE_FIX] OCR完了イベント発行エラー: {ex.GetType().Name} - {ex.Message}");
                _logger?.LogError(ex, "🔥 [BRIDGE_FIX] OCR完了イベント発行中にエラーが発生");
                // エラーが発生しても翻訳フローは継続（フォールバック）
            }
            
            DebugLogUtility.WriteLog($"📊 OCR結果: HasText={ocrResults.HasText}, TextRegions数={ocrResults.TextRegions.Count}");
            DebugLogUtility.WriteLog($"⏱️ OCR処理時間: {ocrResults.ProcessingTime.TotalMilliseconds:F1}ms");
            DebugLogUtility.WriteLog($"🌐 OCR言語: {ocrResults.LanguageCode}");
            
            // 詳細なOCRデバッグ情報を表示
            if (ocrResults.TextRegions.Count > 0)
            {
                DebugLogUtility.WriteLog($"🔍 詳細なOCRテキストリージョン情報:");
                for (int i = 0; i < Math.Min(5, ocrResults.TextRegions.Count); i++) // 最初の5個だけ表示
                {
                    var region = ocrResults.TextRegions[i];
                    DebugLogUtility.WriteLog($"   リージョン {i + 1}:");
                    DebugLogUtility.WriteLog($"     📖 テキスト: '{region.Text ?? "(null)"}'");
                    DebugLogUtility.WriteLog($"     📊 信頼度: {region.Confidence:F4}");
                    DebugLogUtility.WriteLog($"     📍 座標: X={region.Bounds.X}, Y={region.Bounds.Y}, W={region.Bounds.Width}, H={region.Bounds.Height}");
                    DebugLogUtility.WriteLog($"     🔢 テキスト長: {region.Text?.Length ?? 0}");
                }
                if (ocrResults.TextRegions.Count > 5)
                {
                    DebugLogUtility.WriteLog($"   ... 他 {ocrResults.TextRegions.Count - 5} 個のリージョン");
                }
            }
            else
            {
                DebugLogUtility.WriteLog($"📝 TextRegionsが空です");
                DebugLogUtility.WriteLog($"❌ OCR処理でテキストが検出されませんでした");
                DebugLogUtility.WriteLog($"🖼️ 確認事項: 画像内にテキストが含まれているか、OCRエンジンが正常に動作しているか");
            }
            
            if (ocrResults.HasText)
            {
                // 設定に基づいてテキスト結合方法を選択
                var enableTextGrouping = _settingsService.GetValue("Translation:EnableTextGrouping", true);
                
                if (enableTextGrouping)
                {
                    // レイアウト情報を活用した改良されたテキスト結合を使用
                    var preserveParagraphs = _settingsService.GetValue("Translation:PreserveParagraphs", true);
                    var sameLineThreshold = _settingsService.GetValue("Translation:SameLineThreshold", 0.5);
                    var paragraphSeparationThreshold = _settingsService.GetValue("Translation:ParagraphSeparationThreshold", 1.5);
                    
                    originalText = ocrResults.GetGroupedText(
                        preserveParagraphs: preserveParagraphs,
                        sameLineThreshold: sameLineThreshold,
                        paragraphSeparationThreshold: paragraphSeparationThreshold);
                    
                    DebugLogUtility.WriteLog($"📋 テキストグループ化を使用: 段落保持={preserveParagraphs}");
                }
                else
                {
                    // 従来の単純な改行区切り結合
                    originalText = ocrResults.Text;
                    
                    DebugLogUtility.WriteLog($"📋 従来のテキスト結合を使用");
                }
                
                ocrConfidence = ocrResults.TextRegions.Count > 0 
                    ? ocrResults.TextRegions.Average(r => r.Confidence) 
                    : 0.0;
                
                DebugLogUtility.WriteLog($"✅ OCR認識成功:");
                DebugLogUtility.WriteLog($"   📖 認識テキスト: '{originalText}'");
                DebugLogUtility.WriteLog($"   📊 平均信頼度: {ocrConfidence:F2}");
                DebugLogUtility.WriteLog($"   🔢 テキスト長: {originalText.Length}");
                DebugLogUtility.WriteLog($"   🔤 テキストがnullまたは空: {string.IsNullOrEmpty(originalText)}");
                DebugLogUtility.WriteLog($"   🔤 テキストが空白のみ: {string.IsNullOrWhiteSpace(originalText)}");
                    
                _logger?.LogDebug("OCR認識成功: テキスト長={Length}, 信頼度={Confidence:F2}", 
                    originalText.Length, ocrConfidence);
            }
            else
            {
                DebugLogUtility.WriteLog("❌ OCR処理でテキストが検出されませんでした");
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ OCR処理でテキストが検出されませんでした{Environment.NewLine}");
                _logger?.LogWarning("OCR処理でテキストが検出されませんでした");
                originalText = string.Empty;
            }

            // 翻訳処理
            PublishProgress(translationId, TranslationStatus.Translating, 0.7f, "翻訳中...");
            
            // 翻訳設定を取得
            var settings = GetTranslationSettings();
            
            // 🚨 CRITICAL DEBUG: originalTextの内容を確認
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化}{Environment.NewLine}");
            }
            catch { }
            
            string translatedText;
            if (!string.IsNullOrWhiteSpace(originalText))
            {
                try
                {
                    // 設定から言語ペアを取得
                    var sourceCode = settings.DefaultSourceLanguage ?? "ja";
                    var targetCode = settings.DefaultTargetLanguage ?? "en";
                    
                    // 🚨 [CRITICAL_DEBUG] 言語設定の実際の値をデバッグ出力
                    DebugLogUtility.WriteLog($"🚨 [LANGUAGE_SETTINGS_DEBUG] settings.DefaultSourceLanguage='{settings.DefaultSourceLanguage}'");
                    DebugLogUtility.WriteLog($"🚨 [LANGUAGE_SETTINGS_DEBUG] settings.DefaultTargetLanguage='{settings.DefaultTargetLanguage}'");
                    DebugLogUtility.WriteLog($"🚨 [LANGUAGE_SETTINGS_DEBUG] sourceCode='{sourceCode}', targetCode='{targetCode}'");
                    
                    DebugLogUtility.WriteLog($"🌍 翻訳開始: '{originalText}' ({sourceCode} → {targetCode})");
                    
                    // 改善されたMock翻訳処理（実際の翻訳ロジックをシミュレート）
                    DebugLogUtility.WriteLog($"🌍 改善された翻訳処理開始: '{originalText}' ({sourceCode} → {targetCode})");
                    
                    // 簡素な翻訳ロジックを実装
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false); // 少し短く
                    
                    if (sourceCode == "ja" && targetCode == "en")
                    {
                        // 日本語から英語への翻訳
                        translatedText = TranslateJapaneseToEnglish(originalText);
                    }
                    else if (sourceCode == "en" && targetCode == "ja")
                    {
                        // 英語から日本語への翻訳
                        translatedText = TranslateEnglishToJapanese(originalText);
                    }
                    else
                    {
                        // その他の言語ペア
                        translatedText = $"[{sourceCode}→{targetCode}] {originalText}";
                    }
                    
                    DebugLogUtility.WriteLog($"🌍 翻訳完了: '{translatedText}'");
                }
                catch (Exception translationEx)
                {
                    DebugLogUtility.WriteLog($"⚠️ 翻訳エラー: {translationEx.Message}");
                    _logger?.LogWarning(translationEx, "翻訳処理でエラーが発生しました");
                    translatedText = $"翻訳エラー: {translationEx.Message}";
                }
            }
            else
            {
                translatedText = "テキストが検出されませんでした";
            }

            // 完了
            PublishProgress(translationId, TranslationStatus.Completed, 1.0f, "翻訳完了");

            var processingTime = DateTime.UtcNow - startTime;

            // BaketaLogManagerで翻訳結果を構造化ログに記録
            try
            {
                var translationLogEntry = new TranslationResultLogEntry
                {
                    OperationId = translationId,
                    Engine = "OrchestrationService", // 実際の翻訳エンジン名を使用することも可能
                    LanguagePair = $"{settings.DefaultSourceLanguage ?? "ja"}-{settings.DefaultTargetLanguage ?? "en"}",
                    InputText = originalText,
                    OutputText = translatedText,
                    Confidence = ocrConfidence,
                    ProcessingTimeMs = processingTime.TotalMilliseconds,
                    InputTokenCount = originalText.Length,
                    OutputTokenCount = translatedText.Length,
                    CacheHit = false // 現在はキャッシュ機能未実装
                };
                
                BaketaLogManager.LogTranslationResult(translationLogEntry);
            }
            catch (Exception logEx)
            {
                _logger?.LogWarning(logEx, "翻訳結果の構造化ログ記録に失敗");
            }

            return new TranslationResult
            {
                Id = translationId,
                Mode = mode,
                OriginalText = originalText,
                TranslatedText = translatedText,
                DetectedLanguage = settings.DefaultSourceLanguage ?? "ja", // 実際の設定を使用
                TargetLanguage = settings.DefaultTargetLanguage ?? "en",   // 実際の設定を使用
                Confidence = (float)ocrConfidence,
                CapturedImage = null,    // 必要に応じて画像を保持
                ProcessingTime = processingTime
            };
        }
#pragma warning disable CA1031 // 翻訳処理のエラーハンドリングでアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            
            Console.WriteLine($"❌ 翻訳処理で例外発生:");
            Console.WriteLine($"   🔍 例外タイプ: {ex.GetType().Name}");
            Console.WriteLine($"   📝 例外メッセージ: {ex.Message}");
            Console.WriteLine($"   📍 スタックトレース: {ex.StackTrace}");
            
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 翻訳処理で例外発生:{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   🔍 例外タイプ: {ex.GetType().Name}{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📝 例外メッセージ: {ex.Message}{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📍 スタックトレース: {ex.StackTrace}{Environment.NewLine}");
            
            _logger?.LogError(ex, "翻訳処理で例外が発生しました: TranslationId={TranslationId}", translationId);
            
            // OCRエラーかその他のエラーかを分類
            bool isOcrError = ex.Message.Contains("PaddlePredictor") || 
                             ex.Message.Contains("OCR") ||
                             ex is OperationCanceledException;
            
            if (isOcrError)
            {
                DebugLogUtility.WriteLog($"🚫 OCRエラーのため翻訳結果を発行せず: ID={translationId}, Error={ex.Message}");
                
                // OCRエラーはステータス更新のみ行い、翻訳結果は発行しない
                PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"OCRエラー: {ex.Message}");
                
                // OCRエラーの場合は例外を再スローして、上位でキャッチさせる
                throw;
            }
            
            // その他のエラーの場合は従来通り翻訳結果として返す
            DebugLogUtility.WriteLog($"⚠️ 一般的な翻訳エラー、結果として発行: ID={translationId}, Error={ex.Message}");
            return new TranslationResult
            {
                Id = translationId,
                Mode = mode,
                OriginalText = string.Empty,
                TranslatedText = $"翻訳エラー: {ex.Message}",
                TargetLanguage = GetLanguageCode(_settingsService.GetValue("UI:TranslationLanguage", "英語")),
                Confidence = 0.0f,
                ProcessingTime = processingTime
            };
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 進行状況を発行
    /// </summary>
    private void PublishProgress(string id, TranslationStatus status, float progress, string? message = null)
    {
        var progressUpdate = new TranslationProgress
        {
            Id = id,
            Status = status,
            Progress = progress,
            Message = message
        };

        _progressUpdatesSubject.OnNext(progressUpdate);
        _statusChangesSubject.OnNext(status);
    }

    /// <summary>
    /// 日本語から英語への基本的な翻訳
    /// </summary>
    private static string TranslateJapaneseToEnglish(string text)
    {
        var result = text
            .Replace("こんにちは", "hello")
            .Replace("ありがとう", "thank you")
            .Replace("さようなら", "goodbye")
            .Replace("はい", "yes")
            .Replace("いいえ", "no")
            .Replace("すみません", "excuse me")
            .Replace("お疲れ様", "good job")
            .Replace("開始", "start")
            .Replace("終了", "end")
            .Replace("設定", "settings")
            .Replace("メニュー", "menu")
            .Replace("ファイル", "file")
            .Replace("編集", "edit")
            .Replace("表示", "view")
            .Replace("ツール", "tools")
            .Replace("ヘルプ", "help")
            .Replace("ゲーム", "game")
            .Replace("プレイ", "play")
            .Replace("スタート", "start")
            .Replace("ストップ", "stop")
            .Replace("ポーズ", "pause")
            .Replace("続行", "continue")
            .Replace("保存", "save")
            .Replace("読み込み", "load")
            .Replace("終了", "quit")
            .Replace("レベル", "level")
            .Replace("スコア", "score")
            .Replace("ライフ", "life")
            .Replace("ポイント", "point")
            .Replace("コイン", "coin")
            .Replace("アイテム", "item")
            .Replace("武器", "weapon")
            .Replace("防具", "armor")
            .Replace("マジック", "magic")
            .Replace("スキル", "skill")
            .Replace("キャラクター", "character")
            .Replace("プレイヤー", "player")
            .Replace("エネミー", "enemy")
            .Replace("ボス", "boss")
            .Replace("バトル", "battle")
            .Replace("戦闘", "fight")
            .Replace("勝利", "victory")
            .Replace("敗北", "defeat")
            .Replace("ゲームオーバー", "game over");
        return result;
    }
    
    /// <summary>
    /// 英語から日本語への基本的な翻訳
    /// </summary>
    private static string TranslateEnglishToJapanese(string text)
    {
        var result = text.ToLowerInvariant()
            .Replace("hello", "こんにちは")
            .Replace("thank you", "ありがとう")
            .Replace("goodbye", "さようなら")
            .Replace("yes", "はい")
            .Replace("no", "いいえ")
            .Replace("excuse me", "すみません")
            .Replace("good job", "お疲れ様")
            .Replace("start", "開始")
            .Replace("end", "終了")
            .Replace("settings", "設定")
            .Replace("menu", "メニュー")
            .Replace("file", "ファイル")
            .Replace("edit", "編集")
            .Replace("view", "表示")
            .Replace("tools", "ツール")
            .Replace("help", "ヘルプ")
            .Replace("game", "ゲーム")
            .Replace("play", "プレイ")
            .Replace("stop", "ストップ")
            .Replace("pause", "ポーズ")
            .Replace("continue", "続行")
            .Replace("save", "保存")
            .Replace("load", "読み込み")
            .Replace("quit", "終了")
            .Replace("level", "レベル")
            .Replace("score", "スコア")
            .Replace("life", "ライフ")
            .Replace("point", "ポイント")
            .Replace("coin", "コイン")
            .Replace("item", "アイテム")
            .Replace("weapon", "武器")
            .Replace("armor", "防具")
            .Replace("magic", "マジック")
            .Replace("skill", "スキル")
            .Replace("character", "キャラクター")
            .Replace("player", "プレイヤー")
            .Replace("enemy", "エネミー")
            .Replace("boss", "ボス")
            .Replace("battle", "バトル")
            .Replace("fight", "戦闘")
            .Replace("victory", "勝利")
            .Replace("defeat", "敗北")
            .Replace("game over", "ゲームオーバー");
        return result;
    }

    #endregion

    #region IDisposable 実装

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _logger?.LogDebug("TranslationOrchestrationServiceを破棄します");

        // 非同期停止を同期的に実行
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // Disposeメソッドでのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "サービス停止中にエラーが発生しました");
        }
#pragma warning restore CA1031

        // リソースを解放
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
        _disposeCts.Cancel();
#pragma warning restore CA1849
        _disposeCts.Dispose();
        
        _automaticTranslationCts?.Dispose();
        _singleTranslationSemaphore.Dispose();
        _ocrExecutionSemaphore.Dispose();
        _latestOcrRequestCts?.Dispose();
        
        _translationResultsSubject.Dispose();
        _statusChangesSubject.Dispose();
        _progressUpdatesSubject.Dispose();
        
        // 前回画像を安全に破棄
        lock (_previousImageLock)
        {
            _previousCapturedImage?.Dispose();
        }

        _disposed = true;
        
        _logger?.LogDebug("TranslationOrchestrationServiceを破棄しました");
    }

    #endregion

    #region デバッグ用メソッド

    /// <summary>
    /// デバッグ用に画像を保存します
    /// </summary>
    /// <param name="image">保存する画像</param>
    /// <param name="filePath">保存先ファイルパス</param>
    private async Task SaveImageForDebugAsync(IImage image, string filePath)
    {
        try
        {
            // IImageからバイト配列に変換
            byte[] imageBytes = await ConvertImageToBytesAsync(image).ConfigureAwait(false);
            
            // ファイルに保存
            await File.WriteAllBytesAsync(filePath, imageBytes).ConfigureAwait(false);
            
            DebugLogUtility.WriteLog($"✅ デバッグ画像保存完了: {filePath} (サイズ: {imageBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ デバッグ画像保存エラー: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// IImageをバイト配列に変換します
    /// </summary>
    /// <param name="image">変換する画像</param>
    /// <returns>バイト配列</returns>
    private async Task<byte[]> ConvertImageToBytesAsync(IImage image)
    {
        try
        {
            // IImageの実装によって変換方法が異なる可能性があるため、
            // 一般的な方法を試す
            
            // 方法1: ToByteArrayAsyncメソッドがある場合（WindowsImageAdapterでサポート）
            if (image is WindowsImageAdapter adapter)
            {
                DebugLogUtility.WriteLog($"🔄 WindowsImageAdapterから直接バイト配列を取得");
                return await adapter.ToByteArrayAsync().ConfigureAwait(false);
            }
            
            // 方法2: リフレクションでToByteArrayAsyncを呼び出し
            var imageType = image.GetType();
            var toByteArrayMethod = imageType.GetMethod("ToByteArrayAsync");
            if (toByteArrayMethod != null)
            {
                DebugLogUtility.WriteLog($"🔄 リフレクションでToByteArrayAsyncを呼び出し");
                if (toByteArrayMethod.Invoke(image, null) is Task<byte[]> task)
                {
                    return await task.ConfigureAwait(false);
                }
            }
            
            // 方法3: Streamプロパティがある場合
            var streamProperty = imageType.GetProperty("Stream");
            if (streamProperty != null)
            {
                if (streamProperty.GetValue(image) is Stream stream)
                {
                    DebugLogUtility.WriteLog($"🔄 Streamプロパティから変換");
                    using var memoryStream = new MemoryStream();
                    stream.Position = 0;
                    await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
                    return memoryStream.ToArray();
                }
            }
            
            // 方法4: Dataプロパティがある場合
            var dataProperty = imageType.GetProperty("Data");
            if (dataProperty != null)
            {
                if (dataProperty.GetValue(image) is byte[] data)
                {
                    DebugLogUtility.WriteLog($"🔄 Dataプロパティから変換");
                    return data;
                }
            }
            
            // 最後の手段: ToString()でデバッグ情報を取得
            var debugInfo = $"Image Debug Info: Type={imageType.Name}, Width={image.Width}, Height={image.Height}";
            DebugLogUtility.WriteLog($"⚠️ 画像バイト変換失敗 - {debugInfo}");
            return System.Text.Encoding.UTF8.GetBytes(debugInfo);
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ 画像バイト変換中にエラー: {ex.Message}");
            var errorInfo = $"Image Conversion Error: {ex.Message}, Type={image.GetType().Name}";
            return System.Text.Encoding.UTF8.GetBytes(errorInfo);
        }
    }

    #endregion
}

/// <summary>
/// バイト配列を持つ画像インターフェース
/// </summary>
public interface IImageBytes
{
    byte[] ToByteArray();
}

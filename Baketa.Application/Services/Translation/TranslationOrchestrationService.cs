using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Text;
using Baketa.Core.Models.Text;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.License.Models;
using Baketa.Core.Logging;
using Baketa.Core.Performance;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Language = Baketa.Core.Models.Translation.Language;
using Baketa.Infrastructure.Platform.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreOcrResult = Baketa.Core.Models.OCR.OcrResult;
using CoreTranslationSettings = Baketa.Core.Settings.TranslationSettings;
using TranslationService = Baketa.Core.Abstractions.Translation.ITranslationService;
using TranslationMode = Baketa.Core.Abstractions.Services.TranslationMode;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// [Issue #394] 自動翻訳ステップの実行結果
/// bool では「変化なし」と「クールダウン中」を区別できない問題を解決
/// </summary>
enum TranslationStepResult
{
    /// <summary>翻訳実行成功（インターバルリセット）</summary>
    Executed,
    /// <summary>画面変化なし（アダプティブ間隔を進行）</summary>
    NoChange,
    /// <summary>クールダウン中（状態維持、最短待機で再試行）</summary>
    InCooldown,
    /// <summary>エラー発生</summary>
    Error,
    /// <summary>[Issue #389] 対象ウィンドウが閉じられた（ループ停止）</summary>
    WindowClosed,
    /// <summary>[Issue #436] 翻訳をバックグラウンドにディスパッチ（ループは監視継続）</summary>
    TranslationDispatched,
    /// <summary>[Issue #436] 前回の翻訳がまだ実行中（新規ディスパッチ不可）</summary>
    TranslationInFlight
}

/// <summary>
/// 翻訳オーケストレーションサービス実装
/// キャプチャ、翻訳、UI表示の統合管理を担当
/// </summary>
public sealed class TranslationOrchestrationService : ITranslationOrchestrationService, INotifyPropertyChanged, IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly ISettingsService _settingsService;
    private readonly Baketa.Core.Abstractions.OCR.IOcrEngine _ocrEngine;
    private readonly CoordinateBasedTranslationService? _coordinateBasedTranslation;
    private readonly IEventAggregator _eventAggregator;
    private readonly TranslationService _translationService;
    private readonly IOptionsMonitor<Baketa.Core.Settings.OcrSettings> _ocrSettings;
    private readonly ITranslationDictionaryService? _translationDictionaryService;
    private readonly ILogger<TranslationOrchestrationService>? _logger;

    // Issue #290: Fork-Join並列実行用（OCR || Cloud AI翻訳）
    private readonly IFallbackOrchestrator? _fallbackOrchestrator;
    private readonly ILicenseManager? _licenseManager;

    // Issue #293: 投機的OCRサービス（Shot翻訳応答時間短縮）
    private readonly ISpeculativeOcrService? _speculativeOcrService;

    // [Issue #389] ウィンドウ存在チェック用
    private readonly Baketa.Core.Abstractions.Platform.Windows.Adapters.IWindowManagerAdapter? _windowManagerAdapter;

    // [Issue #410] テキスト変化検知キャッシュリセット用
    private readonly Baketa.Core.Abstractions.Processing.ITextChangeDetectionService? _textChangeDetectionService;

    // [Issue #525] Detection-Only キャッシュ・画像変化検知キャッシュリセット用
    private readonly IDetectionBoundsCache? _detectionBoundsCache;
    private readonly Baketa.Core.Abstractions.Services.IImageChangeDetectionService? _imageChangeDetectionService;

    // [Issue #415] Cloud翻訳キャッシュ
    private readonly ICloudTranslationCache? _cloudTranslationCache;

    // ONNXモデル オンデマンドロード/アンロード用
    private readonly IUnifiedSettingsService? _unifiedSettingsService;

    // 状態管理
    private volatile bool _isAutomaticTranslationActive;
    private volatile bool _isSingleTranslationActive;

    // 実行制御
    private CancellationTokenSource? _automaticTranslationCts;
    private Task? _automaticTranslationTask;
    private readonly object _ctsLock = new object(); // Phase 3.3: CTS管理のThread-safe保護
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

    // [Issue #436] 非ブロッキング翻訳ディスパッチ
    private Task? _translationInFlightTask;
    private readonly object _translationInFlightLock = new();

    // [Issue #299] アダプティブ間隔: 画像変化なし時の間隔延長
    /// <summary>連続「変化なし」カウント</summary>
    private int _consecutiveNoChangeCount;
    /// <summary>アダプティブ間隔用ロック</summary>
    private readonly object _adaptiveIntervalLock = new();

    /// <summary>アダプティブ間隔設定（連続無変化回数 → 待機時間、降順で定義）</summary>
    private static readonly (int threshold, TimeSpan interval)[] AdaptiveIntervals =
    [
        (6, TimeSpan.FromSeconds(60)),   // 6回以上: 60秒
        (3, TimeSpan.FromSeconds(20)),   // 3-5回: 20秒
        (0, TimeSpan.FromSeconds(10)),   // 0-2回: 10秒（デフォルト）
    ];

    // [Issue #392] テキスト消失高速検知: 翻訳完了後の短間隔チェックモード
    /// <summary>高速チェック残り回数</summary>
    private int _postTranslationRapidCheckRemaining;
    /// <summary>高速チェック間隔（テキスト消失を素早く検知するため）</summary>
    private static readonly TimeSpan PostTranslationRapidCheckInterval = TimeSpan.FromSeconds(1.5);
    /// <summary>高速チェック最大回数（超過後は通常のアダプティブ間隔に復帰）</summary>
    private const int PostTranslationRapidCheckCount = 15; // 1.5s × 15 = 最大22.5秒間の高速監視

    #region コンストラクタ

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="captureService">キャプチャサービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="ocrEngine">OCRエンジン</param>
    /// <param name="coordinateBasedTranslation">座標ベース翻訳サービス</param>
    /// <param name="eventAggregator">イベント集約サービス</param>
    /// <param name="ocrSettings">OCR設定</param>
    /// <param name="translationService">翻訳サービス</param>
    /// <param name="translationDictionaryService">翻訳辞書サービス（オプショナル）</param>
    /// <param name="fallbackOrchestrator">フォールバックオーケストレーター（Issue #290: Fork-Join用）</param>
    /// <param name="licenseManager">ライセンスマネージャー（Issue #290: Cloud AI利用可否判定用）</param>
    /// <param name="speculativeOcrService">投機的OCRサービス（Issue #293: Shot翻訳応答時間短縮）</param>
    /// <param name="logger">ロガー</param>
    public TranslationOrchestrationService(
        ICaptureService captureService,
        ISettingsService settingsService,
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        CoordinateBasedTranslationService? coordinateBasedTranslation,
        IEventAggregator eventAggregator,
        IOptionsMonitor<Baketa.Core.Settings.OcrSettings> ocrSettings,
        TranslationService translationService,
        ITranslationDictionaryService? translationDictionaryService = null,
        IFallbackOrchestrator? fallbackOrchestrator = null,
        ILicenseManager? licenseManager = null,
        ISpeculativeOcrService? speculativeOcrService = null,
        Baketa.Core.Abstractions.Platform.Windows.Adapters.IWindowManagerAdapter? windowManagerAdapter = null,
        Baketa.Core.Abstractions.Processing.ITextChangeDetectionService? textChangeDetectionService = null,
        ICloudTranslationCache? cloudTranslationCache = null, // [Issue #415] Cloud翻訳キャッシュ
        IUnifiedSettingsService? unifiedSettingsService = null, // ONNXモデル オンデマンドロード/アンロード
        IDetectionBoundsCache? detectionBoundsCache = null, // [Issue #525] Detection-Onlyキャッシュ
        Baketa.Core.Abstractions.Services.IImageChangeDetectionService? imageChangeDetectionService = null, // [Issue #525] 画像変化検知キャッシュ
        ILogger<TranslationOrchestrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(ocrEngine);
        ArgumentNullException.ThrowIfNull(eventAggregator);
        ArgumentNullException.ThrowIfNull(ocrSettings);

        _captureService = captureService;
        _settingsService = settingsService;
        _ocrEngine = ocrEngine;
        _coordinateBasedTranslation = coordinateBasedTranslation;
        _eventAggregator = eventAggregator;
        _ocrSettings = ocrSettings;
        _translationService = translationService;
        _translationDictionaryService = translationDictionaryService;
        _fallbackOrchestrator = fallbackOrchestrator;
        _licenseManager = licenseManager;
        _speculativeOcrService = speculativeOcrService;
        _windowManagerAdapter = windowManagerAdapter;
        _textChangeDetectionService = textChangeDetectionService;
        _cloudTranslationCache = cloudTranslationCache; // [Issue #415] Cloud翻訳キャッシュ
        _unifiedSettingsService = unifiedSettingsService;
        _detectionBoundsCache = detectionBoundsCache; // [Issue #525] Detection-Onlyキャッシュ
        _imageChangeDetectionService = imageChangeDetectionService; // [Issue #525] 画像変化検知キャッシュ
        _logger = logger;

        // 設定変更時にONNXモデルのロード/アンロードをトリガー
        if (_unifiedSettingsService != null)
        {
            _unifiedSettingsService.SettingsChanged += OnTranslationSettingsChanged;
        }

        // Issue #293: 投機的OCRサービスが利用可能かログ出力
        if (_speculativeOcrService?.IsEnabled == true)
        {
            _logger?.LogInformation("🚀 [Issue #293] 投機的OCRサービスが有効です - Shot翻訳応答時間短縮機能が利用可能");
        }

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
    public TranslationMode CurrentMode => _isAutomaticTranslationActive ? TranslationMode.Live : TranslationMode.Singleshot;

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
        // 🔥🔥🔥 [ULTRA_DEBUG] メソッド呼び出しの絶対最初にファイル直接書き込み
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [AUTO_TRANSLATE_START] StartAutomaticTranslationAsync開始{Environment.NewLine}");
        }
        catch { /* ログ失敗は無視 */ }

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
        _logger?.LogDebug($"🎬 StartAutomaticTranslationAsync呼び出し - this={this.GetType().FullName}@{this.GetHashCode()}");
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

            _logger?.LogDebug($"🎬 StartAutomaticTranslationAsync呼び出し");
            _logger?.LogDebug($"   🗑️ Disposed: {_disposed.ToString(CultureInfo.InvariantCulture)}");
            _logger?.LogDebug($"   🔄 すでにアクティブ: {_isAutomaticTranslationActive.ToString(CultureInfo.InvariantCulture)}");
            _logger?.LogDebug($"   🎯 対象ウィンドウハンドル: {(targetWindowHandle?.ToString(CultureInfo.InvariantCulture) ?? "null (画面全体)")}");

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
                _logger?.LogDebug($"⚠️ 自動翻訳は既に実行中です");
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

            _logger?.LogDebug($"🎬 自動翻訳を開始します");

            // 緊急デバッグ: _logger?.LogDebug後
            try
            {
                // 🔥 [FILE_CONFLICT_FIX_ORCHESTRATION_11] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [DEBUG] _logger?.LogDebug後");
            }
            catch { }

            _logger?.LogInformation("自動翻訳を開始します");

            // Phase 3.3: CancellationTokenSource完全刷新（Stop→Start Token競合解決）
            lock (_ctsLock)
            {
                // 🔥 CRITICAL FIX: 古いCTSの即座完全破棄
                var oldCts = _automaticTranslationCts;
                if (oldCts != null)
                {
                    try
                    {
                        oldCts.Cancel();
                        _logger?.LogDebug("🔧 [PHASE3.3_FIX] 古いCTS Cancel完了");
                    }
                    catch (ObjectDisposedException)
                    {
                        // 既に破棄済みの場合は無視
                        _logger?.LogDebug("🔧 [PHASE3.3_FIX] 古いCTSは既に破棄済み");
                    }
                    finally
                    {
                        oldCts.Dispose();
                        _logger?.LogDebug("🔧 [PHASE3.3_FIX] 古いCTS Dispose完了");
                    }
                }

                // 🚀 新CTS即座生成
                _automaticTranslationCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _disposeCts.Token);
                _logger?.LogDebug("🔧 [PHASE3.3_FIX] 新CTS生成完了 - Hash: {TokenHash}", _automaticTranslationCts.Token.GetHashCode());
            }

            // 🔒 [NULL_REF_FIX] ラムダ実行中にフィールドがnullになる競合を防ぐため、トークンをローカル変数にキャプチャ
            CancellationToken automaticToken = _automaticTranslationCts.Token;

            _isAutomaticTranslationActive = true;
            OnPropertyChanged(nameof(IsAnyTranslationActive));

            // [Issue #299] アダプティブ間隔カウンタをリセット
            lock (_adaptiveIntervalLock)
            {
                _consecutiveNoChangeCount = 0;
                _postTranslationRapidCheckRemaining = 0; // [Issue #392]
            }

            // [Issue #525] 翻訳開始時に全Detectionキャッシュをリセット（Shot→Live遷移時の誤判定防止）
            ClearAllDetectionCaches();
            _coordinateBasedTranslation?.ResetTranslationState();

            // バックグラウンドタスクで自動翻訳を実行
            try
            {
                // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 Task.Run開始前（直接書き込み）{Environment.NewLine}");
                // 診断システム実装により debug_app_logs.txt への出力を無効化
            }
            catch { }

            _logger?.LogDebug($"🎬 Task.Run開始前");
            _automaticTranslationTask = Task.Run(async () =>
            {
                try
                {
                    // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 Task.Run内部開始（直接書き込み）{Environment.NewLine}");
                    // 診断システム実装により debug_app_logs.txt への出力を無効化
                }
                catch { }

                _logger?.LogDebug($"🎬 Task.Run内部開始");

                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }

                try
                {
                    await ExecuteAutomaticTranslationLoopAsync(automaticToken).ConfigureAwait(false);

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

                    _logger?.LogDebug($"💥 ExecuteAutomaticTranslationLoopAsync例外: {ex.Message}");
                    _logger?.LogError(ex, "自動翻訳ループで予期しないエラーが発生しました");
                    throw;
                }

                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch { }

                _logger?.LogDebug($"🎬 Task.Run内部終了");
            }, automaticToken);

            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }

            _logger?.LogDebug($"🎬 Task.Run開始後");

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
            _logger?.LogDebug($"💥 StartAutomaticTranslationAsync例外: {ex.GetType().Name}: {ex.Message}");
            _logger?.LogDebug($"💥 スタックトレース: {ex.StackTrace}");
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
            // Phase 3.3: 即座CTS完全破棄（finally待機なし）
            CancellationTokenSource? ctsToDispose = null;
            lock (_ctsLock)
            {
                ctsToDispose = _automaticTranslationCts;
                _automaticTranslationCts = null; // 即座null設定でStart競合防止
            }

            // Lock外で即座Cancel + Dispose
            if (ctsToDispose != null)
            {
                try
                {
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
                    ctsToDispose.Cancel();
#pragma warning restore CA1849
                    _logger?.LogDebug("🔧 [PHASE3.3_STOP] CTS Cancel完了");
                }
                catch (ObjectDisposedException)
                {
                    _logger?.LogDebug("🔧 [PHASE3.3_STOP] CTSは既に破棄済み");
                }
                finally
                {
                    ctsToDispose.Dispose();
                    _logger?.LogDebug("🔧 [PHASE3.3_STOP] CTS Dispose完了 - 即座実行");
                }
            }

            // タスクの完了を待機（タイムアウト付き）
            if (_automaticTranslationTask != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await _automaticTranslationTask.WaitAsync(combinedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 外部からのキャンセルは再スロー（最優先で処理）
                    _logger?.LogDebug("自動翻訳の停止が外部からキャンセルされました");
                    throw;
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    _logger?.LogWarning("自動翻訳の停止がタイムアウトしました");
                }
                catch (OperationCanceledException)
                {
                    // その他のキャンセル（内部タスクのキャンセルなど）は正常な停止操作
                    _logger?.LogDebug("自動翻訳タスクが正常にキャンセルされました");
                }
            }

            // [Issue #436] インフライト翻訳タスクの完了を待機
            Task? inFlightTask;
            lock (_translationInFlightLock)
            {
                inFlightTask = _translationInFlightTask;
            }

            if (inFlightTask is { IsCompleted: false })
            {
                try
                {
                    await inFlightTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                    _logger?.LogDebug("[Issue #436] インフライト翻訳タスクが正常に完了しました");
                }
                catch (TimeoutException)
                {
                    _logger?.LogWarning("[Issue #436] インフライト翻訳タスクの待機がタイムアウトしました");
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogDebug("[Issue #436] インフライト翻訳タスクの待機がキャンセルされました");
                }
            }

            lock (_translationInFlightLock)
            {
                _translationInFlightTask = null;
            }
        }
        finally
        {
            // Phase 3.3: 重複Dispose削除（既に即座Dispose済み）
            lock (_ctsLock)
            {
                _automaticTranslationCts = null; // 安全のため再設定
            }
            _automaticTranslationTask = null;
            _isAutomaticTranslationActive = false;
            OnPropertyChanged(nameof(IsAnyTranslationActive));

            _logger?.LogDebug("🔧 [PHASE3.3_STOP] Stop完了 - Token競合解決済み");

            // [Issue #525] 翻訳停止時に全Detectionキャッシュをクリア（デフォルト状態復帰）
            ClearAllDetectionCaches();
            _coordinateBasedTranslation?.ResetTranslationState();

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
            //     new TranslationModeChangedEvent(TranslationMode.Singleshot, TranslationMode.Live))
            //     .ConfigureAwait(false);

            _logger?.LogInformation("自動翻訳を停止しました");

            // Live翻訳停止後、Cloudモードが有効ならONNXモデルをアンロード
            await TryUnloadOnnxModelsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Cloudモード有効時にONNXモデルをアンロードしてメモリを解放
    /// </summary>
    private async Task TryUnloadOnnxModelsAsync()
    {
        try
        {
            // ローカル翻訳モードならアンロードしない
            if (IsUseLocalEngine())
            {
                return;
            }

            // 利用可能なエンジンからアンロード可能なものを探してアンロード
            var unloadableEngines = _translationService.GetAvailableEngines()
                .OfType<IUnloadableTranslationEngine>();

            foreach (var engine in unloadableEngines)
            {
                await engine.UnloadModelsAsync().ConfigureAwait(false);
                _logger?.LogInformation("Live翻訳停止 + Cloudモード → {EngineName} モデルをアンロード", engine.Name);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ONNXモデルアンロードでエラー（無視して続行）");
        }
    }

    /// <summary>
    /// 翻訳設定変更時にONNXモデルのロード/アンロードをトリガー
    /// </summary>
    private void OnTranslationSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.SettingsType != SettingsType.Translation)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var useLocal = _unifiedSettingsService!.GetTranslationSettings().UseLocalEngine;

                if (useLocal)
                {
                    // [Issue #542] テキスト翻訳フォールバック設定済みならNLLBプリロードをスキップ
                    if (_translationService.HasTextTranslationFallback)
                    {
                        _logger?.LogInformation("[Issue #542] ローカルモード切替: テキスト翻訳フォールバック設定済み → NLLBプリロードスキップ");
                    }
                    else
                    {
                        var engine = _translationService.GetAvailableEngines()
                            .OfType<IUnloadableTranslationEngine>()
                            .FirstOrDefault();

                        if (engine != null)
                        {
                            var isReady = await ((ITranslationEngine)engine).IsReadyAsync().ConfigureAwait(false);
                            if (!isReady)
                            {
                                _logger?.LogInformation("ローカルモードに切替 → ONNXモデルをプリロード開始");
                                await ((ITranslationEngine)engine).InitializeAsync().ConfigureAwait(false);
                            }
                        }
                    }
                }
                else
                {
                    // Cloudモードに切替 → ONNXモデルをアンロード
                    await TryUnloadOnnxModelsForCloudModeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "設定変更時のONNXモデルロード/アンロードでエラー（無視して続行）");
            }
        });
    }

    /// <summary>
    /// Cloudモード切替時にONNXモデルをアンロード（Live翻訳中でなければ）
    /// </summary>
    private async Task TryUnloadOnnxModelsForCloudModeAsync()
    {
        // Live翻訳中はアンロードしない（停止時にアンロードされる）
        if (_isAutomaticTranslationActive)
        {
            _logger?.LogDebug("Live翻訳中のためONNXモデルアンロードを延期");
            return;
        }

        var engines = _translationService.GetAvailableEngines()
            .OfType<IUnloadableTranslationEngine>();

        foreach (var engine in engines)
        {
            await engine.UnloadModelsAsync().ConfigureAwait(false);
            _logger?.LogInformation("Cloudモードに切替 → {EngineName} モデルをアンロード", engine.Name);
        }
    }

    /// <summary>
    /// translation-settings.json から UseLocalEngine を読み取る
    /// </summary>
    private static bool IsUseLocalEngine()
    {
        try
        {
            var settingsPath = BaketaSettingsPaths.TranslationSettingsPath;
            if (!File.Exists(settingsPath))
            {
                return true; // デフォルトはローカル
            }

            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("useLocalEngine", out var value))
            {
                return value.GetBoolean();
            }

            return true; // プロパティ未設定時はローカル
        }
        catch
        {
            return true; // エラー時はローカルと見なしてアンロードしない
        }
    }

    /// <inheritdoc />
    public async Task TriggerSingleTranslationAsync(IntPtr? targetWindowHandle = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 🔍 [DEBUG] セマフォ待機前の状態をログ
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger?.LogInformation("🔒 [SEMAPHORE] 単発翻訳リクエスト開始: ID={RequestId}, CurrentCount={Count}",
            requestId, _singleTranslationSemaphore.CurrentCount);

        // 翻訳対象ウィンドウハンドルを保存
        _targetWindowHandle = targetWindowHandle;

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCts.Token);

        // セマフォを使用して同時実行を制御
        _logger?.LogDebug("🔒 [SEMAPHORE] セマフォ取得待機中: ID={RequestId}", requestId);
        await _singleTranslationSemaphore.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        _logger?.LogInformation("🔓 [SEMAPHORE] セマフォ取得完了: ID={RequestId}", requestId);

        try
        {
            if (_isSingleTranslationActive)
            {
                _logger?.LogWarning("単発翻訳は既に実行中です: ID={RequestId}", requestId);
                return;
            }

            _isSingleTranslationActive = true;
            OnPropertyChanged(nameof(IsAnyTranslationActive));

            // [Issue #525] Singleshot翻訳開始時に全Detectionキャッシュをクリア
            // 前回のDetection-Onlyキャッシュが残るとDetectionOnlySkipped=trueで翻訳がスキップされる
            ClearAllDetectionCaches();

            _logger?.LogInformation("単発翻訳を実行します: ID={RequestId}", requestId);

            // TODO: 翻訳実行イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(new TranslationTriggeredEvent(TranslationMode.Singleshot))
            //     .ConfigureAwait(false);

            // 単発翻訳を実行
            await ExecuteSingleTranslationAsync(combinedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _isSingleTranslationActive = false;
            OnPropertyChanged(nameof(IsAnyTranslationActive));
            _singleTranslationSemaphore.Release();
            _logger?.LogInformation("🔓 [SEMAPHORE] セマフォ解放完了: ID={RequestId}", requestId);
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

    #region プライベートメソッド

    /// <summary>
    /// [Issue #525] 全Detection/画像変化検知キャッシュをクリア
    /// デフォルト状態（翻訳モード=None）に戻る際に呼び出す。
    /// Detection-Onlyフィルタのバウンディングボックスキャッシュ、画像変化検知キャッシュ、
    /// テキスト変化検知キャッシュをすべてリセットし、次回翻訳時にフレッシュな検出を保証する。
    /// </summary>
    private void ClearAllDetectionCaches()
    {
        _textChangeDetectionService?.ClearAllPreviousTexts();
        _textChangeDetectionService?.ClearStaticUiMarkers();
        _detectionBoundsCache?.ClearAll();
        _imageChangeDetectionService?.ClearCache();
        _logger?.LogDebug("[Issue #525] 全Detection/画像変化検知キャッシュをクリア");
    }

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
            OptimizationLevel = 2,
            // 🔥 [PHASE_K-29-F] ROIScaleFactor明示的設定（Gemini推奨値: 0.5）
            // 問題: デフォルト値0.25が使用され、CaptureModels.csの変更が反映されない
            // 解決策: 明示的に0.5を設定し、1920x1080でのテキスト検出精度向上
            ROIScaleFactor = 0.5f // デフォルト0.25 → 0.5（960x540 → 1920x1080）
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
        // 🔥 [Issue #189] translation-settings.jsonから直接読み取り
        // UIはselectedLanguagePairとして保存（例: "en-ja"）
        var sourceLanguageCode = "en"; // デフォルト値（TranslationSettingsと一致）
        var targetLanguageCode = "ja"; // デフォルト値（TranslationSettingsと一致）

        try
        {
            // [Issue #459] BaketaSettingsPaths経由に統一
            var translationSettingsPath = BaketaSettingsPaths.TranslationSettingsPath;

            if (File.Exists(translationSettingsPath))
            {
                var json = File.ReadAllText(translationSettingsPath);
                using var doc = JsonDocument.Parse(json);

                // 🔥 [Issue #189] UIが保存する"selectedLanguagePair"プロパティを読み取る
                // 形式: "en-ja", "ja-en" など
                if (doc.RootElement.TryGetProperty("selectedLanguagePair", out var languagePairElement))
                {
                    var languagePair = languagePairElement.GetString();
                    if (!string.IsNullOrEmpty(languagePair) && languagePair.Contains('-'))
                    {
                        var parts = languagePair.Split('-', 2);
                        if (parts.Length == 2)
                        {
                            sourceLanguageCode = parts[0].Trim().ToLowerInvariant();
                            targetLanguageCode = parts[1].Trim().ToLowerInvariant();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [TRANSLATION_SETTINGS_DEBUG] JSON読み取り失敗: {ex.Message}");
        }

        // 🔥 [Issue #189] デバッグ出力（言語コードのみ表示）
        Console.WriteLine($"🌍 [LANGUAGE_SETTING] 設定ファイルから読み取り: {sourceLanguageCode} → {targetLanguageCode}");
        _logger?.LogDebug("🌍 翻訳言語設定取得: {SourceCode} → {TargetCode}", sourceLanguageCode, targetLanguageCode);

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
    /// [Issue #436] 完了したバックグラウンド翻訳タスクを収穫し、_translationInFlightTask をクリアする。
    /// </summary>
    /// <returns>翻訳が正常完了した場合 true（RapidCheckMode起動用）</returns>
    private bool HarvestCompletedTranslation()
    {
        lock (_translationInFlightLock)
        {
            if (_translationInFlightTask is null or { IsCompleted: false })
                return false;

            var task = _translationInFlightTask;
            _translationInFlightTask = null;

            if (task.IsCompletedSuccessfully)
            {
                _logger?.LogDebug("[Issue #436] インフライト翻訳が正常完了");
                return true;
            }

            // Faulted or Canceled
            if (task.IsFaulted)
            {
                _logger?.LogWarning(task.Exception?.InnerException,
                    "[Issue #436] インフライト翻訳がエラーで完了");
            }
            else if (task.IsCanceled)
            {
                _logger?.LogDebug("[Issue #436] インフライト翻訳がキャンセルで完了");
            }

            return false;
        }
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
        var minInterval = TimeSpan.FromMilliseconds(200); // [Issue #435] 最小間隔を200msに緩和（キャプチャ+ハッシュ比較は高速）
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

                    // [Issue #436] 完了済みインフライト翻訳を収穫
                    bool translationJustCompleted = HarvestCompletedTranslation();

                    // 自動翻訳を実行
                    var stepResult = await ExecuteAutomaticTranslationStepAsync(cancellationToken).ConfigureAwait(false);

                    // [Issue #389] 対象ウィンドウが閉じられた場合はループ終了
                    if (stepResult == TranslationStepResult.WindowClosed)
                    {
                        _logger?.LogInformation("[Issue #389] 対象ウィンドウが閉じられたため自動翻訳ループを停止します");
                        break;
                    }

                    // [Issue #394] InCooldown時の残りクールダウン計算（ロック順序を守るため先に取得）
                    TimeSpan remainingCooldown = TimeSpan.Zero;
                    if (stepResult == TranslationStepResult.InCooldown)
                    {
                        DateTime lastTime;
                        lock (_lastTranslationTimeLock)
                        {
                            lastTime = _lastTranslationCompletedAt;
                        }
                        var cooldownSec = _settingsService.GetValue("Translation:PostTranslationCooldownSeconds", 3);
                        var elapsed = DateTime.UtcNow - lastTime;
                        remainingCooldown = TimeSpan.FromSeconds(Math.Max(0, cooldownSec - elapsed.TotalSeconds));
                    }

                    // [Issue #299][Issue #394] アダプティブ間隔の計算
                    var actualInterval = interval;
                    lock (_adaptiveIntervalLock)
                    {
                        switch (stepResult)
                        {
                            case TranslationStepResult.Executed:
                                // 翻訳実行成功 → カウンタリセット
                                _consecutiveNoChangeCount = 0;

                                if (_postTranslationRapidCheckRemaining <= 0)
                                {
                                    // [Issue #392] 高速チェックモード開始: テキスト消失を素早く検知するため
                                    _postTranslationRapidCheckRemaining = PostTranslationRapidCheckCount;
                                    var cooldownSeconds = _settingsService.GetValue("Translation:PostTranslationCooldownSeconds", 3);
                                    actualInterval = TimeSpan.FromSeconds(cooldownSeconds + 0.5);

                                    _logger?.LogDebug(
                                        "[Issue #392] 高速チェックモード開始: 初回={InitialInterval:F1}s, 以降={RapidInterval:F1}s × {Count}回",
                                        actualInterval.TotalSeconds,
                                        PostTranslationRapidCheckInterval.TotalSeconds,
                                        PostTranslationRapidCheckCount);
                                }
                                // else: 高速チェック中に翻訳成功 → 再起動しない（無限ループ防止）
                                break;

                            case TranslationStepResult.InCooldown:
                                // [Issue #436] クールダウン中はキャプチャ・変化検知をスキップ（GridHashCache保全）
                                // 残りクールダウン時間に基づいて最適な間隔を設定
                                // NoChangeCountを凍結（増減なし）、高速チェックカウンタも消費しない
                                actualInterval = remainingCooldown.TotalSeconds > 0.5
                                    ? TimeSpan.FromMilliseconds(300) // 変化検知 + クールダウン消化
                                    : TimeSpan.FromMilliseconds(100); // クールダウン残りわずか → 即座に翻訳開始

                                _logger?.LogDebug(
                                    "[Issue #435] クールダウン中（変化検知継続）: NoChangeCount={Count}, RemainingCooldown={Remaining:F1}s, NextInterval={Interval}ms",
                                    _consecutiveNoChangeCount,
                                    remainingCooldown.TotalSeconds,
                                    actualInterval.TotalMilliseconds);
                                break;

                            case TranslationStepResult.TranslationDispatched:
                                // [Issue #436] 翻訳をバックグラウンドにディスパッチ → 監視継続
                                _consecutiveNoChangeCount = 0;
                                actualInterval = TimeSpan.FromMilliseconds(300);
                                _logger?.LogDebug(
                                    "[Issue #436] 翻訳ディスパッチ完了、監視継続: Interval=300ms");
                                break;

                            case TranslationStepResult.TranslationInFlight:
                                // [Issue #436] 前回の翻訳がまだ実行中 → 高速ポーリングで監視継続
                                actualInterval = TimeSpan.FromMilliseconds(300);
                                break;

                            case TranslationStepResult.NoChange:
                            case TranslationStepResult.Error:
                                if (_postTranslationRapidCheckRemaining > 0)
                                {
                                    // [Issue #392] 高速チェックモード中: 短間隔でテキスト消失を監視
                                    _postTranslationRapidCheckRemaining--;
                                    actualInterval = PostTranslationRapidCheckInterval;
                                    _consecutiveNoChangeCount = 0;

                                    _logger?.LogDebug(
                                        "[Issue #392] 高速チェック中: Remaining={Remaining}, Interval={Interval:F1}s, StepResult={Result}",
                                        _postTranslationRapidCheckRemaining,
                                        actualInterval.TotalSeconds,
                                        stepResult);
                                }
                                else
                                {
                                    // [Issue #436] InFlightタスクが存在する場合は300msで監視継続（Adaptive抑制）
                                    // ステップ関数はInFlight中にTranslationInFlightを直接返すが、
                                    // 完了済み未Harvestのタスクが残っている場合にNoChangeで到達しうる
                                    // → 次サイクルで即座にHarvest → RapidCheck起動
                                    bool isInFlight;
                                    lock (_translationInFlightLock)
                                    {
                                        isInFlight = _translationInFlightTask is not null;
                                    }

                                    if (isInFlight)
                                    {
                                        actualInterval = TimeSpan.FromMilliseconds(300);
                                        _logger?.LogDebug(
                                            "[Issue #436] InFlight中のため監視継続: Interval=300ms, NoChangeCount={Count}",
                                            _consecutiveNoChangeCount);
                                    }
                                    else
                                    {
                                        // 高速チェック完了後 → カウンタ増加、アダプティブ間隔を適用
                                        _consecutiveNoChangeCount++;

                                        // 閾値に基づいてアダプティブ間隔を選択（降順定義のため最初のマッチを使用）
                                        foreach (var (threshold, adaptiveInterval) in AdaptiveIntervals)
                                        {
                                            if (_consecutiveNoChangeCount >= threshold)
                                            {
                                                actualInterval = adaptiveInterval;
                                                break;
                                            }
                                        }

                                        _logger?.LogDebug(
                                            "[Issue #299] Adaptive interval: NoChangeCount={Count}, Interval={Interval}s",
                                            _consecutiveNoChangeCount,
                                            actualInterval.TotalSeconds);
                                    }
                                }
                                break;
                        }
                    }

                    // [Issue #436] インフライト翻訳が完了した場合、RapidCheckModeを起動
                    // ただし、同じサイクルで新しい翻訳がディスパッチされた場合はスキップ
                    // （監視継続の300msインターバルで上書きしてしまうため）
                    if (translationJustCompleted && _postTranslationRapidCheckRemaining <= 0
                        && stepResult != TranslationStepResult.TranslationDispatched)
                    {
                        _postTranslationRapidCheckRemaining = PostTranslationRapidCheckCount;
                        // [Issue #436] クールダウンはディスパッチ時点で起点設定済みのため、
                        // Harvest時点では既に経過している → 即座にチェック開始
                        actualInterval = TimeSpan.FromMilliseconds(300);

                        _logger?.LogDebug(
                            "[Issue #436] インフライト翻訳完了 → 高速チェックモード開始: 初回={InitialInterval}ms",
                            actualInterval.TotalMilliseconds);
                    }

                    // 次の実行まで待機
                    try
                    {
                        await Task.Delay(actualInterval, cancellationToken).ConfigureAwait(false);
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
    /// <returns>[Issue #394] 実行結果を示すenum（Executed/NoChange/InCooldown/Error）</returns>
    private async Task<TranslationStepResult> ExecuteAutomaticTranslationStepAsync(CancellationToken cancellationToken)
    {
        // 緊急デバッグ: ExecuteAutomaticTranslationStepAsync開始確認
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch { }

        var translationId = Guid.NewGuid().ToString("N")[..8];
        _logger?.LogDebug($"🎯 自動翻訳ステップ開始: ID={translationId}");
        _logger?.LogDebug($"   ⏱️ 開始時キャンセル要求: {cancellationToken.IsCancellationRequested}");
        _logger?.LogDebug($"   📡 CaptureServiceが利用可能: {_captureService != null}");

        // [Issue #436] InFlight/Cooldownチェックはキャプチャ・変化検知の前に実行。
        // EnhancedImageChangeDetectionServiceの内部GridHashCacheは毎回無条件更新されるため、
        // InFlight/Cooldown中に変化検知を実行するとテキスト変化がキャッシュに吸収される。

        IImage? currentImage = null;
        var captureStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // [Issue #389] ウィンドウ存在チェック（キャプチャ前）
            // キャプチャパイプラインはフォールバック（WGC → GDI）で常にnon-null画像を返すため、
            // キャプチャ前に明示的にウィンドウの存在を確認する
            if (_targetWindowHandle.HasValue && _windowManagerAdapter != null)
            {
                var bounds = _windowManagerAdapter.GetWindowBounds(_targetWindowHandle.Value);
                if (bounds == null)
                {
                    _logger?.LogInformation("[Issue #389] 対象ウィンドウが閉じられました: Handle=0x{Handle:X}",
                        _targetWindowHandle.Value.ToInt64());

                    await _eventAggregator.PublishAsync(new CaptureFailedEvent(
                        System.Drawing.Rectangle.Empty,
                        new InvalidOperationException($"Target window has been closed: Handle=0x{_targetWindowHandle.Value.ToInt64():X}"),
                        "Target window closed")).ConfigureAwait(false);

                    return TranslationStepResult.WindowClosed;
                }
            }

            // [Issue #436] InFlightチェック（キャプチャ・変化検知前）
            // GridHashCache保全: InFlight中は検知をスキップし、完了後に正しく変化を検出
            lock (_translationInFlightLock)
            {
                if (_translationInFlightTask is { IsCompleted: false })
                {
                    _logger?.LogDebug(
                        "[Issue #436] 翻訳インフライト中、キャプチャ・検知スキップ: ID={TranslationId}",
                        translationId);
                    return TranslationStepResult.TranslationInFlight;
                }
            }

            // [Issue #436] クールダウンチェック（キャプチャ・変化検知前）
            // GridHashCache保全: Cooldown中は検知をスキップし、解除後に正しく変化を検出
            {
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
                    _logger?.LogDebug(
                        "[Issue #436] クールダウン中、キャプチャ・検知スキップ: ID={TranslationId}, 残り{Remaining:F1}秒",
                        translationId, remainingCooldown);
                    return TranslationStepResult.InCooldown;
                }
            }

            // 進行状況を通知
            PublishProgress(translationId, TranslationStatus.Capturing, 0.1f, "画面キャプチャ中...");

            // 画面またはウィンドウをキャプチャ
            if (_targetWindowHandle.HasValue)
            {
                var windowHandle = _targetWindowHandle.Value;
                _logger?.LogDebug($"📷 ウィンドウキャプチャ開始: Handle={windowHandle}");

                // 🔥🔥🔥 [CRITICAL_DEBUG] CaptureService状態確認
                _logger?.LogDebug($"🔥 [CAPTURE_DEBUG] _captureService is null: {_captureService is null}");
                _logger?.LogDebug($"🔥 [CAPTURE_DEBUG] _captureService type: {_captureService?.GetType().FullName ?? "NULL"}");
                _logger?.LogDebug($"🔥 [CAPTURE_DEBUG] CaptureWindowAsync呼び出し直前");

                currentImage = await _captureService!.CaptureWindowAsync(windowHandle).ConfigureAwait(false);

                _logger?.LogDebug($"🔥 [CAPTURE_DEBUG] CaptureWindowAsync呼び出し完了");
                if (currentImage is null)
                {
                    // [Issue #389] ウィンドウキャプチャ失敗時にCaptureFailedEventを発行
                    var captureEx = new InvalidOperationException($"Window capture failed: Handle={windowHandle}");
                    await _eventAggregator.PublishAsync(new CaptureFailedEvent(
                        System.Drawing.Rectangle.Empty, captureEx, captureEx.Message)).ConfigureAwait(false);
                    throw new TranslationException("Window capture failed");
                }
                _logger?.LogDebug($"📷 ウィンドウキャプチャ完了: {(currentImage is not null ? "成功" : "失敗")}");
            }
            else
            {
                _logger?.LogDebug($"📷 画面全体キャプチャ開始");
                currentImage = await _captureService!.CaptureScreenAsync().ConfigureAwait(false);
                if (currentImage is null)
                {
                    throw new TranslationException("画面キャプチャに失敗しました");
                }
                _logger?.LogDebug($"📷 画面全体キャプチャ完了: {(currentImage is not null ? "成功" : "失敗")}");
            }

            captureStopwatch.Stop();

            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();

            // 画面変化検出による無駄な処理の削減
            // [Issue #436] Clone() は SafeImageAdapter で未サポートのため、
            // 参照を取得して直接比較する。_previousCapturedImage は
            // バックグラウンドタスク完了後にのみ更新されるため、
            // 比較中に Dispose されるリスクは低い（参照保持中は GC されない）。
            IImage? previousImageRef = null;
            lock (_previousImageLock)
            {
                previousImageRef = _previousCapturedImage;
            }

            if (previousImageRef != null && currentImage != null)
            {
                try
                {
                    var hasChanges = await _captureService.DetectChangesAsync(
                        previousImageRef, currentImage, 0.05f)
                        .ConfigureAwait(false);

                    if (!hasChanges)
                    {
                        _logger?.LogDebug("🔄 画面に変化がないため翻訳をスキップ: ID={TranslationId}", translationId);
                        currentImage?.Dispose();
                        return TranslationStepResult.NoChange;
                    }
                    _logger?.LogDebug("📸 画面変化を検出、翻訳処理を継続: ID={TranslationId}", translationId);
                }
                catch (ObjectDisposedException)
                {
                    // [Issue #436] バックグラウンドタスクが _previousCapturedImage を更新した場合
                    _logger?.LogDebug("前回画像がDispose済み、変化ありとして処理を継続: ID={TranslationId}", translationId);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("画面変化検出でエラー、翻訳処理を継続: ID={TranslationId}, {Message}", translationId, ex.Message);
                }
            }

            // [Issue #436] InFlight/Cooldownチェックはキャプチャ・変化検知前に移動済み。
            // ここに到達 = InFlight/Cooldown共になし → ディスパッチ可能

            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();

            // [Issue #436] クールダウン起点を設定
            lock (_lastTranslationTimeLock)
            {
                _lastTranslationCompletedAt = DateTime.UtcNow;
            }

            // [Issue #436] _previousCapturedImage の更新は翻訳完了後（Harvest時）に行う。
            // InFlight中はキャプチャ・変化検知をスキップするため、
            // GridHashCacheと_previousCapturedImageの両方が保全される。
            // バックグラウンドタスク完了後に _previousCapturedImage を更新する。

            // [Issue #436] Task.Run で翻訳をバックグラウンド実行
            // currentImage の所有権をバックグラウンドタスクに完全譲渡
            // バックグラウンドタスクが翻訳に使用し、完了後に _previousCapturedImage を更新してから Dispose
            var capturedTranslationId = translationId;
            var capturedCancellationToken = cancellationToken;
            var capturedCurrentImage = currentImage!;

            lock (_translationInFlightLock)
            {
                _translationInFlightTask = Task.Run(async () =>
                {
                    try
                    {
                        await TranslateAndPublishAsync(
                            capturedTranslationId,
                            capturedCurrentImage,
                            capturedCancellationToken).ConfigureAwait(false);

                        // [Issue #436] 翻訳完了後に _previousCapturedImage を更新
                        // バックグラウンドタスクが画像の所有権を持っているため、ここで安全に更新
                        lock (_previousImageLock)
                        {
                            var oldImage = _previousCapturedImage;
                            _previousCapturedImage = capturedCurrentImage; // 所有権移転（Disposeしない）
                            oldImage?.Dispose();
                        }
                    }
                    catch (OperationCanceledException) when (capturedCancellationToken.IsCancellationRequested)
                    {
                        _logger?.LogDebug("[Issue #436] バックグラウンド翻訳がキャンセルされました: ID={TranslationId}",
                            capturedTranslationId);
                        capturedCurrentImage.Dispose();
                    }
                    catch (Exception translationEx) when (translationEx.Message.Contains("PaddlePredictor") ||
                                                          translationEx.Message.Contains("OCR"))
                    {
                        _logger?.LogWarning(translationEx,
                            "[Issue #436] OCRエラーにより翻訳をスキップ: ID={TranslationId}", capturedTranslationId);

                        // OCRエラー時の追加クールダウン
                        if (translationEx.Message.Contains("PaddlePredictor") || translationEx.Message.Contains("run failed"))
                        {
                            lock (_lastTranslationTimeLock)
                            {
                                _lastTranslationCompletedAt = DateTime.UtcNow.AddSeconds(2);
                            }
                        }

                        capturedCurrentImage.Dispose();
                    }
#pragma warning disable CA1031 // バックグラウンドタスクでのアプリケーション安定性のため一般例外をキャッチ
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[Issue #436] バックグラウンド翻訳でエラー: ID={TranslationId}",
                            capturedTranslationId);
                        capturedCurrentImage.Dispose();
                    }
#pragma warning restore CA1031
                }, capturedCancellationToken);
            }

            _logger?.LogDebug("[Issue #436] 翻訳をバックグラウンドにディスパッチ: ID={TranslationId}", translationId);
            return TranslationStepResult.TranslationDispatched;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogDebug($"❌ 自動翻訳ステップがキャンセルされました: ID={translationId}");
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

            // [Issue #389] キャプチャ失敗後にウィンドウの存在を確認
            // キャプチャが全戦略で失敗した場合、ウィンドウが閉じられた可能性が高い
            if (_targetWindowHandle.HasValue && _windowManagerAdapter != null)
            {
                var bounds = _windowManagerAdapter.GetWindowBounds(_targetWindowHandle.Value);
                if (bounds == null)
                {
                    _logger?.LogInformation("[Issue #389] キャプチャ失敗後にウィンドウが存在しないことを確認: Handle=0x{Handle:X}",
                        _targetWindowHandle.Value.ToInt64());
                    try
                    {
                        await _eventAggregator.PublishAsync(new CaptureFailedEvent(
                            System.Drawing.Rectangle.Empty, ex, "Target window closed")).ConfigureAwait(false);
                    }
                    catch (Exception publishEx)
                    {
                        _logger?.LogWarning(publishEx, "[Issue #389] CaptureFailedEvent発行失敗");
                    }
                    return TranslationStepResult.WindowClosed;
                }
            }

            return TranslationStepResult.Error;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 単発翻訳を実行
    /// </summary>
    private async Task ExecuteSingleTranslationAsync(CancellationToken cancellationToken)
    {
        var translationId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 📊 [DIAGNOSTIC] 翻訳工程開始イベント
        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
        {
            Stage = "Translation",
            IsSuccess = true,
            ProcessingTimeMs = 0,
            SessionId = translationId,
            Severity = DiagnosticSeverity.Information,
            Message = $"翻訳工程開始: 単発翻訳実行 ID={translationId}",
            Metrics = new Dictionary<string, object>
            {
                { "TranslationId", translationId },
                { "TranslationMode", "Manual" },
                { "IsAutomaticActive", _isAutomaticTranslationActive },
                { "TargetWindowHandle", _targetWindowHandle?.ToString("X") ?? "なし" }
            }
        }).ConfigureAwait(false);

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

            // 🔥 [ISSUE#163_REFACTOR] Singleshotパイプライン処理を使用
            var (currentImage, result) = await ExecuteTranslationPipelineAsync(
                translationId,
                cancellationToken).ConfigureAwait(false);

            // 画像の破棄責任はSingleshotが持つ
            using (currentImage)
            {
                if (result == null)
                {
                    _logger?.LogWarning("⚠️ 翻訳結果がnullのため処理を中断");
                    return;
                }

                // 翻訳完了時刻を記録（重複翻訳防止用）
                lock (_lastTranslationTimeLock)
                {
                    _lastTranslationCompletedAt = DateTime.UtcNow;
                }

                // 📊 [DIAGNOSTIC] 翻訳工程成功イベント
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "Translation",
                    IsSuccess = true,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Information,
                    Message = $"翻訳工程成功: 翻訳テキスト長={result.TranslatedText.Length}, 処理時間={stopwatch.ElapsedMilliseconds}ms",
                    Metrics = new Dictionary<string, object>
                    {
                        { "TranslationId", translationId },
                        { "TranslationMode", "Manual" },
                        { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds },
                        { "OriginalTextLength", result.OriginalText?.Length ?? 0 },
                        { "TranslatedTextLength", result.TranslatedText.Length },
                        { "TargetLanguage", result.TargetLanguage ?? "未指定" }
                        // NOTE: 以下プロパティは現在のTranslationResult型に存在しないためコメントアウト
                        // { "DetectedTextRegions", result.DetectedTextRegions?.Count ?? 0 },
                        // { "SourceLanguage", result.SourceLanguage ?? "未指定" },
                        // { "TranslationEngine", result.EngineUsed ?? "未指定" },
                        // { "DisplayDuration", result.DisplayDuration?.TotalSeconds ?? 0 }
                    }
                }).ConfigureAwait(false);

                _logger?.LogInformation("単発翻訳が完了しました: ID={Id}, テキスト長={Length}",
                    translationId, result.TranslatedText.Length);

                // [Issue #557] Singleshotのローディング終了はAggregatedChunksReadyEventHandlerの
                // オーバーレイ表示直前で発火するため、ここでの発火は削除
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
            // 📊 [DIAGNOSTIC] 翻訳工程失敗イベント
            try
            {
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "Translation",
                    IsSuccess = false,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = ex.Message,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"翻訳工程失敗: {ex.GetType().Name}: {ex.Message}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "TranslationId", translationId },
                        { "TranslationMode", "Manual" },
                        { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds },
                        { "ErrorType", ex.GetType().Name },
                        { "IsAutomaticActive", _isAutomaticTranslationActive },
                        { "TargetWindowHandle", _targetWindowHandle?.ToString("X") ?? "なし" }
                    }
                }).ConfigureAwait(false);
            }
            catch
            {
                // 診断イベント発行失敗は無視（元の例外を優先）
            }

            _logger?.LogError(ex, "単発翻訳でエラーが発生しました");
            PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"エラー: {ex.Message}");
            throw;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 🔥 [ISSUE#163_REFACTOR] 既にキャプチャされた画像を翻訳して結果を発行（Live翻訳用）
    /// </summary>
    /// <param name="translationId">翻訳ID</param>
    /// <param name="currentImage">既にキャプチャされた画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳結果（発行された場合）、nullの場合は重複/座標ベースでスキップ</returns>
    private async Task<TranslationResult?> TranslateAndPublishAsync(
        string translationId,
        IImage currentImage,
        CancellationToken cancellationToken)
    {
        // 翻訳実行
        _logger?.LogDebug("🌍 翻訳処理開始: ID={Id}", translationId);
        var result = await ExecuteTranslationAsync(translationId, currentImage, TranslationMode.Live, cancellationToken)
            .ConfigureAwait(false);

        // Live翻訳: 重複チェック、座標ベースモード判定
        string lastTranslatedText;
        lock (_lastTranslatedTextLock)
        {
            lastTranslatedText = _lastTranslatedText;
        }

        if (!string.IsNullOrEmpty(lastTranslatedText) &&
            string.Equals(result?.TranslatedText, lastTranslatedText, StringComparison.Ordinal))
        {
            _logger?.LogDebug("🔄 前回と同じ翻訳結果のため発行をスキップ: '{Text}'", result?.TranslatedText);
            return null; // 結果発行なし
        }

        if (result?.IsCoordinateBasedMode == true)
        {
            _logger?.LogDebug("🎯 座標ベース翻訳モードのためObservable発行をスキップ");
            return null; // 結果発行なし
        }

        // 翻訳結果を記録
        lock (_lastTranslatedTextLock)
        {
            _lastTranslatedText = result?.TranslatedText ?? string.Empty;
        }

        // 結果を通知
        if (result != null)
        {
            _logger?.LogDebug("📤 翻訳結果をObservableに発行: '{Text}'", result.TranslatedText);
            _translationResultsSubject.OnNext(result);
            return result; // 発行された結果を返す
        }
        return null;
    }

    /// <summary>
    /// 🔥 [ISSUE#163_REFACTOR] Singleshot翻訳のパイプライン処理
    /// キャプチャ→翻訳→結果発行の一連の処理を実行
    /// </summary>
    /// <param name="translationId">翻訳ID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>キャプチャ画像と翻訳結果のタプル（呼び出し側が画像の破棄責任を持つ）</returns>
    private async Task<(IImage? currentImage, TranslationResult? result)> ExecuteTranslationPipelineAsync(
        string translationId,
        CancellationToken cancellationToken)
    {
        // [Issue #389] ウィンドウ存在チェック（キャプチャ前）
        if (_targetWindowHandle.HasValue && _targetWindowHandle.Value != IntPtr.Zero && _windowManagerAdapter != null)
        {
            var bounds = _windowManagerAdapter.GetWindowBounds(_targetWindowHandle.Value);
            if (bounds == null)
            {
                _logger?.LogInformation("[Issue #389] 対象ウィンドウが閉じられました（Singleshot）: Handle=0x{Handle:X}",
                    _targetWindowHandle.Value.ToInt64());

                await _eventAggregator.PublishAsync(new CaptureFailedEvent(
                    System.Drawing.Rectangle.Empty,
                    new InvalidOperationException($"Target window has been closed: Handle=0x{_targetWindowHandle.Value.ToInt64():X}"),
                    "Target window closed")).ConfigureAwait(false);

                return (null, null);
            }
        }

        // キャプチャ処理
        IImage? currentImage;
        if (_targetWindowHandle.HasValue && _targetWindowHandle.Value != IntPtr.Zero)
        {
            _logger?.LogDebug("🎯 ウィンドウキャプチャ実行: Handle={Handle:X}",
                _targetWindowHandle.Value.ToInt64());
            currentImage = await _captureService.CaptureWindowAsync(_targetWindowHandle.Value).ConfigureAwait(false);
        }
        else
        {
            _logger?.LogWarning("⚠️ ウィンドウハンドルが未指定のため画面全体をキャプチャ");
            currentImage = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
        }

        if (currentImage == null)
        {
            _logger?.LogDebug("❌ 画面キャプチャが失敗しました: ID={Id}", translationId);
            return (null, null);
        }

        // [Issue #508] Shot翻訳前にDetection-Onlyを実行してOCRヒントを直接渡す
        // DetectionBoundsCacheを経由しないことで、Detection-Onlyフィルタ（#500）の誤スキップを防止
        if (_coordinateBasedTranslation != null && _targetWindowHandle.HasValue)
        {
            try
            {
                var detectionResults = await _ocrEngine.DetectTextRegionsAsync(currentImage!, cancellationToken)
                    .ConfigureAwait(false);
                var detectionBounds = detectionResults.TextRegions
                    .Select(r => r.Bounds)
                    .Where(b => b.Width > 0 && b.Height > 0)
                    .ToArray();

                if (detectionBounds.Length > 0)
                {
                    _coordinateBasedTranslation.SetPrecomputedHintBounds(detectionBounds, currentImage!.Height);
                    _logger?.LogInformation(
                        "[Issue #508] Shot翻訳前Detection-Only完了: {Count}個のテキスト領域を検出（直接パス, ImageHeight={Height}）",
                        detectionBounds.Length, currentImage!.Height);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Issue #508] Shot翻訳前Detection-Only失敗（ヒントなしで続行）");
            }
        }

        // 翻訳実行
        _logger?.LogDebug("🌍 翻訳処理開始: ID={Id}", translationId);
        var result = await ExecuteTranslationAsync(translationId, currentImage!, TranslationMode.Singleshot, cancellationToken)
            .ConfigureAwait(false);

        // 🔥 [ISSUE#163_REFACTOR] 画像の破棄は呼び出し側（usingステートメント）が責任を持つ
        // currentImage.Dispose(); // 削除

        // Singleshot: DisplayDuration設定
        result = result with { DisplayDuration = GetSingleTranslationDisplayDuration() };
        _translationResultsSubject.OnNext(result);

        return (currentImage, result);
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

            // 🗑️ [PHASE2.5_CLEANUP] CaptureCompletedEvent重複発行削除
            // CaptureCompletedEventの発行はAdaptiveCaptureService.PublishCaptureCompletedEventAsyncに統一
            // 複数ROI画像の場合はROIImageCapturedEvent × 8が発行され、各ROIが個別に処理される
            _logger?.LogDebug($"🔄 [PHASE2.5_CLEANUP] CaptureCompletedEvent発行はAdaptiveCaptureServiceに統一: ID={translationId}");

            // 🚨 CRITICAL DEBUG: _logger?.LogDebug呼び出し直前
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
            _logger?.LogDebug($"🔍 座標ベース翻訳チェック:");
            _logger?.LogDebug($"   📦 _coordinateBasedTranslation != null: {_coordinateBasedTranslation != null}");
            _logger?.LogDebug($"   ✅ IsCoordinateBasedTranslationAvailable: {_coordinateBasedTranslation?.IsCoordinateBasedTranslationAvailable()}");
            _logger?.LogDebug($"   🪟 _targetWindowHandle.HasValue: {_targetWindowHandle.HasValue}");
            _logger?.LogDebug($"   🪟 _targetWindowHandle: {_targetWindowHandle?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}");

            // 🚨 CRITICAL DEBUG: _logger?.LogDebug呼び出し完了
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch { }
            _logger?.LogDebug($"   🖼️ image is IAdvancedImage: {image is IAdvancedImage}");

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

            _logger?.LogDebug($"🎯 座標ベース翻訳条件評価結果: {overallCondition}");
            _logger?.LogDebug($"   📋 詳細条件:");
            _logger?.LogDebug($"     📦 coordinateAvailable: {coordinateAvailable}");
            _logger?.LogDebug($"     🪟 hasWindowHandle: {hasWindowHandle}");
            _logger?.LogDebug($"     🖼️ isAdvancedImage: {isAdvancedImage}");

            Console.WriteLine($"🎯 座標ベース翻訳条件評価結果: {overallCondition}");
            Console.WriteLine($"   📦 coordinateAvailable: {coordinateAvailable}");
            Console.WriteLine($"   🪟 hasWindowHandle: {hasWindowHandle}");
            Console.WriteLine($"   🖼️ isAdvancedImage: {isAdvancedImage}");

            // 座標ベース翻訳実行フラグ
            var coordinateBasedTranslationExecuted = false;

            // 🎉 [PHASE12.2_COMPLETE] Phase 12.2イベント駆動アーキテクチャ
            // ProcessWithCoordinateBasedTranslationAsync内部でTimedChunkAggregatorを呼び出し
            // CoordinateBasedTranslationService.cs Line 315でreturnして2重翻訳を防止済み
            if (overallCondition && image is IAdvancedImage advancedImage)
            {
                // 🔥🔥🔥 [ULTRA_DEBUG] 座標ベース翻訳実行開始
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [ORCH_FLOW] 座標ベース翻訳開始 - ID={translationId}{Environment.NewLine}");
                }
                catch { }

                _logger?.LogDebug($"🎯 座標ベース翻訳処理を実行開始: ID={translationId}");
                _logger?.LogDebug("🎯 座標ベース翻訳処理を実行: ID={TranslationId}", translationId);

                try
                {
                    // 座標ベース翻訳処理を実行（BatchOCR + MultiWindowOverlay）
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }

                    _logger?.LogDebug($"🔄 ProcessWithCoordinateBasedTranslationAsync呼び出し開始");

                    // 🔧 [SINGLESHOT_FIX] Singleshotモード時は画面変化検出をバイパス
                    var pipelineOptions = mode == TranslationMode.Singleshot
                        ? new Baketa.Core.Models.Processing.ProcessingPipelineOptions
                        {
                            ForceCompleteExecution = true,
                            EnableEarlyTermination = false  // 早期終了も明示的に無効化
                        }
                        : null;

                    // 🔥 [Issue #193/#194] キャプチャ時のOCR結果を渡して二重OCR防止
                    var preExecutedOcrResult = advancedImage.PreExecutedOcrResult;
                    if (preExecutedOcrResult != null)
                    {
                        _logger?.LogInformation("🔥 [DUAL_OCR_FIX] PreExecutedOcrResult検出: {RegionCount}個のテキスト領域をパイプラインに渡す",
                            preExecutedOcrResult.TextRegions.Count);
                    }

                    await _coordinateBasedTranslation!.ProcessWithCoordinateBasedTranslationAsync(
                        advancedImage,
                        _targetWindowHandle!.Value,
                        pipelineOptions,
                        preExecutedOcrResult,
                        cancellationToken)
                        .ConfigureAwait(false);

                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch { }

                    _logger?.LogDebug($"✅ ProcessWithCoordinateBasedTranslationAsync呼び出し完了");
                    _logger?.LogInformation("✅ 座標ベース翻訳処理完了: ID={TranslationId}", translationId);

                    // 座標ベース翻訳が正常実行された
                    coordinateBasedTranslationExecuted = true;

                    // 🎉 [PHASE12.2_COMPLETE] Phase 12.2イベント駆動アーキテクチャ
                    // ProcessWithCoordinateBasedTranslationAsync内部でTimedChunkAggregatorに追加済み
                    // AggregatedChunksReadyEventHandler経由で翻訳・オーバーレイ表示されるため、
                    // ここでは2重翻訳を防止するため、IsCoordinateBasedMode=trueで即座にreturn
                    _logger?.LogDebug($"🎉 [PHASE12.2_COMPLETE] Phase 12.2早期リターン - AggregatedChunksReadyEventHandler経由で処理");
                    _logger?.LogInformation("🎉 [PHASE12.2_COMPLETE] 2重翻訳防止: AggregatedChunksReadyEventHandler経由で処理 - ID={TranslationId}", translationId);

                    // [Issue #394] クールダウンは呼び出し元で設定済みのため、ここでは設定しない
                    // - Auto path: ExecuteAutomaticTranslationStepAsync L1267 で設定済み
                    //   → OCR処理中にクールダウンを消化（体感遅延削減）
                    // - Singleshot path: ExecuteSingleTranslationAsync L1415 で設定される

                    return new TranslationResult
                    {
                        Id = translationId,
                        Mode = mode,
                        OriginalText = "",
                        TranslatedText = "",
                        DetectedLanguage = "ja",
                        TargetLanguage = LanguageCodeConverter.ToLanguageCode(_settingsService.GetValue("UI:TranslationLanguage", "英語")),
                        Confidence = 1.0f,
                        ProcessingTime = DateTime.UtcNow - startTime,
                        IsCoordinateBasedMode = true // 座標ベースモードを示すフラグ - Observableスキップ + クールダウン設定
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // [Issue #402] Stop操作によるキャンセルはフォールバックせず上位に伝播
                    _logger?.LogDebug("座標ベース処理がキャンセルされました: ID={TranslationId}", translationId);
                    throw;
                }
                catch (Exception coordinateEx)
                {
                    _logger?.LogDebug($"❌ 座標ベース処理でエラー発生: {coordinateEx.Message}");
                    _logger?.LogDebug($"❌ エラーのスタックトレース: {coordinateEx.StackTrace}");

                    // [Issue #389] ウィンドウが閉じられたことによるエラーの場合はフォールバックせず上位に伝播
                    if (_targetWindowHandle.HasValue && _windowManagerAdapter != null)
                    {
                        var bounds = _windowManagerAdapter.GetWindowBounds(_targetWindowHandle.Value);
                        if (bounds == null)
                        {
                            _logger?.LogInformation("[Issue #389] 座標ベース処理失敗後にウィンドウが存在しないことを確認: Handle=0x{Handle:X}",
                                _targetWindowHandle.Value.ToInt64());
                            throw; // 上位のcatchでWindowClosedとして処理される
                        }
                    }

                    _logger?.LogWarning(coordinateEx, "⚠️ 座標ベース処理でエラーが発生、従来のOCR処理にフォールバック: ID={TranslationId}", translationId);
                    // 座標ベース処理でエラーが発生した場合は従来のOCR処理にフォールバック
                    coordinateBasedTranslationExecuted = false;
                }
            }
            else
            {
                _logger?.LogDebug($"⚠️ 座標ベース翻訳をスキップ（条件不一致）");
                if (_coordinateBasedTranslation == null)
                    _logger?.LogDebug($"   理由: _coordinateBasedTranslation is null");
                else if (_coordinateBasedTranslation.IsCoordinateBasedTranslationAvailable() != true)
                    _logger?.LogDebug($"   理由: IsCoordinateBasedTranslationAvailable() = {_coordinateBasedTranslation.IsCoordinateBasedTranslationAvailable()}");
                if (!_targetWindowHandle.HasValue)
                    _logger?.LogDebug($"   理由: _targetWindowHandle is null");
                if (image is not IAdvancedImage)
                    _logger?.LogDebug($"   理由: image is not IAdvancedImage (actual type: {image?.GetType()?.Name ?? "null"})");
            }

            // OCR処理
            PublishProgress(translationId, TranslationStatus.ProcessingOCR, 0.3f, "テキスト認識中...");

            _logger?.LogDebug($"🔍 OCRエンジン状態チェック - IsInitialized: {_ocrEngine.IsInitialized}");

            // OCRエンジンが初期化されていない場合は初期化
            if (!_ocrEngine.IsInitialized)
            {
                _logger?.LogDebug($"🛠️ OCRエンジン初期化開始");

                var unifiedSettings = _ocrSettings.CurrentValue;
                var ocrSettings = new OcrEngineSettings
                {
                    Language = "jpn", // 日本語
                    DetectionThreshold = (float)unifiedSettings.DetectionThreshold, // 統一設定: appsettings.json から読み込み
                    RecognitionThreshold = 0.1f // 認識閾値（今後統一化対象）
                };

                try
                {
                    await _ocrEngine.InitializeAsync(ocrSettings, cancellationToken).ConfigureAwait(false);
                    _logger?.LogDebug($"✅ OCRエンジン初期化完了");
                }
                catch (Exception initEx)
                {
                    _logger?.LogDebug($"❌ OCRエンジン初期化エラー: {initEx.Message}");
                    throw;
                }
            }
            else
            {
                // 既に初期化されているが、閾値設定を更新する
                _logger?.LogDebug($"🔄 既に初期化されたOCRエンジンの設定を更新");

                var unifiedSettings = _ocrSettings.CurrentValue;
                var updatedSettings = new OcrEngineSettings
                {
                    Language = "jpn", // 日本語
                    DetectionThreshold = (float)unifiedSettings.DetectionThreshold, // 統一設定: appsettings.json から読み込み
                    RecognitionThreshold = 0.1f // 認識閾値（今後統一化対象）
                };

                try
                {
                    await _ocrEngine.ApplySettingsAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
                    _logger?.LogDebug($"✅ OCRエンジン設定更新完了");
                }
                catch (Exception applyEx)
                {
                    _logger?.LogDebug($"⚠️ OCRエンジン設定更新エラー: {applyEx.Message}");
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
                _logger?.LogDebug($"🔍 OCR処理開始 - 画像サイズ: {image?.Width ?? 0}x{image?.Height ?? 0}");
                _logger?.LogDebug($"🖼️ 画像情報: 型={image?.GetType().Name ?? "null"}");

                // デバッグ用: キャプチャした画像を保存
                if (image != null)
                {
                    // 画像キャプチャ完了
                }

                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 OCR処理開始 - 画像サイズ: {image?.Width ?? 0}x{image?.Height ?? 0}{Environment.NewLine}");
            }
            catch (Exception sizeEx)
            {
                _logger?.LogDebug($"❌ 画像サイズ取得エラー: {sizeEx.Message}");
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
                    _logger?.LogDebug($"🛑 前のOCR要求を強制キャンセル: ID={translationId}");
                    oldCts.Cancel();

                    // PaddleOCRエンジンのタイムアウトもキャンセル
                    _ocrEngine.CancelCurrentOcrTimeout();
                }
                catch (Exception cancelEx)
                {
                    _logger?.LogDebug($"⚠️ OCR強制キャンセル中にエラー: {cancelEx.Message}");
                }
                finally
                {
                    oldCts.Dispose();
                }
            }

            var currentRequestToken = _latestOcrRequestCts.Token;

            _logger?.LogDebug($"🤖 OCRエンジン呼び出し開始（排他制御付き）:");
            _logger?.LogDebug($"   🔧 エンジン名: {_ocrEngine?.EngineName ?? "(null)"}");
            _logger?.LogDebug($"   ✅ 初期化状態: {_ocrEngine?.IsInitialized ?? false}");
            _logger?.LogDebug($"   🌐 現在の言語: {_ocrEngine?.CurrentLanguage ?? "(null)"}");

            OcrResults? ocrResults = null;
            FallbackTranslationResult? cloudTranslationResult = null;
            var speculativeOcrUsed = false;

            // 🚀 [Issue #293] 投機的OCRキャッシュチェック
            // Shot翻訳時にキャッシュがあればOCRをスキップして応答時間を短縮
            if (_speculativeOcrService?.IsCacheValid == true && mode == TranslationMode.Singleshot)
            {
                // 画像ハッシュを計算してキャッシュと照合
                var imageHash = await ComputeImageHashForCacheAsync(image, currentRequestToken).ConfigureAwait(false);
                var cachedResult = _speculativeOcrService.ConsumeCache(imageHash);

                if (cachedResult != null)
                {
                    _logger?.LogInformation("🎯 [Issue #293] 投機的OCRキャッシュヒット！ OCR処理をスキップ (ExecutionTime={ExecutionTime}ms, Regions={Regions})",
                        cachedResult.ExecutionTime.TotalMilliseconds,
                        cachedResult.DetectedRegionCount);
                    Console.WriteLine($"🎯 [Issue #293] 投機的OCRキャッシュヒット！ OCR処理をスキップ");

                    ocrResults = cachedResult.OcrResults;
                    speculativeOcrUsed = true;
                }
            }

            // 🚀 [Issue #290] Fork-Join並列実行: OCR || Cloud AI翻訳
            // Pro/Premiaプランの場合、OCRとCloud AI翻訳を並列実行して待ち時間を短縮
            var isCloudAiAvailable = IsCloudAiParallelExecutionAvailable();

            // 投機的OCRキャッシュがヒットしなかった場合のみOCR処理を実行
            if (!speculativeOcrUsed)
            {
            // OCR処理の排他制御
            await _ocrExecutionSemaphore.WaitAsync(currentRequestToken).ConfigureAwait(false);
            try
            {
                // 最新要求かどうかチェック
                if (_latestOcrRequestCts?.Token != currentRequestToken)
                {
                    _logger?.LogDebug($"🚫 古いOCR要求のためキャンセル: ID={translationId}");
                    currentRequestToken.ThrowIfCancellationRequested();
                }

                _logger?.LogDebug($"🔒 OCR処理を排他実行開始: ID={translationId}");

                if (isCloudAiAvailable)
                {
                    // [Issue #415] 画像ハッシュによるCloud APIコール抑制
                    FallbackTranslationResult? cachedResult = null;
                    long imageHash = 0;

                    if (_cloudTranslationCache != null && _targetWindowHandle.HasValue)
                    {
                        imageHash = _cloudTranslationCache.ComputeImageHash(image.GetImageMemory());
                        if (_cloudTranslationCache.TryGetCachedResult(_targetWindowHandle.Value, imageHash, out cachedResult))
                        {
                            _logger?.LogInformation("[Issue #415] セカンダリパス: Cloud APIスキップ（キャッシュ結果を再利用）");
                        }
                    }

                    if (cachedResult != null)
                    {
                        // キャッシュヒット → OCRのみ実行（Cloud結果は既にキャッシュ済みのため再利用不要）
                        ocrResults = await _ocrEngine!.RecognizeAsync(image, cancellationToken: currentRequestToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // キャッシュミス → 通常のFork-Join実行
                        var parallelResult = await ExecuteForkJoinOcrAndCloudAsync(
                            image, translationId, currentRequestToken).ConfigureAwait(false);

                        ocrResults = parallelResult.OcrResults;
                        cloudTranslationResult = parallelResult.CloudResult;

                        // [Issue #415] 成功した結果をキャッシュに保存
                        if (cloudTranslationResult?.IsSuccess == true && _cloudTranslationCache != null
                            && _targetWindowHandle.HasValue && imageHash != 0)
                        {
                            _cloudTranslationCache.CacheResult(_targetWindowHandle.Value, imageHash, cloudTranslationResult);
                        }

                        _logger?.LogInformation(
                            "🚀 [Issue #290] Fork-Join完了: OCR={OcrMs}ms, Cloud={CloudMs}ms (並列実行)",
                            parallelResult.OcrDuration.TotalMilliseconds,
                            parallelResult.CloudDuration?.TotalMilliseconds ?? 0);
                    }
                }
                else
                {
                    // 従来のOCRのみ実行（Free/Standardプラン）
                    ocrResults = await _ocrEngine!.RecognizeAsync(image, cancellationToken: currentRequestToken).ConfigureAwait(false);
                }

                _logger?.LogDebug($"🔓 OCR処理を排他実行完了: ID={translationId}");
            }
            finally
            {
                _ocrExecutionSemaphore.Release();
            }

            _logger?.LogDebug($"🤖 OCRエンジン呼び出し完了");
            } // end if (!speculativeOcrUsed)

            // 🚀 [OCR_TRANSLATION_BRIDGE_FIX] OCR完了イベントを発行して翻訳フローを開始
            try
            {
                Console.WriteLine($"🔥 [BRIDGE_FIX] OCR完了イベント発行開始: TextRegions数={ocrResults.TextRegions.Count}");

                // 🔥 [CONFIDENCE_FILTER] 信頼度フィルタリング - 低信頼度結果を翻訳から除外
                var confidenceThreshold = _ocrSettings?.CurrentValue?.ConfidenceThreshold ?? 0.9;
                var filteredRegions = ocrResults.TextRegions
                    .Where(region => region.Confidence >= confidenceThreshold)
                    .ToList();

                var filteredCount = ocrResults.TextRegions.Count - filteredRegions.Count;
                if (filteredCount > 0)
                {
                    Console.WriteLine($"🔍 [CONFIDENCE_FILTER] 信頼度フィルタリング: {filteredCount}件除外（閾値={confidenceThreshold:F2}）");
                    _logger?.LogInformation(
                        "🔍 [CONFIDENCE_FILTER] 信頼度{Threshold:F2}未満の{FilteredCount}件をフィルタリング（残り{RemainingCount}件）",
                        confidenceThreshold, filteredCount, filteredRegions.Count);
                }

                // OCR結果をOcrResultsコレクションに変換（フィルタリング済み）
                var ocrResultsList = filteredRegions.Select(region => new CoreOcrResult(
                    text: region.Text,
                    bounds: region.Bounds,
                    confidence: (float)region.Confidence)).ToList().AsReadOnly();

                var ocrCompletedEvent = new OcrCompletedEvent(
                    sourceImage: image,
                    results: ocrResultsList,
                    processingTime: ocrResults.ProcessingTime);

                Console.WriteLine($"🔥 [BRIDGE_FIX] OcrCompletedEvent作成完了 - ID: {ocrCompletedEvent.Id}");

                // 🎉 [PHASE12.2_COMPLETE] イベント駆動アーキテクチャ完全移行
                // 座標ベース翻訳モードの場合、AggregatedChunksReadyEventHandlerが処理するため発行不要
                // overallCondition: 座標ベース翻訳の実行条件（coordinateAvailable && hasWindowHandle && isAdvancedImage）
                if (!overallCondition)
                {
                    // 従来の翻訳モード（非座標ベース）の場合のみイベント発行
                    await _eventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                    Console.WriteLine($"🔥 [PHASE12.2] OcrCompletedEvent発行完了 - 従来翻訳フロー");
                    _logger?.LogDebug($"🔥 [PHASE12.2] 従来翻訳フロー: 非座標ベースモードのためOcrCompletedEvent発行");
                }
                else
                {
                    Console.WriteLine($"🎉 [PHASE12.2_COMPLETE] 座標ベース翻訳モード - AggregatedChunksReadyEventHandlerが処理");
                    _logger?.LogDebug($"🎉 [PHASE12.2_COMPLETE] イベント駆動処理: TimedChunkAggregator → AggregatedChunksReadyEventHandler");
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

            _logger?.LogDebug($"📊 OCR結果: HasText={ocrResults.HasText}, TextRegions数={ocrResults.TextRegions.Count}");
            _logger?.LogDebug($"⏱️ OCR処理時間: {ocrResults.ProcessingTime.TotalMilliseconds:F1}ms");
            _logger?.LogDebug($"🌐 OCR言語: {ocrResults.LanguageCode}");

            // 詳細なOCRデバッグ情報を表示
            if (ocrResults.TextRegions.Count > 0)
            {
                _logger?.LogDebug($"🔍 詳細なOCRテキストリージョン情報:");
                for (int i = 0; i < Math.Min(5, ocrResults.TextRegions.Count); i++) // 最初の5個だけ表示
                {
                    var region = ocrResults.TextRegions[i];
                    _logger?.LogDebug($"   リージョン {i + 1}:");
                    _logger?.LogDebug($"     📖 テキスト: '{region.Text ?? "(null)"}'");
                    _logger?.LogDebug($"     📊 信頼度: {region.Confidence:F4}");
                    _logger?.LogDebug($"     📍 座標: X={region.Bounds.X}, Y={region.Bounds.Y}, W={region.Bounds.Width}, H={region.Bounds.Height}");
                    _logger?.LogDebug($"     🔢 テキスト長: {region.Text?.Length ?? 0}");
                }
                if (ocrResults.TextRegions.Count > 5)
                {
                    _logger?.LogDebug($"   ... 他 {ocrResults.TextRegions.Count - 5} 個のリージョン");
                }
            }
            else
            {
                // テキスト未検出時はデバッグログのみに変更
                _logger?.LogDebug("TextRegions が空です - 画像内にテキストが検出されませんでした");
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

                    _logger?.LogDebug($"📋 テキストグループ化を使用: 段落保持={preserveParagraphs}");
                }
                else
                {
                    // 従来の単純な改行区切り結合
                    originalText = ocrResults.Text;

                    _logger?.LogDebug($"📋 従来のテキスト結合を使用");
                }

                ocrConfidence = ocrResults.TextRegions.Count > 0
                    ? ocrResults.TextRegions.Average(r => r.Confidence)
                    : 0.0;

                _logger?.LogDebug($"✅ OCR認識成功:");
                _logger?.LogDebug($"   📖 認識テキスト: '{originalText}'");
                _logger?.LogDebug($"   📊 平均信頼度: {ocrConfidence:F2}");
                _logger?.LogDebug($"   🔢 テキスト長: {originalText.Length}");
                _logger?.LogDebug($"   🔤 テキストがnullまたは空: {string.IsNullOrEmpty(originalText)}");
                _logger?.LogDebug($"   🔤 テキストが空白のみ: {string.IsNullOrWhiteSpace(originalText)}");

                _logger?.LogDebug("OCR認識成功: テキスト長={Length}, 信頼度={Confidence:F2}",
                    originalText.Length, ocrConfidence);
            }
            else
            {
                // テキスト未検出時はデバッグログのみに変更（通常ログを抑制）
                _logger?.LogDebug("OCR処理でテキストが検出されませんでした");
                originalText = string.Empty;
            }

            // 🔥 [DIAGNOSTIC] OCR処理診断イベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "OCR",
                IsSuccess = ocrResults.HasText,
                ProcessingTimeMs = (long)ocrResults.ProcessingTime.TotalMilliseconds,
                SessionId = translationId,
                Severity = ocrResults.HasText ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                Message = ocrResults.HasText
                    ? $"OCR処理成功: テキスト検出数={ocrResults.TextRegions.Count}, 処理時間={ocrResults.ProcessingTime.TotalMilliseconds:F1}ms"
                    : "OCR処理でテキストが検出されませんでした",
                Metrics = new Dictionary<string, object>
                {
                    { "DetectedTextRegions", ocrResults.TextRegions.Count },
                    { "HasText", ocrResults.HasText },
                    { "AverageConfidence", ocrConfidence },
                    { "ProcessingTimeMs", (long)ocrResults.ProcessingTime.TotalMilliseconds },
                    { "LanguageCode", ocrResults.LanguageCode ?? "unknown" },
                    { "OCREngine", _ocrEngine?.EngineName ?? "unknown" },
                    { "ImageSize", $"{image?.Width ?? 0}x{image?.Height ?? 0}" },
                    { "ExtractedText", originalText }, // 🔥 重要: 実際のOCRテキストを記録
                    { "TextLength", originalText.Length }
                }
            }).ConfigureAwait(false);

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

            string translatedText = string.Empty;
            if (!string.IsNullOrWhiteSpace(originalText))
            {
                try
                {
                    // 設定から言語ペアを取得
                    // 🔥 [Issue #189] nullフォールバックをTranslationSettingsのデフォルト値(en→ja)に合わせて修正
                    var sourceCode = settings.DefaultSourceLanguage ?? "en";
                    var targetCode = settings.DefaultTargetLanguage ?? "ja";

                    // 🚨 [CRITICAL_DEBUG] 言語設定の実際の値をデバッグ出力
                    _logger?.LogDebug($"🚨 [LANGUAGE_SETTINGS_DEBUG] settings.DefaultSourceLanguage='{settings.DefaultSourceLanguage}'");
                    _logger?.LogDebug($"🚨 [LANGUAGE_SETTINGS_DEBUG] settings.DefaultTargetLanguage='{settings.DefaultTargetLanguage}'");
                    _logger?.LogDebug($"🚨 [LANGUAGE_SETTINGS_DEBUG] sourceCode='{sourceCode}', targetCode='{targetCode}'");

                    // 🔥 [DIAGNOSTIC] 言語検出診断イベント
                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "LanguageDetection",
                        IsSuccess = true,
                        ProcessingTimeMs = 0,
                        SessionId = translationId,
                        Severity = DiagnosticSeverity.Information,
                        Message = $"言語検出完了: {sourceCode} → {targetCode}",
                        Metrics = new Dictionary<string, object>
                        {
                            { "SourceLanguage", sourceCode },
                            { "TargetLanguage", targetCode },
                            { "LanguageDetectionEngine", "Settings" },
                            { "ConfidenceScore", 1.0 },
                            { "RequiresTranslation", sourceCode != targetCode },
                            { "OriginalText", originalText }
                        }
                    }).ConfigureAwait(false);

                    _logger?.LogDebug($"🌍 翻訳開始: '{originalText}' ({sourceCode} → {targetCode})");

                    // ✨ 実際のAI翻訳エンジンを使用した翻訳処理（辞書置換を廃止）
                    _logger?.LogDebug($"🤖 AI翻訳エンジン使用開始: '{originalText}' ({sourceCode} → {targetCode})");

                    // 🚀 [NLLB-200_INTEGRATION] NLLB-200 AI翻訳エンジンを使用
                    _logger?.LogDebug($"🤖 NLLB-200 AI翻訳エンジンを使用して翻訳開始");

                    // 🔥 [DIAGNOSTIC] 翻訳エンジン選択診断イベント
                    var engineStatus = "nllb_200_ai_engine"; // NLLB-200 AI翻訳エンジン使用
                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "TranslationEngineSelection",
                        IsSuccess = true, // NLLB-200 AI翻訳エンジンを使用
                        ProcessingTimeMs = 0,
                        SessionId = translationId,
                        Severity = DiagnosticSeverity.Information,
                        Message = "翻訳エンジン選択: NLLB-200 AI翻訳エンジンを使用",
                        Metrics = new Dictionary<string, object>
                        {
                            { "PrimaryEngine", "NLLB-200" },
                            { "PrimaryEngineStatus", "active" },
                            { "FallbackEngine", "None" },
                            { "FallbackReason", "not_required" },
                            { "ActualEngine", "NLLB-200" }
                        }
                    }).ConfigureAwait(false);

                    // 🤖 NLLB-200 AI翻訳エンジンを使用
                    var translationStartTime = DateTime.UtcNow;

                    // すべての言語ペアでNLLB-200を使用
                    // NOTE: Gate判定はAggregatedChunksReadyEventHandlerに移行済み（Issue #293）
                    translatedText = await TranslateWithNLLBEngineAsync(originalText, sourceCode, targetCode);
                    _logger?.LogDebug($"🤖 NLLB-200翻訳結果: '{translatedText}'");

                    var translationElapsed = DateTime.UtcNow - translationStartTime;

                    // 📊 [Issue #307] 翻訳完了イベントを発行（Analytics用）
                    // AnalyticsEventProcessorがこのイベントを購読して使用統計を記録
                    // [Gemini Review] Analyticsは副次的処理なので、失敗しても翻訳は継続
                    if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                    {
                        try
                        {
                            await _eventAggregator.PublishAsync(new TranslationCompletedEvent(
                                sourceText: originalText,
                                translatedText: translatedText,
                                sourceLanguage: sourceCode,
                                targetLanguage: targetCode,
                                processingTime: translationElapsed,
                                engineName: "NLLB-200"
                            )).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "[Issue #307] Failed to publish TranslationCompletedEvent");
                        }
                    }

                    // 🔥 [DIAGNOSTIC] 翻訳品質評価診断イベント
                    var isSameLanguage = originalText == translatedText;
                    var textSimilarity = isSameLanguage ? 1.0 : 0.0;
                    var qualityScore = isSameLanguage ? 0.0 : 0.5; // 辞書翻訳は中程度の品質

                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "TranslationQualityCheck",
                        IsSuccess = !isSameLanguage, // 同一言語の場合は失敗
                        ProcessingTimeMs = (long)translationElapsed.TotalMilliseconds,
                        SessionId = translationId,
                        Severity = isSameLanguage ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                        Message = isSameLanguage
                            ? $"翻訳品質問題: 同一言語検出 - 翻訳が実行されていません"
                            : $"翻訳品質評価: スコア={qualityScore:F2}",
                        Metrics = new Dictionary<string, object>
                        {
                            { "OriginalText", originalText },
                            { "TranslatedText", translatedText },
                            { "SourceLanguage", sourceCode },
                            { "TargetLanguage", targetCode },
                            { "IsSameLanguage", isSameLanguage },
                            { "TextSimilarity", textSimilarity },
                            { "QualityScore", qualityScore },
                            { "TranslationMethod", "string_replacement" },
                            { "IsActualTranslation", !isSameLanguage }
                        }
                    }).ConfigureAwait(false);

                    // 🔥 [DIAGNOSTIC] 翻訳実行診断イベント
                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "TranslationExecution",
                        IsSuccess = !isSameLanguage,
                        ProcessingTimeMs = (long)translationElapsed.TotalMilliseconds,
                        SessionId = translationId,
                        Severity = isSameLanguage ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                        Message = isSameLanguage
                            ? $"翻訳実行失敗: 辞書にない文字のため原文をそのまま返却"
                            : $"翻訳実行成功: 辞書翻訳により変換完了",
                        Metrics = new Dictionary<string, object>
                        {
                            { "UsedEngine", "DictionaryTranslation" },
                            { "OriginalText", originalText },
                            { "TranslatedText", translatedText },
                            { "TranslationMethod", "string_replacement" },
                            { "ProcessingTimeMs", (long)translationElapsed.TotalMilliseconds },
                            { "IsActualTranslation", !isSameLanguage },
                            { "DictionaryHit", !isSameLanguage }
                        }
                    }).ConfigureAwait(false);

                    _logger?.LogDebug($"🌍 翻訳処理完了: '{translatedText}'");
                }
                catch (Exception translationEx)
                {
                    // 🔥 [DIAGNOSTIC] 翻訳エラー診断イベント
                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "TranslationExecution",
                        IsSuccess = false,
                        ProcessingTimeMs = 0,
                        ErrorMessage = translationEx.Message,
                        SessionId = translationId,
                        Severity = DiagnosticSeverity.Error,
                        Message = $"翻訳実行エラー: {translationEx.GetType().Name} - {translationEx.Message}",
                        Metrics = new Dictionary<string, object>
                        {
                            { "ErrorType", translationEx.GetType().Name },
                            { "OriginalText", originalText },
                            { "AttemptedTranslationMethod", "nllb200_timeout_fallback" }
                        }
                    }).ConfigureAwait(false);

                    _logger?.LogDebug($"⚠️ 翻訳エラー: {translationEx.Message}");
                    _logger?.LogWarning(translationEx, "翻訳処理でエラーが発生しました");
                    translatedText = $"翻訳エラー: {translationEx.Message}";
                }
            }
            else
            {
                // テキスト未検出時は翻訳結果を表示しない（UI上で空表示となる）
                translatedText = string.Empty;

                // 🔥 [DIAGNOSTIC] 空テキスト診断イベント
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationExecution",
                    IsSuccess = false,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Warning,
                    Message = "翻訳スキップ: OCRでテキストが検出されませんでした",
                    Metrics = new Dictionary<string, object>
                    {
                        { "SkipReason", "no_text_detected" },
                        { "OriginalText", originalText },
                        { "TranslatedText", translatedText }
                    }
                }).ConfigureAwait(false);
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
                    ProcessingTimeMs = (long)processingTime.TotalMilliseconds,
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // [Issue #402] Stop操作によるキャンセルは上位に伝播（ERROR/WARNログ不要）
            _logger?.LogDebug("翻訳処理がキャンセルされました: TranslationId={TranslationId}", translationId);
            throw;
        }
#pragma warning disable CA1031 // 翻訳処理のエラーハンドリングでアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;

            // 🔥 [DIAGNOSTIC] 翻訳全体失敗診断イベント
            try
            {
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationOverall",
                    IsSuccess = false,
                    ProcessingTimeMs = (long)processingTime.TotalMilliseconds,
                    ErrorMessage = ex.Message,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Critical,
                    Message = $"翻訳処理全体でエラー: {ex.GetType().Name} - {ex.Message}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "ErrorType", ex.GetType().Name },
                        { "ProcessingTimeMs", (long)processingTime.TotalMilliseconds },
                        { "StackTrace", ex.StackTrace ?? "N/A" }
                    }
                }).ConfigureAwait(false);
            }
            catch
            {
                // 診断イベント発行失敗は無視（元の例外を優先）
            }

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
                _logger?.LogDebug($"🚫 OCRエラーのため翻訳結果を発行せず: ID={translationId}, Error={ex.Message}");

                // OCRエラーはステータス更新のみ行い、翻訳結果は発行しない
                PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"OCRエラー: {ex.Message}");

                // OCRエラーの場合は例外を再スローして、上位でキャッチさせる
                throw;
            }

            // その他のエラーの場合は従来通り翻訳結果として返す
            _logger?.LogDebug($"⚠️ 一般的な翻訳エラー、結果として発行: ID={translationId}, Error={ex.Message}");
            return new TranslationResult
            {
                Id = translationId,
                Mode = mode,
                OriginalText = string.Empty,
                TranslatedText = $"翻訳エラー: {ex.Message}",
                TargetLanguage = LanguageCodeConverter.ToLanguageCode(_settingsService.GetValue("UI:TranslationLanguage", "英語")),
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
    /// 辞書サービスを使用した翻訳（フォールバック付き）
    /// </summary>
    private async Task<string> TranslateWithNLLBEngineAsync(string text, string sourceLanguage, string targetLanguage)
    {
        try
        {
            // 🚀 [SIMPLIFIED] 直接ITranslationServiceを使用してNLLB-200翻訳を実行
            // ファクトリーパターンは不要（DefaultTranslationServiceが自動選択）

            _logger?.LogTrace("🤖 NLLB-200翻訳エンジンで翻訳開始: '{Text}' ({SourceLang} -> {TargetLang})",
                text, sourceLanguage, targetLanguage);

            // 🚀 実際の翻訳処理を実行（辞書翻訳削除後の正しい実装）
            _logger?.LogTrace("🤖 ITranslationServiceで実際の翻訳を実行: '{Text}' ({SourceLang} -> {TargetLang})",
                text, sourceLanguage, targetLanguage);

            // 実際の翻訳サービスを使用してNLLB-200翻訳を実行
            var sourceLang = Language.FromCode(sourceLanguage);
            var targetLang = Language.FromCode(targetLanguage);

            var response = await _translationService.TranslateAsync(text, sourceLang, targetLang);
            var translatedText = response.TranslatedText;

            // 翻訳が成功した場合（元のテキストと異なる場合）
            if (!string.Equals(text, translatedText, StringComparison.Ordinal))
            {
                _logger?.LogTrace("✅ NLLB-200翻訳成功: '{Text}' -> '{Translation}' ({SourceLang} -> {TargetLang})",
                    text, translatedText, sourceLanguage, targetLanguage);
                return translatedText!;
            }

            _logger?.LogTrace("🔄 NLLB-200翻訳結果が元のテキストと同じ: '{Text}'", text);
            return translatedText!;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ NLLB-200翻訳でエラーが発生: '{Text}' ({SourceLang} -> {TargetLang})",
                text, sourceLanguage, targetLanguage);

            // エラー時は空文字列を返却（何も表示しない）
            return string.Empty;
        }
    }

    // 🗑️ [REMOVED] 辞書翻訳メソッドを削除 - NLLB-200 AI翻訳エンジンに統合完了

    #endregion

    #region Issue #290: Fork-Join並列実行（OCR || Cloud AI翻訳）

    /// <summary>
    /// Fork-Join並列実行の結果
    /// </summary>
    private sealed record ForkJoinResult(
        OcrResults OcrResults,
        FallbackTranslationResult? CloudResult,
        TimeSpan OcrDuration,
        TimeSpan? CloudDuration);

    /// <summary>
    /// Cloud AI並列実行が利用可能かどうかを判定
    /// </summary>
    /// <returns>Pro/Premiaプランでセッションが有効な場合はtrue</returns>
    private bool IsCloudAiParallelExecutionAvailable()
    {
        // FallbackOrchestratorが登録されていない場合は並列実行不可
        if (_fallbackOrchestrator == null)
        {
            _logger?.LogInformation("[Issue #290] FallbackOrchestrator未登録 - 並列実行無効");
            return false;
        }

        // LicenseManagerが登録されていない場合は並列実行不可
        if (_licenseManager == null)
        {
            _logger?.LogInformation("[Issue #290] LicenseManager未登録 - 並列実行無効");
            return false;
        }

        // Cloud AI翻訳機能が利用可能かチェック
        if (!_licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation))
        {
            _logger?.LogInformation("[Issue #290] CloudAiTranslation機能が無効 - 並列実行無効");
            return false;
        }

        // セッションが有効かチェック
        var sessionId = _licenseManager.CurrentState.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger?.LogInformation("[Issue #290] セッションID未設定 - 並列実行無効");
            return false;
        }

        // FallbackOrchestratorの状態をチェック
        var fallbackStatus = _fallbackOrchestrator.GetCurrentStatus();
        if (!fallbackStatus.PrimaryAvailable && !fallbackStatus.SecondaryAvailable)
        {
            _logger?.LogInformation("[Issue #290] Cloud AI翻訳サービス利用不可 - 並列実行無効");
            return false;
        }

        _logger?.LogInformation("[Issue #290] Cloud AI並列実行が利用可能");
        return true;
    }

    /// <summary>
    /// OCRとCloud AI翻訳をFork-Join方式で並列実行
    /// </summary>
    /// <param name="image">キャプチャ画像</param>
    /// <param name="translationId">翻訳ID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>Fork-Join実行結果</returns>
    private async Task<ForkJoinResult> ExecuteForkJoinOcrAndCloudAsync(
        IImage image,
        string translationId,
        CancellationToken cancellationToken)
    {
        var ocrStopwatch = new Stopwatch();
        var cloudStopwatch = new Stopwatch();

        _logger?.LogInformation("🚀 [Issue #290] Fork-Join並列実行開始: ID={TranslationId}", translationId);

        // OCRタスク定義
        async Task<OcrResults> ExecuteOcrAsync()
        {
            ocrStopwatch.Start();
            try
            {
                return await _ocrEngine!.RecognizeAsync(image, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ocrStopwatch.Stop();
            }
        }

        // Cloud AI翻訳タスク定義
        async Task<FallbackTranslationResult?> ExecuteCloudTranslationAsync()
        {
            cloudStopwatch.Start();
            try
            {
                // 画像データをBase64エンコード
                var imageMemory = image.GetImageMemory();
                var imageBase64 = Convert.ToBase64String(imageMemory.Span);

                // 翻訳設定を取得
                var translationSettings = await _settingsService.GetAsync<CoreTranslationSettings>()
                    .ConfigureAwait(false);
                var targetLanguage = translationSettings?.DefaultTargetLanguage ?? "ja";

                // セッショントークンを取得
                var sessionToken = _licenseManager!.CurrentState.SessionId;
                if (string.IsNullOrEmpty(sessionToken))
                {
                    _logger?.LogWarning("[Issue #290] セッショントークンが空 - Cloud翻訳スキップ");
                    return null;
                }

                // ImageTranslationRequestを作成
                var request = new ImageTranslationRequest
                {
                    RequestId = translationId,
                    ImageBase64 = imageBase64,
                    MimeType = "image/png",
                    TargetLanguage = targetLanguage,
                    Width = image.Width,
                    Height = image.Height,
                    SessionToken = sessionToken
                };

                // タイムアウト付きでCloud翻訳を実行
                using var cloudCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cloudCts.CancelAfter(TimeSpan.FromSeconds(30));

                return await _fallbackOrchestrator!.TranslateWithFallbackAsync(request, cloudCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("[Issue #290] Cloud翻訳がキャンセルされました");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Issue #290] Cloud翻訳でエラー発生（OCR結果のみ使用）");
                return null;
            }
            finally
            {
                cloudStopwatch.Stop();
            }
        }

        // 🚀 Fork: 両タスクを同時に開始（awaitせずに即座に開始）
        var ocrTask = ExecuteOcrAsync();
        var cloudTask = ExecuteCloudTranslationAsync();

        // Join: 両タスクの完了を待機
        await Task.WhenAll(ocrTask, cloudTask).ConfigureAwait(false);

        // 結果を取得
        var ocrResults = await ocrTask.ConfigureAwait(false);
        var cloudResult = await cloudTask.ConfigureAwait(false);

        _logger?.LogInformation(
            "🚀 [Issue #290] Fork-Join完了: OCR={OcrMs}ms ({OcrCount}テキスト), Cloud={CloudMs}ms (成功={CloudSuccess})",
            ocrStopwatch.ElapsedMilliseconds,
            ocrResults.TextRegions.Count,
            cloudStopwatch.ElapsedMilliseconds,
            cloudResult?.IsSuccess ?? false);

        return new ForkJoinResult(
            ocrResults,
            cloudResult,
            ocrStopwatch.Elapsed,
            cloudStopwatch.Elapsed);
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

        // 設定変更イベントの購読解除
        if (_unifiedSettingsService != null)
        {
            _unifiedSettingsService.SettingsChanged -= OnTranslationSettingsChanged;
        }

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

        // [Issue #436] インフライト翻訳タスクをクリア
        lock (_translationInFlightLock)
        {
            _translationInFlightTask = null;
        }

        _disposed = true;

        _logger?.LogDebug("TranslationOrchestrationServiceを破棄しました");
    }

    #endregion


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
                _logger?.LogDebug($"🔄 WindowsImageAdapterから直接バイト配列を取得");
                return await adapter.ToByteArrayAsync().ConfigureAwait(false);
            }

            // 方法2: リフレクションでToByteArrayAsyncを呼び出し
            var imageType = image.GetType();
            var toByteArrayMethod = imageType.GetMethod("ToByteArrayAsync");
            if (toByteArrayMethod != null)
            {
                _logger?.LogDebug($"🔄 リフレクションでToByteArrayAsyncを呼び出し");
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
                    _logger?.LogDebug($"🔄 Streamプロパティから変換");
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
                    _logger?.LogDebug($"🔄 Dataプロパティから変換");
                    return data;
                }
            }

            // 最後の手段: ToString()でデバッグ情報を取得
            var debugInfo = $"Image Debug Info: Type={imageType.Name}, Width={image.Width}, Height={image.Height}";
            _logger?.LogDebug($"⚠️ 画像バイト変換失敗 - {debugInfo}");
            return System.Text.Encoding.UTF8.GetBytes(debugInfo);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"❌ 画像バイト変換中にエラー: {ex.Message}");
            var errorInfo = $"Image Conversion Error: {ex.Message}, Type={image.GetType().Name}";
            return System.Text.Encoding.UTF8.GetBytes(errorInfo);
        }
    }

    /// <summary>
    /// 🔥 [Issue #293] 投機的OCRキャッシュ照合用の画像ハッシュを計算
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>画像ハッシュ（16文字）</returns>
    private static async Task<string> ComputeImageHashForCacheAsync(IImage image, CancellationToken cancellationToken)
    {
        try
        {
            // 画像をバイト配列に変換してサンプリングハッシュ
            var data = await image.ToByteArrayAsync().ConfigureAwait(false);

            if (data == null || data.Length == 0)
                return Guid.NewGuid().ToString("N")[..16];

            cancellationToken.ThrowIfCancellationRequested();

            // サンプリング（1024バイトを等間隔で取得）
            var sampleSize = Math.Min(1024, data.Length);
            var sample = new byte[sampleSize];
            var step = Math.Max(1, data.Length / sampleSize);

            for (int i = 0, j = 0; i < sampleSize && j < data.Length; i++, j += step)
            {
                sample[i] = data[j];
            }

            // SHA256ハッシュ（先頭16文字のみ使用）
            var hash = System.Security.Cryptography.SHA256.HashData(sample);
            return Convert.ToHexString(hash)[..16];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Guid.NewGuid().ToString("N")[..16];
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

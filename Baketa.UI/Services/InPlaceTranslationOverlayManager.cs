using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Utilities;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Geometry;
using Baketa.UI.Factories;
using Baketa.UI.Services.Monitor;
using Baketa.UI.Views.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// インプレース翻訳オーバーレイの管理サービス
/// Google翻訳カメラのような、元テキストを翻訳テキストで置き換える表示を管理
/// UltraThink Phase 11.1: IOverlayPositioningService統合による精密位置調整
/// </summary>
/// <summary>
/// Phase 4.1: Factory Pattern適用によるオーバーレイ管理の専門化
/// </summary>
public class InPlaceTranslationOverlayManager(
    IEventAggregator eventAggregator,
    IOverlayPositioningService overlayPositioningService,
    IMonitorManager monitorManager,
    IAdvancedMonitorService advancedMonitorService,
    IInPlaceOverlayFactory overlayFactory,
    IOverlayCoordinateTransformer coordinateTransformer,
    IOverlayDiagnosticService diagnosticService,
    IOverlayCollectionManager collectionManager,
    ILogger<InPlaceTranslationOverlayManager> logger) : IInPlaceTranslationOverlayManager, IEventProcessor<OverlayUpdateEvent>, IDisposable
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly IOverlayPositioningService _overlayPositioningService = overlayPositioningService ?? throw new ArgumentNullException(nameof(overlayPositioningService));
    private readonly IMonitorManager _monitorManager = monitorManager ?? throw new ArgumentNullException(nameof(monitorManager));
    private readonly IAdvancedMonitorService _advancedMonitorService = advancedMonitorService ?? throw new ArgumentNullException(nameof(advancedMonitorService));

    // Phase 4.1: 専門サービスによる責任分離
    private readonly IInPlaceOverlayFactory _overlayFactory = overlayFactory ?? throw new ArgumentNullException(nameof(overlayFactory));
    private readonly IOverlayCoordinateTransformer _coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    private readonly IOverlayDiagnosticService _diagnosticService = diagnosticService ?? throw new ArgumentNullException(nameof(diagnosticService));
    private readonly IOverlayCollectionManager _collectionManager = collectionManager ?? throw new ArgumentNullException(nameof(collectionManager));

    private readonly ILogger<InPlaceTranslationOverlayManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    // 🚑 Phase 13: 重複防止フィルター実装（Gemini推奨のReactive Extensions + ハッシュベース）
    private readonly ConcurrentDictionary<string, DateTime> _recentTranslations = new();
    private readonly TimeSpan _duplicatePreventionWindow = TimeSpan.FromSeconds(2);
    
    private bool _isInitialized;
    private bool _disposed;
    private readonly object _initializeLock = new();

    /// <summary>
    /// インプレースオーバーレイマネージャーを初期化
    /// </summary>
    public async Task InitializeAsync()
    {
        Console.WriteLine($"🔧 InPlaceTranslationOverlayManager.InitializeAsync開始 - _isInitialized: {_isInitialized}, _disposed: {_disposed}");
        
        lock (_initializeLock)
        {
            if (_isInitialized || _disposed)
            {
                Console.WriteLine($"⚠️ インプレースオーバーレイマネージャー初期化スキップ (initialized: {_isInitialized}, disposed: {_disposed})");
                _logger.LogDebug("InPlace overlay manager initialization skipped (initialized: {IsInitialized}, disposed: {IsDisposed})", 
                    _isInitialized, _disposed);
                return;
            }
            
            Console.WriteLine("🔒 インプレースオーバーレイマネージャー初期化ロック取得、実際の初期化を開始");
        }

        try
        {
            _logger.LogDebug("Starting InPlace overlay manager initialization");

            // 初期化完了
            lock (_initializeLock)
            {
                _isInitialized = true;
                Console.WriteLine("🔓 インプレースオーバーレイマネージャー初期化完了フラグ設定");
            }
            
            Console.WriteLine("🎉 InPlaceTranslationOverlayManager.InitializeAsync正常完了");
            _logger.LogInformation("InPlace translation overlay manager initialized successfully");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 InPlaceTranslationOverlayManager.InitializeAsync例外: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            _logger.LogError(ex, "Failed to initialize InPlace translation overlay manager");
            throw;
        }
    }

    /// <summary>
    /// 🚑 Phase 13: テキストハッシュベースの重複検出
    /// Gemini推奨のReactive Extensions活用版
    /// </summary>
    /// <param name="text">翻訳テキスト</param>
    /// <returns>重複防止用ハッシュキー</returns>
    private static string GetTextHash(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
            
        // テキスト内容 + 長さを組み合わせたシンプルなハッシュ
        return $"{text}_{text.Length}".GetHashCode().ToString();
    }

    /// <summary>
    /// 🚑 Phase 13: 重複翻訳結果チェック
    /// 2秒間の重複防止ウィンドウで同一テキストの重複表示を防止
    /// </summary>
    /// <param name="text">翻訳テキスト</param>
    /// <returns>true=表示すべき, false=重複のためスキップ</returns>
    private bool ShouldDisplayOverlay(string text)
    {
        var textHash = GetTextHash(text);
        
        if (string.IsNullOrEmpty(textHash))
        {
            return false; // 空テキストは表示しない
        }
        
        // 重複チェック
        if (_recentTranslations.TryGetValue(textHash, out var lastTime))
        {
            if (DateTime.UtcNow - lastTime < _duplicatePreventionWindow)
            {
                _logger.LogDebug("🚫 [PHASE13] 重複オーバーレイ防止 - Text: {Text}, Hash: {Hash}", 
                    text.Substring(0, Math.Min(50, text.Length)), textHash);
                return false; // 重複表示をスキップ
            }
        }
        
        // 最後の表示時刻を更新
        _recentTranslations[textHash] = DateTime.UtcNow;
        
        // 古いエントリのクリーンアップ（メモリリーク防止）
        CleanupOldTranslationEntries();
        
        return true;
    }

    /// <summary>
    /// 🚑 Phase 13: 古い重複防止エントリのクリーンアップ
    /// メモリ効率化のため、古いエントリを定期的に削除
    /// </summary>
    private void CleanupOldTranslationEntries()
    {
        // パフォーマンス考慮: 100エントリごとにクリーンアップ
        if (_recentTranslations.Count < 100) return;
        
        var cutoffTime = DateTime.UtcNow - _duplicatePreventionWindow.Multiply(2);
        var keysToRemove = new List<string>();
        
        foreach (var kvp in _recentTranslations)
        {
            if (kvp.Value < cutoffTime)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _recentTranslations.TryRemove(key, out _);
        }
        
        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("🧹 [PHASE13] 重複防止エントリクリーンアップ完了 - 削除数: {Count}", keysToRemove.Count);
        }
    }

    /// <summary>
    /// TextChunkのインプレースオーバーレイを表示
    /// 既存のオーバーレイがある場合は更新、ない場合は新規作成
    /// </summary>
    public async Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Phase 4.1: IOverlayDiagnosticServiceに転送
        await _diagnosticService.PublishOverlayStartedAsync(textChunk, sessionId, _isInitialized, _disposed).ConfigureAwait(false);
        
        // STOP押下後の表示を防ぐためのキャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();
        
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        // 初期化後にもう一度キャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();

        // 🔍 [P2_COORDINATE_DEBUG] オーバーレイ表示直前の詳細監視
        Console.WriteLine($"🔍 [P2_COORDINATE_DEBUG] ShowInPlaceOverlayAsync - ChunkId: {textChunk.ChunkId}");
        Console.WriteLine($"🔍 [P2_COORDINATE_DEBUG] 入力ROI座標: X={textChunk.CombinedBounds.X}, Y={textChunk.CombinedBounds.Y}, W={textChunk.CombinedBounds.Width}, H={textChunk.CombinedBounds.Height}");
        Console.WriteLine($"🔍 [P2_COORDINATE_DEBUG] ソースウィンドウハンドル: {textChunk.SourceWindowHandle}");
        Console.WriteLine($"🔍 [P2_COORDINATE_DEBUG] CombinedText: '{textChunk.CombinedText}'");
        Console.WriteLine($"🔍 [P2_COORDINATE_DEBUG] TranslatedText: '{textChunk.TranslatedText}'");
        Console.WriteLine($"🔍 [P2_COORDINATE_DEBUG] CanShowInPlace: {textChunk.CanShowInPlace()}");

        // 🎯 [P2_DPI_DEBUG] DPI・システム情報の詳細監視
        // TODO: Avalonia API調査後にDPI取得実装
        Console.WriteLine($"🎯 [P2_DPI_DEBUG] システムDPIスケール取得予定（API調査中）");

        // 🖥️ [P2_MONITOR_DEBUG] モニター情報の詳細監視
        try
        {
            var monitor = _monitorManager.DetermineOptimalMonitor(textChunk.SourceWindowHandle);
            Console.WriteLine($"🖥️ [P2_MONITOR_DEBUG] 検出モニター: {monitor.Name}");
            Console.WriteLine($"🖥️ [P2_MONITOR_DEBUG] モニター解像度: {monitor.Bounds.Width}x{monitor.Bounds.Height}");
            Console.WriteLine($"🖥️ [P2_MONITOR_DEBUG] モニター位置: X={monitor.Bounds.X}, Y={monitor.Bounds.Y}");
            Console.WriteLine($"🖥️ [P2_MONITOR_DEBUG] 作業領域: モニター情報から取得予定（API調査中）");
        }
        catch (Exception monitorEx)
        {
            Console.WriteLine($"⚠️ [P2_MONITOR_DEBUG] モニター情報取得失敗: {monitorEx.Message}");
        }

        _logger.LogDebug("[P2_COORDINATE_DEBUG] オーバーレイ表示開始 - ChunkId: {ChunkId}, 入力ROI: ({X},{Y},{W},{H}), Handle: {Handle}",
            textChunk.ChunkId, textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y,
            textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height, textChunk.SourceWindowHandle);
        
        // 🚫 [P2_VALIDATION_DEBUG] 表示条件検証の詳細監視
        Console.WriteLine($"🔍 [P2_VALIDATION_DEBUG] === 表示条件検証開始 (ChunkId: {textChunk.ChunkId}) ===");

        var isTranslationValid = TranslationValidator.IsValid(textChunk.TranslatedText, textChunk.CombinedText);
        Console.WriteLine($"🔍 [P2_VALIDATION_DEBUG] 翻訳結果妥当性: {isTranslationValid}");
        Console.WriteLine($"🔍 [P2_VALIDATION_DEBUG] 翻訳テキスト長: {textChunk.TranslatedText?.Length ?? 0}");
        Console.WriteLine($"🔍 [P2_VALIDATION_DEBUG] 元テキスト長: {textChunk.CombinedText?.Length ?? 0}");

        if (!isTranslationValid)
        {
            Console.WriteLine($"🚫 [P2_VALIDATION_DEBUG] 無効な翻訳結果のため表示をスキップ - ChunkId: {textChunk.ChunkId}, 結果: '{textChunk.TranslatedText}'");
            _logger.LogWarning("[P2_VALIDATION_DEBUG] 無効な翻訳結果スキップ - ChunkId: {ChunkId}, TranslatedText: {TranslatedText}, CombinedText: {CombinedText}",
                textChunk.ChunkId, textChunk.TranslatedText ?? "null", textChunk.CombinedText ?? "null");
            return;
        }

        var canShowInPlace = textChunk.CanShowInPlace();
        Console.WriteLine($"🔍 [P2_VALIDATION_DEBUG] インプレース表示可能: {canShowInPlace}");

        if (!canShowInPlace)
        {
            Console.WriteLine($"🚫 [P2_VALIDATION_DEBUG] インプレース表示条件未満 - ChunkId: {textChunk.ChunkId}");
            _logger.LogWarning("[P2_VALIDATION_DEBUG] インプレース表示条件を満たしていません - ChunkId: {ChunkId}, Log: {InPlaceLog}",
                textChunk.ChunkId, textChunk.ToInPlaceLogString());
            return;
        }

        Console.WriteLine($"✅ [P2_VALIDATION_DEBUG] === 表示条件検証合格 (ChunkId: {textChunk.ChunkId}) ===");

        try
        {
            // オーバーレイ処理直前のキャンセレーションチェック
            cancellationToken.ThrowIfCancellationRequested();
            
            // Phase 4.1: IOverlayCollectionManagerに転送
            var hasExistingOverlay = _collectionManager.TryGetOverlay(textChunk.ChunkId, out var existingOverlay);
            Console.WriteLine($"🔍 [P2_OVERLAY_BRANCH] === オーバーレイ処理分岐 (ChunkId: {textChunk.ChunkId}) ===");
            Console.WriteLine($"🔍 [P2_OVERLAY_BRANCH] 既存オーバーレイ存在: {hasExistingOverlay}");
            Console.WriteLine($"🔍 [P2_OVERLAY_BRANCH] 総アクティブオーバーレイ数: {_collectionManager.ActiveOverlayCount}");

            if (hasExistingOverlay && existingOverlay != null)
            {
                Console.WriteLine($"🔄 [P2_OVERLAY_BRANCH] 既存オーバーレイ更新処理開始 - ChunkId: {textChunk.ChunkId}");

                // 既存オーバーレイの状態確認
                try
                {
                    Console.WriteLine($"🔍 [P2_OVERLAY_BRANCH] 既存オーバーレイ状態: Visible={existingOverlay.IsVisible}, Position=({existingOverlay.Position.X},{existingOverlay.Position.Y})");
                }
                catch (Exception stateEx)
                {
                    Console.WriteLine($"⚠️ [P2_OVERLAY_BRANCH] 既存オーバーレイ状態確認失敗: {stateEx.Message}");
                }

                // 既存のオーバーレイを更新
                await existingOverlay.UpdateInPlaceContentAsync(textChunk, cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"✅ [P2_OVERLAY_BRANCH] 既存オーバーレイ更新完了 - ChunkId: {textChunk.ChunkId}");
                _logger.LogInformation("[P2_OVERLAY_BRANCH] 既存インプレースオーバーレイ更新完了 - ChunkId: {ChunkId}", textChunk.ChunkId);
            }
            else
            {
                Console.WriteLine($"🆕 [P2_OVERLAY_BRANCH] 新規オーバーレイ作成処理開始 - ChunkId: {textChunk.ChunkId}");

                // 新規インプレースオーバーレイを作成・表示
                await CreateAndShowNewInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"✅ [P2_OVERLAY_BRANCH] 新規オーバーレイ作成完了 - ChunkId: {textChunk.ChunkId}");
            }

            // Phase 4.1: IOverlayDiagnosticServiceに転送
            var isUpdate = _collectionManager.TryGetOverlay(textChunk.ChunkId, out _);
            await _diagnosticService.PublishOverlaySuccessAsync(
                textChunk, sessionId, stopwatch.ElapsedMilliseconds, _collectionManager.ActiveOverlayCount, isUpdate)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Phase 4.1: IOverlayDiagnosticServiceに転送
            try
            {
                await _diagnosticService.PublishOverlayFailedAsync(
                    textChunk, sessionId, stopwatch.ElapsedMilliseconds, ex, _isInitialized, _disposed, _collectionManager.ActiveOverlayCount)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 診断イベント発行失敗は無視（元の例外を優先）
            }

            _logger.LogError(ex, "インプレース表示エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 新規インプレースオーバーレイを作成して表示
    /// </summary>
    private async Task CreateAndShowNewInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken)
    {
        // オーバーレイ作成前のキャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();
        
        InPlaceTranslationOverlayWindow? newOverlay = null;
        
        try
        {
            // 衝突回避のための既存オーバーレイ境界情報を取得
            var existingBounds = GetExistingOverlayBounds();
            
            // UIスレッドでオーバーレイウィンドウを作成
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Console.WriteLine($"🏗️ 新規インプレースオーバーレイ作成開始 - ChunkId: {textChunk.ChunkId}");
                
                newOverlay = new InPlaceTranslationOverlayWindow
                {
                    ChunkId = textChunk.ChunkId,
                    OriginalText = textChunk.CombinedText,
                    TranslatedText = textChunk.TranslatedText,
                    TargetBounds = textChunk.CombinedBounds,
                    SourceWindowHandle = textChunk.SourceWindowHandle
                };
                
                Console.WriteLine($"✅ 新規インプレースオーバーレイ作成完了 - ChunkId: {textChunk.ChunkId}");
                
            }, DispatcherPriority.Normal, cancellationToken);

            if (newOverlay != null)
            {
                // オーバーレイ表示直前のキャンセレーションチェック
                cancellationToken.ThrowIfCancellationRequested();
                
                // 🎯 [P2_COORDINATE_TRANSFORM] IOverlayPositioningServiceによる精密座標変換詳細監視
                System.Drawing.Point optimalPosition;
                System.Drawing.Point avaloniaCompensatedPosition;
                MonitorInfo actualMonitor;
                try
                {
                    var overlaySize = textChunk.GetOverlaySize();
                    var options = new OverlayPositioningOptions(); // デフォルト設定を使用

                    // 🔍 [P2_COORDINATE_TRANSFORM] 座標変換前の詳細情報
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] === 座標変換開始 (ChunkId: {textChunk.ChunkId}) ===");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 入力ROI座標: ({textChunk.CombinedBounds.X},{textChunk.CombinedBounds.Y}) サイズ: {textChunk.CombinedBounds.Width}x{textChunk.CombinedBounds.Height}");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 計算オーバーレイサイズ: {overlaySize.Width}x{overlaySize.Height}");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 既存オーバーレイ数: {existingBounds.Count}");

                    // 🎯 Phase 11.4: 実際のモニター情報取得と詳細ログ
                    var monitorResult = _monitorManager.DetermineOptimalMonitor(textChunk.SourceWindowHandle);
                    if (monitorResult == null)
                    {
                        throw new InvalidOperationException($"モニター情報取得失敗 - ChunkId: {textChunk.ChunkId}");
                    }
                    actualMonitor = monitorResult;

                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 対象モニター: {actualMonitor.Name}");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] モニター境界: ({actualMonitor.Bounds.X},{actualMonitor.Bounds.Y}) サイズ: {actualMonitor.Bounds.Width}x{actualMonitor.Bounds.Height}");
                    // TODO: [P2_COORDINATE_DEBUG] WorkingAreaプロパティAPI実装後に有効化
                    // Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 作業領域: ({actualMonitor.WorkingArea.X},{actualMonitor.WorkingArea.Y}) サイズ: {actualMonitor.WorkingArea.Width}x{actualMonitor.WorkingArea.Height}");

                    // 座標変換実行
                    var result = _overlayPositioningService.CalculateOptimalPosition(
                        textChunk, overlaySize, actualMonitor, existingBounds, options);

                    optimalPosition = result.Position;

                    // 🔍 [P2_COORDINATE_TRANSFORM] 座標変換結果の詳細ログ
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] === 座標変換完了 (ChunkId: {textChunk.ChunkId}) ===");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 最終スクリーン座標: ({optimalPosition.X},{optimalPosition.Y})");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 使用戦略: {result.UsedStrategy}");
                    Console.WriteLine($"🔍 [P2_COORDINATE_TRANSFORM] 座標変換量: ΔX={optimalPosition.X - textChunk.CombinedBounds.X}, ΔY={optimalPosition.Y - textChunk.CombinedBounds.Y}");

                    // 🎯 [P2_COORDINATE_VALIDATION] 座標妥当性検証
                    // TODO: [P2_COORDINATE_DEBUG] Contains・WorkingAreaプロパティAPI実装後に有効化
                    var geometryPoint = new Baketa.Core.UI.Geometry.Point(optimalPosition.X, optimalPosition.Y);
                    var isInMonitorBounds = actualMonitor.Bounds.Contains(geometryPoint);
                    // var isInWorkingArea = actualMonitor.WorkingArea.Contains(geometryPoint);
                    Console.WriteLine($"🎯 [P2_COORDINATE_VALIDATION] モニター境界内: {isInMonitorBounds}");

                    if (!isInMonitorBounds)
                    {
                        Console.WriteLine($"⚠️ [P2_COORDINATE_VALIDATION] 警告: 座標がモニター境界外 - 座標: ({optimalPosition.X},{optimalPosition.Y}), モニター境界: {actualMonitor.Bounds}");
                    }

                    _logger.LogInformation("[P2_COORDINATE_TRANSFORM] 座標変換完了 - ChunkId: {ChunkId}, ROI: ({RoiX},{RoiY}→{FinalX},{FinalY}), Strategy: {Strategy}, Monitor: {MonitorName}",
                        textChunk.ChunkId, textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y, optimalPosition.X, optimalPosition.Y, result.UsedStrategy, actualMonitor.Name);

                    // 🖥️ [PHASE1_DPI_COMPENSATION] Avalonia DPI補正適用 (try内で実行)
                    var monitorType = _advancedMonitorService.DetectMonitorType(actualMonitor);
                    var advancedDpiInfo = _advancedMonitorService.GetAdvancedDpiInfo(actualMonitor);

                    avaloniaCompensatedPosition = _advancedMonitorService.CompensateCoordinatesForAvalonia(
                        optimalPosition, advancedDpiInfo);

                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] === Avalonia DPI補正実施 (ChunkId: {textChunk.ChunkId}) ===");
                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] モニター種別: {monitorType}");
                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] Avaloniaスケーリング: {advancedDpiInfo.AvaloniaScaling}");
                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] 補正要否: {advancedDpiInfo.RequiresAvaloniaCompensation}");
                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] 補正係数: {advancedDpiInfo.CompensationFactor}");
                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] 補正前座標: ({optimalPosition.X},{optimalPosition.Y})");
                    Console.WriteLine($"🖥️ [PHASE1_DPI_COMPENSATION] 補正後座標: ({avaloniaCompensatedPosition.X},{avaloniaCompensatedPosition.Y})");

                    _logger.LogInformation("[PHASE1_DPI_COMPENSATION] Avalonia DPI補正完了 - ChunkId: {ChunkId}, MonitorType: {MonitorType}, " +
                        "Before: ({BeforeX},{BeforeY}) → After: ({AfterX},{AfterY}), Factor: {Factor}",
                        textChunk.ChunkId, monitorType, optimalPosition.X, optimalPosition.Y,
                        avaloniaCompensatedPosition.X, avaloniaCompensatedPosition.Y, advancedDpiInfo.CompensationFactor);
                }
                catch (Exception ex)
                {
                    // 精密位置計算またはDPI補正失敗時は基本位置を使用
                    optimalPosition = textChunk.GetBasicOverlayPosition();
                    avaloniaCompensatedPosition = optimalPosition;
                    _logger.LogWarning(ex, "精密位置計算またはDPI補正失敗、基本位置を使用 - ChunkId: {ChunkId}", textChunk.ChunkId);
                }

                // Phase 4.1: IOverlayCollectionManagerに転送
                _collectionManager.AddOverlay(textChunk.ChunkId, newOverlay);

                // 一時的なTextChunkで精密位置調整結果を適用
                var adjustedTextChunk = CreateAdjustedTextChunk(textChunk, optimalPosition);

                // 🎯 [P2_OVERLAY_DISPLAY] オーバーレイ表示前の最終状態確認
                Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] === オーバーレイ表示開始 (ChunkId: {textChunk.ChunkId}) ===");
                Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] 調整後TextChunk座標: ({adjustedTextChunk.CombinedBounds.X},{adjustedTextChunk.CombinedBounds.Y}) サイズ: {adjustedTextChunk.CombinedBounds.Width}x{adjustedTextChunk.CombinedBounds.Height}");
                Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] 表示テキスト: '{adjustedTextChunk.TranslatedText}'");
                Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] newOverlay作成状態: {newOverlay != null}");

                // UIスレッドでオーバーレイ表示を実行
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // Phase 11.1精密位置でインプレース表示を開始
                    await newOverlay.ShowInPlaceOverlayAsync(adjustedTextChunk, cancellationToken).ConfigureAwait(false);
                }, DispatcherPriority.Normal, cancellationToken);

                // 🎯 [P2_OVERLAY_DISPLAY] 表示完了後のオーバーレイウィンドウ状態監視
                Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] === オーバーレイ表示完了 (ChunkId: {textChunk.ChunkId}) ===");

                // UIスレッドでウィンドウ状態を確認
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] ウィンドウ可視性: {newOverlay.IsVisible}");
                        Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] ウィンドウ位置: ({newOverlay.Position.X},{newOverlay.Position.Y})");
                        Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] ウィンドウサイズ: {newOverlay.ClientSize.Width}x{newOverlay.ClientSize.Height}");
                        Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] Topmost設定: {newOverlay.Topmost}");
                        Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] Opacity設定: {newOverlay.Opacity}");
                        Console.WriteLine($"🎯 [P2_OVERLAY_DISPLAY] WindowState: {newOverlay.WindowState}");
                    }
                    catch (Exception displayEx)
                    {
                        Console.WriteLine($"⚠️ [P2_OVERLAY_DISPLAY] ウィンドウ状態取得失敗: {displayEx.Message}");
                    }
                }, DispatcherPriority.Normal, cancellationToken);

                // UIスレッドでIsVisibleを取得してログ出力
                var isVisible = await Dispatcher.UIThread.InvokeAsync(() => newOverlay.IsVisible, DispatcherPriority.Normal, cancellationToken);

                _logger.LogInformation("[P2_OVERLAY_DISPLAY] 新規オーバーレイ表示完了 - ChunkId: {ChunkId}, FinalPosition: ({X},{Y}), Visible: {IsVisible}",
                    textChunk.ChunkId, optimalPosition.X, optimalPosition.Y, isVisible);
            }
            else
            {
                throw new InvalidOperationException("インプレースオーバーレイウィンドウの作成に失敗しました");
            }
        }
        catch (Exception ex)
        {
            // Phase 4.1: IOverlayCollectionManagerに転送
            if (newOverlay != null)
            {
                try
                {
                    _collectionManager.RemoveOverlay(textChunk.ChunkId, out _);
                    newOverlay.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "インプレースオーバーレイクリーンアップエラー - ChunkId: {ChunkId}", textChunk.ChunkId);
                }
            }

            _logger.LogError(ex, "新規インプレースオーバーレイ作成エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }


    /// <summary>
    /// Phase 4.1: IOverlayCollectionManagerに転送
    /// すべてのインプレースオーバーレイを非表示
    /// </summary>
    public async Task HideAllInPlaceOverlaysAsync()
        => await _collectionManager.HideAllOverlaysAsync().ConfigureAwait(false);

    /// <summary>
    /// Phase 4.1: IOverlayCollectionManagerに転送
    /// すべてのインプレースオーバーレイの可視性を切り替え（高速化版）
    /// オーバーレイの削除/再作成ではなく、可視性プロパティのみを変更
    /// </summary>
    public async Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
        => await _collectionManager.SetAllOverlaysVisibilityAsync(visible, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// インプレースオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public async Task ResetAsync()
    {
        Console.WriteLine("🔄 InPlaceTranslationOverlayManager - リセット開始");
        
        await HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
        
        _isInitialized = false;
        
        // 🚑 Phase 13: 重複防止フィルタークリーンアップ
        _recentTranslations.Clear();
        
        Console.WriteLine("✅ InPlaceTranslationOverlayManager - リセット完了");
    }

    /// <summary>
    /// Phase 4.1: IOverlayCollectionManagerに転送
    /// 現在アクティブなインプレースオーバーレイの数を取得
    /// </summary>
    public int ActiveOverlayCount => _collectionManager.ActiveOverlayCount;
    
    /// <summary>
    /// Phase 4.1: IOverlayCollectionManagerに転送
    /// 既存の全てのアクティブオーバーレイの境界情報を取得
    /// 衝突回避計算用
    /// </summary>
    /// <returns>既存オーバーレイの境界リスト</returns>
    private List<Rectangle> GetExistingOverlayBounds() => _collectionManager.GetExistingOverlayBounds();

    /// <summary>
    /// Phase 11.1: 精密位置調整で調整されたTextChunkを作成
    /// 元のTextChunkのプロパティを維持しつつ、表示位置のみをIOverlayPositioningServiceによる精密位置に調整
    /// </summary>
    /// <param name="originalChunk">元のTextChunk</param>
    /// <param name="adjustedPosition">IOverlayPositioningServiceで決定された最適位置</param>
    /// <returns>位置調整されたTextChunk</returns>
    private static TextChunk CreateAdjustedTextChunk(TextChunk originalChunk, System.Drawing.Point adjustedPosition)
    {
        // 元の境界サイズを維持しつつ、位置のみを調整
        var adjustedBounds = new Rectangle(adjustedPosition.X, adjustedPosition.Y, 
            originalChunk.CombinedBounds.Width, originalChunk.CombinedBounds.Height);
        
        // 調整済みTextChunkを作成（元のプロパティを全て継承）
        return new TextChunk
        {
            ChunkId = originalChunk.ChunkId,
            TextResults = originalChunk.TextResults,
            CombinedBounds = adjustedBounds, // 調整済み位置
            CombinedText = originalChunk.CombinedText,
            TranslatedText = originalChunk.TranslatedText,
            SourceWindowHandle = originalChunk.SourceWindowHandle,
            DetectedLanguage = originalChunk.DetectedLanguage,
            CreatedAt = originalChunk.CreatedAt
        };
    }

    /// <summary>
    /// Phase 4.1: IOverlayCollectionManagerに転送
    /// 指定されたChunkIdのオーバーレイを非表示にする（翻訳完了時の原文非表示用）
    /// </summary>
    public async Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
        => await _collectionManager.HideOverlayAsync(chunkId, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// TODO [Phase 4.2]: IOverlayCollectionManagerに高度な領域ベース削除機能を追加後に再実装
    /// 指定されたエリア内の既存オーバーレイを非表示にする（翻訳結果表示時の原文非表示用）
    /// 一時的に無効化 - Phase 4.1リファクタリング範囲外のため将来実装
    /// </summary>
    public Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("HideOverlaysInAreaAsync is temporarily disabled - Phase 4.2 will reimplement using IOverlayCollectionManager");
        return Task.CompletedTask;
    }

    /// <summary>
    /// イベントプロセッサの優先度
    /// </summary>
    public int Priority => 100; // UI関連なので高い優先度

    /// <summary>
    /// 同期実行フラグ（UIスレッドでの実行が必要なため非同期）
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// OverlayUpdateEventを処理するハンドラ（優先度対応版）
    /// </summary>
    public async Task HandleAsync(OverlayUpdateEvent eventData) => await HandleAsync(eventData, CancellationToken.None);

    /// <summary>
    /// OverlayUpdateEventを処理して翻訳結果をオーバーレイ表示
    /// </summary>
    /// <param name="eventData">オーバーレイ更新イベントデータ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    public async Task HandleAsync(OverlayUpdateEvent eventData, CancellationToken cancellationToken = default)
    {
        if (eventData == null)
        {
            _logger.LogWarning("OverlayUpdateEvent is null - skipping overlay update");
            return;
        }

        // 🚫 翻訳アプリケーションとして、OCR結果（原文）は表示せず翻訳結果のみ表示
        if (!eventData.IsTranslationResult)
        {
            Console.WriteLine($"🚫 [TRANSLATION_ONLY] OCR結果表示をスキップ - Text: '{eventData.Text}' (翻訳結果のみ表示ポリシー)");
            _logger.LogDebug("OCR結果表示をスキップ - 翻訳結果のみ表示: Text={Text}", eventData.Text);
            return;
        }

        // 🚫 [DUPLICATE_DISPLAY_FIX] 空文字の翻訳結果は表示しない（同言語スキップなど）
        if (string.IsNullOrWhiteSpace(eventData.Text))
        {
            Console.WriteLine($"🚫 [EMPTY_TEXT_SKIP] 空文字の翻訳結果をスキップ - Text: '{eventData.Text}' (非表示設定)");
            _logger.LogDebug("空文字の翻訳結果をスキップ: Text={Text}", eventData.Text);
            return;
        }

        // 🚑 Phase 13: 重複防止フィルターチェック
        if (!ShouldDisplayOverlay(eventData.Text))
        {
            Console.WriteLine($"🚫 [PHASE13] 重複翻訳結果のため表示をスキップ - Text: '{eventData.Text?.Substring(0, Math.Min(50, eventData.Text?.Length ?? 0))}'");
            _logger.LogDebug("🚑 [PHASE13] 重複翻訳結果のため表示をスキップ - Text: {Text}", eventData.Text);
            return;
        }

        try
        {
            // 🔍 [P2_EVENT_DEBUG] OverlayUpdateEvent処理の詳細監視
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] === OverlayUpdateEvent処理開始 ===");
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] イベントタイプ: 翻訳結果 (IsTranslationResult: {eventData.IsTranslationResult})");
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] 表示テキスト: '{eventData.Text}'");
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] 表示エリア: ({eventData.DisplayArea.X},{eventData.DisplayArea.Y}) サイズ: {eventData.DisplayArea.Width}x{eventData.DisplayArea.Height}");
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] 元テキスト: '{eventData.OriginalText}'");
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] 言語: {eventData.SourceLanguage} → {eventData.TargetLanguage}");
            Console.WriteLine($"🔍 [P2_EVENT_DEBUG] マネージャー状態: 初期化={_isInitialized}, 破棄={_disposed}");

            _logger.LogInformation("[P2_EVENT_DEBUG] OverlayUpdateEvent処理開始 - Text: {Text}, Area: ({X},{Y},{W},{H}), IsTranslation: {IsTranslation}",
                eventData.Text, eventData.DisplayArea.X, eventData.DisplayArea.Y, eventData.DisplayArea.Width, eventData.DisplayArea.Height, eventData.IsTranslationResult);

            // UIスレッドでオーバーレイ表示処理を実行
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!_isInitialized)
                {
                    Console.WriteLine("⚠️ [OVERLAY] オーバーレイマネージャーが初期化されていません - 初期化を実行");
                    _logger.LogWarning("オーバーレイマネージャーが初期化されていないため初期化を実行");
                    await InitializeAsync().ConfigureAwait(false);
                }

                // 🔍 [P2_EVENT_TEXTCHUNK] TextChunk作成過程の詳細監視
                var chunkId = eventData.GetHashCode();
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] === TextChunk作成開始 ===");
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] 生成ChunkId: {chunkId}");
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] DisplayArea→CombinedBounds: ({eventData.DisplayArea.X},{eventData.DisplayArea.Y}) サイズ: {eventData.DisplayArea.Width}x{eventData.DisplayArea.Height}");
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] SourceLanguage: {eventData.SourceLanguage ?? "null"}");
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] IsTranslationResult: {eventData.IsTranslationResult}");

                // オーバーレイ表示のためにTextChunkを作成
                var textChunk = new TextChunk
                {
                    ChunkId = chunkId, // イベントデータのハッシュをチャンクIDとして使用
                    TextResults = [], // 空のリスト（OverlayUpdateEventからは個別結果が得られない）
                    CombinedBounds = eventData.DisplayArea,
                    CombinedText = eventData.OriginalText ?? string.Empty, // 元テキスト（表示には使用しない）
                    SourceWindowHandle = IntPtr.Zero, // OverlayUpdateEventからは取得できない
                    DetectedLanguage = eventData.SourceLanguage ?? "en",
                    // 🚫 [TRANSLATION_ONLY] 翻訳結果のみ設定（OCR結果は表示しない）
                    TranslatedText = eventData.IsTranslationResult ? eventData.Text : string.Empty
                };

                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] === TextChunk作成完了 ===");
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] 最終TranslatedText: '{textChunk.TranslatedText}'");
                Console.WriteLine($"🔍 [P2_EVENT_TEXTCHUNK] CanShowInPlace予測: {!string.IsNullOrWhiteSpace(textChunk.TranslatedText)}");

                Console.WriteLine($"🔍 [TRANSLATION_FILTER] IsTranslationResult: {eventData.IsTranslationResult}, Text: '{eventData.Text}'");
                Console.WriteLine($"🔍 [TRANSLATION_FILTER] TranslatedText設定: '{textChunk.TranslatedText}'");
                
                // 🎯 翻訳結果のみ表示（OCR結果は事前にフィルタリング済み）
                Console.WriteLine($"🎯 [TRANSLATION] 翻訳結果表示 - Area: {eventData.DisplayArea}");
                await ShowInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);

                Console.WriteLine($"✅ [OVERLAY] オーバーレイ表示完了 - ChunkId: {textChunk.ChunkId}");
                _logger.LogDebug("OverlayUpdateEvent処理完了 - ChunkId: {ChunkId}", textChunk.ChunkId);
            });
        }
        catch (Exception ex)
        {
            // 🚨 [P2_ERROR_DEBUG] 例外の詳細分析
            Console.WriteLine($"🚨 [P2_ERROR_DEBUG] === OverlayUpdateEvent処理エラー ===");
            Console.WriteLine($"🚨 [P2_ERROR_DEBUG] 例外タイプ: {ex.GetType().Name}");
            Console.WriteLine($"🚨 [P2_ERROR_DEBUG] エラーメッセージ: {ex.Message}");
            Console.WriteLine($"🚨 [P2_ERROR_DEBUG] 発生箇所: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            Console.WriteLine($"🚨 [P2_ERROR_DEBUG] 処理中イベント: Text='{eventData?.Text}', Area=({eventData?.DisplayArea.X},{eventData?.DisplayArea.Y})");
            Console.WriteLine($"🚨 [P2_ERROR_DEBUG] マネージャー状態: 初期化={_isInitialized}, 破棄={_disposed}, アクティブオーバーレイ数={_collectionManager.ActiveOverlayCount}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"🚨 [P2_ERROR_DEBUG] 内部例外: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }

            _logger.LogError(ex, "[P2_ERROR_DEBUG] OverlayUpdateEvent処理中にエラーが発生 - EventText: {EventText}, Area: ({X},{Y},{W},{H}), ManagerState: Init={IsInit}/Disposed={IsDisposed}",
                eventData?.Text, eventData?.DisplayArea.X, eventData?.DisplayArea.Y, eventData?.DisplayArea.Width, eventData?.DisplayArea.Height, _isInitialized, _disposed);
        }
    }


    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // Phase 4.1: オーバーレイクリーンアップはIOverlayCollectionManagerに委任
            // Dispose時は同期実行が必要なため、HideAllOverlaysAsyncを使用せずに直接実行
            // （既にDisposeパターンで適切に処理される）
            
            // 🚑 Phase 13: 重複防止フィルタークリーンアップ
            _recentTranslations.Clear();
            
            _isInitialized = false;
            _disposed = true;
            
            _logger.LogDebug("InPlace translation overlay manager disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing InPlace translation overlay manager");
        }
        
        GC.SuppressFinalize(this);
    }
}

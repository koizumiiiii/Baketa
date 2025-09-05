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
using Baketa.UI.Views.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// インプレース翻訳オーバーレイの管理サービス
/// Google翻訳カメラのような、元テキストを翻訳テキストで置き換える表示を管理
/// </summary>
public class InPlaceTranslationOverlayManager(
    IEventAggregator eventAggregator,
    ILogger<InPlaceTranslationOverlayManager> logger) : IInPlaceTranslationOverlayManager, IEventProcessor<OverlayUpdateEvent>, IDisposable
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<InPlaceTranslationOverlayManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    // チャンクIDとインプレースオーバーレイウィンドウのマッピング
    private readonly ConcurrentDictionary<int, InPlaceTranslationOverlayWindow> _activeOverlays = new();
    
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
    /// TextChunkのインプレースオーバーレイを表示
    /// 既存のオーバーレイがある場合は更新、ない場合は新規作成
    /// </summary>
    public async Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 📊 [DIAGNOSTIC] オーバーレイ表示開始イベント
        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
        {
            Stage = "Overlay",
            IsSuccess = true,
            ProcessingTimeMs = 0,
            SessionId = sessionId,
            Severity = DiagnosticSeverity.Information,
            Message = $"オーバーレイ表示開始: ChunkId={textChunk.ChunkId}, テキスト長={textChunk.TranslatedText?.Length ?? 0}",
            Metrics = new Dictionary<string, object>
            {
                { "ChunkId", textChunk.ChunkId },
                { "CombinedTextLength", textChunk.CombinedText?.Length ?? 0 },
                { "TranslatedTextLength", textChunk.TranslatedText?.Length ?? 0 },
                { "BoundsX", textChunk.CombinedBounds.X },
                { "BoundsY", textChunk.CombinedBounds.Y },
                { "BoundsWidth", textChunk.CombinedBounds.Width },
                { "BoundsHeight", textChunk.CombinedBounds.Height },
                { "CanShowInPlace", textChunk.CanShowInPlace() },
                { "IsInitialized", _isInitialized },
                { "IsDisposed", _disposed }
            }
        }).ConfigureAwait(false);
        
        // STOP押下後の表示を防ぐためのキャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();
        
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        // 初期化後にもう一度キャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();

        // 🔍 [DISPLAY_DEBUG] オーバーレイ表示直前のテキスト内容をログ出力
        Console.WriteLine($"🔍 [DISPLAY_DEBUG] ShowInPlaceOverlayAsync - ChunkId: {textChunk.ChunkId}");
        Console.WriteLine($"🔍 [DISPLAY_DEBUG] CombinedText: '{textChunk.CombinedText}'");
        Console.WriteLine($"🔍 [DISPLAY_DEBUG] TranslatedText: '{textChunk.TranslatedText}'");
        Console.WriteLine($"🔍 [DISPLAY_DEBUG] CanShowInPlace: {textChunk.CanShowInPlace()}");
        Console.WriteLine($"🔍 [DISPLAY_DEBUG] Bounds: X={textChunk.CombinedBounds.X}, Y={textChunk.CombinedBounds.Y}, W={textChunk.CombinedBounds.Width}, H={textChunk.CombinedBounds.Height}");
        
        // 🚫 [TRANSLATION_ONLY] 失敗・エラー結果の表示を包括的に防止
        if (!TranslationValidator.IsValid(textChunk.TranslatedText, textChunk.CombinedText))
        {
            Console.WriteLine($"🚫 [TRANSLATION_ONLY] 無効な翻訳結果のため表示をスキップ - ChunkId: {textChunk.ChunkId}, 結果: '{textChunk.TranslatedText}'");
            _logger.LogDebug("無効な翻訳結果のため表示をスキップ - ChunkId: {ChunkId}, 結果: {Result}", textChunk.ChunkId, textChunk.TranslatedText ?? "null");
            return;
        }
        
        if (!textChunk.CanShowInPlace())
        {
            _logger.LogWarning("インプレース表示条件を満たしていません: {InPlaceLog}", textChunk.ToInPlaceLogString());
            return;
        }

        try
        {
            // オーバーレイ処理直前のキャンセレーションチェック
            cancellationToken.ThrowIfCancellationRequested();
            
            // 既存のオーバーレイをチェック
            if (_activeOverlays.TryGetValue(textChunk.ChunkId, out var existingOverlay))
            {
                // 既存のオーバーレイを更新
                await existingOverlay.UpdateInPlaceContentAsync(textChunk, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("既存インプレースオーバーレイを更新 - ChunkId: {ChunkId}", textChunk.ChunkId);
            }
            else
            {
                // 新規インプレースオーバーレイを作成・表示
                await CreateAndShowNewInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
            }

            // 📊 [DIAGNOSTIC] オーバーレイ表示成功イベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "Overlay",
                IsSuccess = true,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Message = $"オーバーレイ表示成功: ChunkId={textChunk.ChunkId}, 処理時間={stopwatch.ElapsedMilliseconds}ms",
                Metrics = new Dictionary<string, object>
                {
                    { "ChunkId", textChunk.ChunkId },
                    { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds },
                    { "CombinedTextLength", textChunk.CombinedText?.Length ?? 0 },
                    { "TranslatedTextLength", textChunk.TranslatedText?.Length ?? 0 },
                    { "BoundsArea", textChunk.CombinedBounds.Width * textChunk.CombinedBounds.Height },
                    { "ActiveOverlaysCount", _activeOverlays.Count },
                    { "IsUpdate", _activeOverlays.ContainsKey(textChunk.ChunkId) },
                    { "DisplayType", "InPlace" }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 📊 [DIAGNOSTIC] オーバーレイ表示失敗イベント
            try
            {
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "Overlay",
                    IsSuccess = false,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = ex.Message,
                    SessionId = sessionId,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"オーバーレイ表示失敗: ChunkId={textChunk.ChunkId}, エラー={ex.GetType().Name}: {ex.Message}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "ChunkId", textChunk.ChunkId },
                        { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds },
                        { "ErrorType", ex.GetType().Name },
                        { "CombinedTextLength", textChunk.CombinedText?.Length ?? 0 },
                        { "TranslatedTextLength", textChunk.TranslatedText?.Length ?? 0 },
                        { "IsInitialized", _isInitialized },
                        { "IsDisposed", _disposed },
                        { "ActiveOverlaysCount", _activeOverlays.Count }
                    }
                }).ConfigureAwait(false);
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
                
                // 🎯 衝突回避位置を計算
                System.Drawing.Point collisionAwarePosition;
                try
                {
                    var overlaySize = textChunk.GetOverlaySize();
                    var screenBounds = new Rectangle(0, 0, 1920, 1080); // デフォルト画面サイズ
                        
                    collisionAwarePosition = textChunk.CalculateOptimalOverlayPositionWithCollisionAvoidance(
                        overlaySize, screenBounds, existingBounds);
                        
                    Console.WriteLine($"🎯 [COLLISION_AVOIDANCE] 衝突回避位置計算完了 - ChunkId: {textChunk.ChunkId}, " +
                                    $"Position: ({collisionAwarePosition.X},{collisionAwarePosition.Y}), " +
                                    $"ExistingOverlays: {existingBounds.Count}");
                }
                catch (Exception ex)
                {
                    // 衝突回避計算失敗時は通常の位置計算にフォールバック
                    collisionAwarePosition = textChunk.GetOverlayPosition();
                    _logger.LogWarning(ex, "衝突回避位置計算失敗、通常位置を使用 - ChunkId: {ChunkId}", textChunk.ChunkId);
                }
                
                // オーバーレイを コレクションに追加
                _activeOverlays[textChunk.ChunkId] = newOverlay;
                
                // 一時的なTextChunkで衝突回避位置を適用
                var adjustedTextChunk = CreateAdjustedTextChunk(textChunk, collisionAwarePosition);
                
                // 衝突回避位置でインプレース表示を開始
                await newOverlay.ShowInPlaceOverlayAsync(adjustedTextChunk, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("新規インプレースオーバーレイ表示完了（衝突回避対応） - ChunkId: {ChunkId}, Position: ({X},{Y})", 
                    textChunk.ChunkId, collisionAwarePosition.X, collisionAwarePosition.Y);
            }
            else
            {
                throw new InvalidOperationException("インプレースオーバーレイウィンドウの作成に失敗しました");
            }
        }
        catch (Exception ex)
        {
            // エラー時のクリーンアップ
            if (newOverlay != null)
            {
                try
                {
                    _activeOverlays.TryRemove(textChunk.ChunkId, out _);
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
    /// すべてのインプレースオーバーレイを非表示
    /// </summary>
    public async Task HideAllInPlaceOverlaysAsync()
    {
        Console.WriteLine("🚫 すべてのインプレースオーバーレイを非表示開始");
        
        var overlaysToHide = new List<KeyValuePair<int, InPlaceTranslationOverlayWindow>>();
        
        // アクティブなオーバーレイをコピー（列挙中の変更を避けるため）
        foreach (var kvp in _activeOverlays)
        {
            overlaysToHide.Add(kvp);
        }
        
        Console.WriteLine($"🔢 [STOP_DEBUG] 非表示対象オーバーレイ数: {overlaysToHide.Count}");
        
        if (overlaysToHide.Count == 0)
        {
            Console.WriteLine("⚠️ [STOP_DEBUG] アクティブオーバーレイが存在しません - Stop処理スキップ");
            return;
        }
        
        // すべてのオーバーレイを並行して非表示
        var hideTasks = overlaysToHide.Select(async kvp =>
        {
            try
            {
                Console.WriteLine($"🎯 [STOP_DEBUG] オーバーレイ非表示開始 - ChunkId: {kvp.Key}");
                
                _activeOverlays.TryRemove(kvp.Key, out _);
                await kvp.Value.HideAsync().ConfigureAwait(false);
                
                Console.WriteLine($"✅ [STOP_DEBUG] オーバーレイHide完了 - ChunkId: {kvp.Key}");
                
                kvp.Value.Dispose();
                
                Console.WriteLine($"🧹 [STOP_DEBUG] オーバーレイDispose完了 - ChunkId: {kvp.Key}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [STOP_DEBUG] オーバーレイ非表示エラー - ChunkId: {kvp.Key}, Error: {ex.Message}");
                _logger.LogError(ex, "インプレースオーバーレイ一括非表示エラー - ChunkId: {ChunkId}", kvp.Key);
            }
        });
        
        await Task.WhenAll(hideTasks).ConfigureAwait(false);
        
        Console.WriteLine($"✅ すべてのインプレースオーバーレイ非表示完了 - 処理済み: {overlaysToHide.Count}");
        Console.WriteLine($"📊 [STOP_DEBUG] 残存アクティブオーバーレイ数: {_activeOverlays.Count}");
        
        _logger.LogDebug("すべてのインプレースオーバーレイ非表示完了 - Count: {Count}", overlaysToHide.Count);
    }

    /// <summary>
    /// すべてのインプレースオーバーレイの可視性を切り替え（高速化版）
    /// オーバーレイの削除/再作成ではなく、可視性プロパティのみを変更
    /// </summary>
    public async Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"👁️ すべてのインプレースオーバーレイ可視性切り替え開始: {visible}");
        _logger.LogDebug("オーバーレイ可視性切り替え: {Visible}, 対象数: {Count}", visible, _activeOverlays.Count);
        
        if (_activeOverlays.IsEmpty)
        {
            Console.WriteLine("⚠️ アクティブなオーバーレイが存在しません - 可視性切り替えをスキップ");
            _logger.LogDebug("アクティブなオーバーレイが存在しないため可視性切り替えをスキップ");
            return;
        }

        // アクティブなオーバーレイをコピー（列挙中の変更を避けるため）
        var overlaysToToggle = new List<KeyValuePair<int, InPlaceTranslationOverlayWindow>>();
        foreach (var kvp in _activeOverlays)
        {
            overlaysToToggle.Add(kvp);
        }
        
        // すべてのオーバーレイの可視性を並行して切り替え
        var visibilityTasks = overlaysToToggle.Select(async kvp =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                
                // UIスレッドで可視性を変更
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        kvp.Value.IsVisible = visible;
                        _logger.LogTrace("オーバーレイ可視性変更: ChunkId={ChunkId}, Visible={Visible}", kvp.Key, visible);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "オーバーレイ可視性変更エラー: ChunkId={ChunkId}", kvp.Key);
                    }
                }, DispatcherPriority.Normal, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("オーバーレイ可視性変更がキャンセルされました: ChunkId={ChunkId}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "オーバーレイ可視性切り替えエラー - ChunkId: {ChunkId}", kvp.Key);
            }
        });
        
        await Task.WhenAll(visibilityTasks).ConfigureAwait(false);
        
        Console.WriteLine($"✅ すべてのインプレースオーバーレイ可視性切り替え完了: {visible} - 処理済み: {overlaysToToggle.Count}");
        _logger.LogDebug("オーバーレイ可視性切り替え完了: {Visible}, 処理数: {Count}", visible, overlaysToToggle.Count);
    }

    /// <summary>
    /// インプレースオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public async Task ResetAsync()
    {
        Console.WriteLine("🔄 InPlaceTranslationOverlayManager - リセット開始");
        
        await HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
        
        _isInitialized = false;
        
        Console.WriteLine("✅ InPlaceTranslationOverlayManager - リセット完了");
    }

    /// <summary>
    /// 現在アクティブなインプレースオーバーレイの数を取得
    /// </summary>
    public int ActiveOverlayCount => _activeOverlays.Count;
    
    /// <summary>
    /// 既存の全てのアクティブオーバーレイの境界情報を取得
    /// 衝突回避計算用
    /// </summary>
    /// <returns>既存オーバーレイの境界リスト</returns>
    private List<Rectangle> GetExistingOverlayBounds()
    {
        var bounds = new List<Rectangle>();
        
        foreach (var overlay in _activeOverlays.Values)
        {
            try
            {
                // オーバーレイの現在位置とサイズを取得
                var position = overlay.Position;
                var clientSize = overlay.ClientSize;
                bounds.Add(new Rectangle((int)position.X, (int)position.Y, (int)clientSize.Width, (int)clientSize.Height));
            }
            catch (Exception ex)
            {
                // 個別オーバーレイの情報取得失敗は無視（他のオーバーレイに影響しない）
                _logger.LogDebug(ex, "オーバーレイ境界情報取得失敗: ChunkId={ChunkId}", overlay.ChunkId);
            }
        }
        
        return bounds;
    }

    /// <summary>
    /// 衝突回避位置で調整されたTextChunkを作成
    /// 元のTextChunkのプロパティを維持しつつ、表示位置のみを衝突回避位置に調整
    /// </summary>
    /// <param name="originalChunk">元のTextChunk</param>
    /// <param name="adjustedPosition">衝突回避計算で決定された新しい位置</param>
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
    /// 指定されたChunkIdのオーバーレイを非表示にする（翻訳完了時の原文非表示用）
    /// </summary>
    public async Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_activeOverlays.TryRemove(chunkId, out var overlay))
            {
                _logger.LogDebug("オーバーレイ非表示実行 - ChunkId: {ChunkId}", chunkId);
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Hide();
                    overlay.Dispose();
                }, DispatcherPriority.Normal, cancellationToken);
                
                _logger.LogDebug("オーバーレイ非表示完了 - ChunkId: {ChunkId}", chunkId);
            }
            else
            {
                _logger.LogDebug("非表示対象オーバーレイが見つかりません - ChunkId: {ChunkId}", chunkId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "オーバーレイ非表示処理エラー - ChunkId: {ChunkId}", chunkId);
        }
    }

    /// <summary>
    /// 指定されたエリア内の既存オーバーレイを非表示にする（翻訳結果表示時の原文非表示用）
    /// </summary>
    public async Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var overlaysToHide = new List<(int chunkId, InPlaceTranslationOverlayWindow overlay)>();
            
            // 同一エリア内の既存オーバーレイを特定（除外ChunkId以外）
            foreach (var kvp in _activeOverlays)
            {
                if (kvp.Key != excludeChunkId)
                {
                    // エリアが重複している場合は非表示対象とする
                    // TODO: より精密な重複判定を実装する場合は、オーバーレイの位置情報を取得
                    overlaysToHide.Add((kvp.Key, kvp.Value));
                }
            }
            
            _logger.LogDebug("エリア内オーバーレイ非表示対象: {Count}個 - Area: {Area}", overlaysToHide.Count, area);
            
            // 非表示実行
            foreach (var (chunkId, overlay) in overlaysToHide)
            {
                if (_activeOverlays.TryRemove(chunkId, out _))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        overlay.Hide();
                        overlay.Dispose();
                    }, DispatcherPriority.Normal, cancellationToken);
                    
                    _logger.LogDebug("エリア内オーバーレイ非表示完了 - ChunkId: {ChunkId}", chunkId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エリア内オーバーレイ非表示処理エラー - Area: {Area}", area);
        }
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

        try
        {
            Console.WriteLine($"🎯 [OVERLAY] 翻訳結果オーバーレイ処理開始 - Text: '{eventData.Text}', Area: {eventData.DisplayArea}");
            _logger.LogDebug("翻訳結果OverlayUpdateEvent処理開始 - Text: {Text}, DisplayArea: {Area}", 
                eventData.Text, eventData.DisplayArea);

            // UIスレッドでオーバーレイ表示処理を実行
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!_isInitialized)
                {
                    Console.WriteLine("⚠️ [OVERLAY] オーバーレイマネージャーが初期化されていません - 初期化を実行");
                    _logger.LogWarning("オーバーレイマネージャーが初期化されていないため初期化を実行");
                    await InitializeAsync().ConfigureAwait(false);
                }

                // オーバーレイ表示のためにTextChunkを作成
                var textChunk = new TextChunk
                {
                    ChunkId = eventData.GetHashCode(), // イベントデータのハッシュをチャンクIDとして使用
                    TextResults = [], // 空のリスト（OverlayUpdateEventからは個別結果が得られない）
                    CombinedBounds = eventData.DisplayArea,
                    CombinedText = eventData.OriginalText ?? string.Empty, // 元テキスト（表示には使用しない）
                    SourceWindowHandle = IntPtr.Zero, // OverlayUpdateEventからは取得できない
                    DetectedLanguage = eventData.SourceLanguage ?? "en",
                    // 🚫 [TRANSLATION_ONLY] 翻訳結果のみ設定（OCR結果は表示しない）
                    TranslatedText = eventData.IsTranslationResult ? eventData.Text : string.Empty
                };

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
            Console.WriteLine($"❌ [OVERLAY] オーバーレイ更新処理エラー: {ex.Message}");
            _logger.LogError(ex, "OverlayUpdateEvent処理中にエラーが発生: {Error}", ex.Message);
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
            // すべてのオーバーレイを同期的に閉じる
            foreach (var kvp in _activeOverlays)
            {
                try
                {
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "インプレースオーバーレイDispose エラー - ChunkId: {ChunkId}", kvp.Key);
                }
            }
            
            _activeOverlays.Clear();
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

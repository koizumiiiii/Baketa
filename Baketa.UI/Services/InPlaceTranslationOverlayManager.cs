using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.UI.Views.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// インプレース翻訳オーバーレイの管理サービス
/// Google翻訳カメラのような、元テキストを翻訳テキストで置き換える表示を管理
/// </summary>
public class InPlaceTranslationOverlayManager(
    IEventAggregator eventAggregator,
    ILogger<InPlaceTranslationOverlayManager> logger) : IInPlaceTranslationOverlayManager, IDisposable
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
        
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (!textChunk.CanShowInPlace())
        {
            _logger.LogWarning("インプレース表示条件を満たしていません: {InPlaceLog}", textChunk.ToInPlaceLogString());
            return;
        }

        try
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "インプレース表示エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 新規インプレースオーバーレイを作成して表示
    /// </summary>
    private async Task CreateAndShowNewInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken)
    {
        InPlaceTranslationOverlayWindow? newOverlay = null;
        
        try
        {
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
                // オーバーレイをコレクションに追加
                _activeOverlays[textChunk.ChunkId] = newOverlay;
                
                // インプレース表示を開始
                await newOverlay.ShowInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("新規インプレースオーバーレイ表示完了 - ChunkId: {ChunkId}", textChunk.ChunkId);
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
    /// 指定されたチャンクのインプレースオーバーレイを非表示
    /// </summary>
    public async Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        if (_activeOverlays.TryRemove(chunkId, out var overlay))
        {
            try
            {
                await overlay.HideAsync(cancellationToken).ConfigureAwait(false);
                overlay.Dispose();
                _logger.LogDebug("インプレースオーバーレイ非表示完了 - ChunkId: {ChunkId}", chunkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "インプレースオーバーレイ非表示エラー - ChunkId: {ChunkId}", chunkId);
            }
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
        
        // すべてのオーバーレイを並行して非表示
        var hideTasks = overlaysToHide.Select(async kvp =>
        {
            try
            {
                _activeOverlays.TryRemove(kvp.Key, out _);
                await kvp.Value.HideAsync().ConfigureAwait(false);
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "インプレースオーバーレイ一括非表示エラー - ChunkId: {ChunkId}", kvp.Key);
            }
        });
        
        await Task.WhenAll(hideTasks).ConfigureAwait(false);
        
        Console.WriteLine($"✅ すべてのインプレースオーバーレイ非表示完了 - 処理済み: {overlaysToHide.Count}");
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
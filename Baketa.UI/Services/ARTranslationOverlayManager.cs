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
/// AR風翻訳オーバーレイの管理サービス
/// Google翻訳カメラのような、元テキストを翻訳テキストで置き換える表示を管理
/// </summary>
public class ARTranslationOverlayManager(
    IEventAggregator eventAggregator,
    ILogger<ARTranslationOverlayManager> logger) : IARTranslationOverlayManager, IDisposable
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<ARTranslationOverlayManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    // チャンクIDとARオーバーレイウィンドウのマッピング
    private readonly ConcurrentDictionary<int, ARTranslationOverlayWindow> _activeOverlays = new();
    
    private bool _isInitialized;
    private bool _disposed;
    private readonly object _initializeLock = new();

    /// <summary>
    /// ARオーバーレイマネージャーを初期化
    /// </summary>
    public async Task InitializeAsync()
    {
        Console.WriteLine($"🔧 ARTranslationOverlayManager.InitializeAsync開始 - _isInitialized: {_isInitialized}, _disposed: {_disposed}");
        
        lock (_initializeLock)
        {
            if (_isInitialized || _disposed)
            {
                Console.WriteLine($"⚠️ ARオーバーレイマネージャー初期化スキップ (initialized: {_isInitialized}, disposed: {_disposed})");
                _logger.LogDebug("AR overlay manager initialization skipped (initialized: {IsInitialized}, disposed: {IsDisposed})", 
                    _isInitialized, _disposed);
                return;
            }
            
            Console.WriteLine("🔒 ARオーバーレイマネージャー初期化ロック取得、実際の初期化を開始");
        }

        try
        {
            _logger.LogDebug("Starting AR overlay manager initialization");

            // 初期化完了
            lock (_initializeLock)
            {
                _isInitialized = true;
                Console.WriteLine("🔓 ARオーバーレイマネージャー初期化完了フラグ設定");
            }
            
            Console.WriteLine("🎉 ARTranslationOverlayManager.InitializeAsync正常完了");
            _logger.LogInformation("AR translation overlay manager initialized successfully");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 ARTranslationOverlayManager.InitializeAsync例外: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            _logger.LogError(ex, "Failed to initialize AR translation overlay manager");
            throw;
        }
    }

    /// <summary>
    /// TextChunkのAR風オーバーレイを表示
    /// 既存のオーバーレイがある場合は更新、ない場合は新規作成
    /// </summary>
    public async Task ShowAROverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (!textChunk.CanShowAR())
        {
            _logger.LogWarning("AR表示条件を満たしていません: {ARLog}", textChunk.ToARLogString());
            return;
        }

        try
        {
            // 既存のオーバーレイをチェック
            if (_activeOverlays.TryGetValue(textChunk.ChunkId, out var existingOverlay))
            {
                // 既存のオーバーレイを更新
                await existingOverlay.UpdateARContentAsync(textChunk, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("既存ARオーバーレイを更新 - ChunkId: {ChunkId}", textChunk.ChunkId);
            }
            else
            {
                // 新規ARオーバーレイを作成・表示
                await CreateAndShowNewAROverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AR表示エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 新規ARオーバーレイを作成して表示
    /// </summary>
    private async Task CreateAndShowNewAROverlayAsync(TextChunk textChunk, CancellationToken cancellationToken)
    {
        ARTranslationOverlayWindow? newOverlay = null;
        
        try
        {
            // UIスレッドでオーバーレイウィンドウを作成
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Console.WriteLine($"🏗️ 新規ARオーバーレイ作成開始 - ChunkId: {textChunk.ChunkId}");
                
                newOverlay = new ARTranslationOverlayWindow
                {
                    ChunkId = textChunk.ChunkId,
                    OriginalText = textChunk.CombinedText,
                    TranslatedText = textChunk.TranslatedText,
                    TargetBounds = textChunk.CombinedBounds,
                    SourceWindowHandle = textChunk.SourceWindowHandle
                };
                
                Console.WriteLine($"✅ 新規ARオーバーレイ作成完了 - ChunkId: {textChunk.ChunkId}");
                
            }, DispatcherPriority.Normal, cancellationToken);

            if (newOverlay != null)
            {
                // オーバーレイをコレクションに追加
                _activeOverlays[textChunk.ChunkId] = newOverlay;
                
                // AR表示を開始
                await newOverlay.ShowAROverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("新規ARオーバーレイ表示完了 - ChunkId: {ChunkId}", textChunk.ChunkId);
            }
            else
            {
                throw new InvalidOperationException("ARオーバーレイウィンドウの作成に失敗しました");
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
                    _logger.LogError(cleanupEx, "ARオーバーレイクリーンアップエラー - ChunkId: {ChunkId}", textChunk.ChunkId);
                }
            }
            
            _logger.LogError(ex, "新規ARオーバーレイ作成エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 指定されたチャンクのARオーバーレイを非表示
    /// </summary>
    public async Task HideAROverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        if (_activeOverlays.TryRemove(chunkId, out var overlay))
        {
            try
            {
                await overlay.HideAsync(cancellationToken).ConfigureAwait(false);
                overlay.Dispose();
                _logger.LogDebug("ARオーバーレイ非表示完了 - ChunkId: {ChunkId}", chunkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ARオーバーレイ非表示エラー - ChunkId: {ChunkId}", chunkId);
            }
        }
    }

    /// <summary>
    /// すべてのARオーバーレイを非表示
    /// </summary>
    public async Task HideAllAROverlaysAsync()
    {
        Console.WriteLine("🚫 すべてのARオーバーレイを非表示開始");
        
        var overlaysToHide = new List<KeyValuePair<int, ARTranslationOverlayWindow>>();
        
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
                _logger.LogError(ex, "ARオーバーレイ一括非表示エラー - ChunkId: {ChunkId}", kvp.Key);
            }
        });
        
        await Task.WhenAll(hideTasks).ConfigureAwait(false);
        
        Console.WriteLine($"✅ すべてのARオーバーレイ非表示完了 - 処理済み: {overlaysToHide.Count}");
        _logger.LogDebug("すべてのARオーバーレイ非表示完了 - Count: {Count}", overlaysToHide.Count);
    }

    /// <summary>
    /// ARオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public async Task ResetAsync()
    {
        Console.WriteLine("🔄 ARTranslationOverlayManager - リセット開始");
        
        await HideAllAROverlaysAsync().ConfigureAwait(false);
        
        _isInitialized = false;
        
        Console.WriteLine("✅ ARTranslationOverlayManager - リセット完了");
    }

    /// <summary>
    /// 現在アクティブなARオーバーレイの数を取得
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
                    _logger.LogError(ex, "ARオーバーレイDispose エラー - ChunkId: {ChunkId}", kvp.Key);
                }
            }
            
            _activeOverlays.Clear();
            _isInitialized = false;
            _disposed = true;
            
            _logger.LogDebug("AR translation overlay manager disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing AR translation overlay manager");
        }
        
        GC.SuppressFinalize(this);
    }
}
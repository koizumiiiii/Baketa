using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.UI.Views.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// シンプルで確実に動作するインプレース翻訳オーバーレイマネージャー
///
/// Phase 3 完全リファクタリング - UltraThink + Geminiレビュー反映版
/// 設計原則:
/// - YAGNI原則: 必要最小限の機能のみ実装
/// - Single Responsibility: オーバーレイ表示/非表示のみ担当
/// - Dispatcher.UIThread保証: UIスレッドで確実に実行
/// - Window Pooling: GC圧力軽減のためウィンドウ再利用
/// - Thread Safety: lock文でコレクション同期
/// </summary>
public sealed class SimpleInPlaceOverlayManager : IInPlaceTranslationOverlayManager, IDisposable
{
    private readonly ILogger<SimpleInPlaceOverlayManager> _logger;
    private readonly object _lock = new(); // 🔒 スレッドセーフなコレクション管理

    // アクティブなオーバーレイウィンドウ（表示中）
    private readonly List<InPlaceTranslationOverlayWindow> _activeWindows = new();

    // ウィンドウプール（再利用可能な非表示ウィンドウ）
    private readonly Queue<InPlaceTranslationOverlayWindow> _windowPool = new();

    private const int MaxPoolSize = 10; // プールサイズ上限（メモリ使用量制御）
    private bool _disposed;

    public SimpleInPlaceOverlayManager(ILogger<SimpleInPlaceOverlayManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 🔥 [EMERGENCY] コンストラクタ実行確認（Phase 3 診断）
        Console.WriteLine("🔥🔥🔥 [EMERGENCY] SimpleInPlaceOverlayManager コンストラクタ実行");
        _logger.LogInformation("✅ SimpleInPlaceOverlayManager初期化完了");
    }

    /// <summary>
    /// TextChunkの翻訳結果をインプレースオーバーレイで表示
    /// </summary>
    /// <param name="textChunk">翻訳結果を含むTextChunk</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        // 🔥🔥🔥 [ULTRA_CRITICAL] メソッド開始ログ（Phase 3 診断）
        var chunkIdStr = textChunk?.ChunkId.ToString() ?? "NULL";
        Console.WriteLine($"🔥🔥🔥 [ULTRA_CRITICAL] SimpleInPlaceOverlayManager.ShowInPlaceOverlayAsync CALLED - ChunkId: {chunkIdStr}");
        _logger.LogInformation("🔥 ShowInPlaceOverlayAsync開始 - ChunkId: {ChunkId}", textChunk?.ChunkId ?? -1);

        if (textChunk == null)
        {
            _logger.LogWarning("⚠️ TextChunkがnullのため、オーバーレイ表示をスキップ");
            return;
        }

        // UIスレッドで確実に実行
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _logger.LogDebug("🎯 UIスレッド内処理開始 - ChunkId: {ChunkId}", textChunk.ChunkId);

            InPlaceTranslationOverlayWindow window;

            // 🔒 スレッドセーフなウィンドウ取得
            lock (_lock)
            {
                // プールから再利用可能なウィンドウを取得（GC圧力軽減）
                if (_windowPool.Count > 0)
                {
                    window = _windowPool.Dequeue();
                    _logger.LogDebug("♻️ ウィンドウプールから再利用 - Pool残: {PoolCount}", _windowPool.Count);
                }
                else
                {
                    window = new InPlaceTranslationOverlayWindow();
                    _logger.LogDebug("🆕 新規ウィンドウ作成");
                }

                _activeWindows.Add(window);
            }

            try
            {
                // InPlaceTranslationOverlayWindow.ShowInPlaceOverlayAsync()を直接呼び出し
                // TextChunkの拡張メソッド（GetBasicOverlayPosition, GetOverlaySize, CalculateOptimalFontSize）
                // が座標変換を担当するため、OverlayCoordinateTransformerは不要
                await window.ShowInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("✅ オーバーレイ表示完了 - ChunkId: {ChunkId}", textChunk.ChunkId);
                Console.WriteLine($"✅ [SUCCESS] オーバーレイ表示成功 - ChunkId: {textChunk.ChunkId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ オーバーレイ表示中にエラー - ChunkId: {ChunkId}, Error: {Message}",
                    textChunk.ChunkId, ex.Message);

                // エラー時はアクティブリストから削除してプールに戻す
                lock (_lock)
                {
                    _activeWindows.Remove(window);
                    ReturnWindowToPool(window);
                }

                throw;
            }
        }, DispatcherPriority.Normal, cancellationToken);
    }

    /// <summary>
    /// 指定されたチャンクのインプレースオーバーレイを非表示
    /// 簡易実装: 全オーバーレイをクリアして再構築（YAGNI原則）
    /// </summary>
    public async Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🗑️ 指定チャンクのオーバーレイ非表示 - ChunkId: {ChunkId}", chunkId);
        // 簡易実装: 個別管理はせず、全クリアで対応
        await HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 指定領域内のオーバーレイを非表示にする
    /// 簡易実装: 全オーバーレイをクリア（YAGNI原則）
    /// </summary>
    public async Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🗑️ 領域内オーバーレイ非表示 - Area: {Area}, Exclude: {ExcludeChunkId}", area, excludeChunkId);
        // 簡易実装: 領域判定はせず、全クリアで対応
        await HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// すべてのインプレースオーバーレイを非表示
    /// </summary>
    public async Task HideAllInPlaceOverlaysAsync()
    {
        _logger.LogInformation("🗑️ 全オーバーレイ非表示開始 - Count: {Count}", _activeWindows.Count);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            List<InPlaceTranslationOverlayWindow> windowsToHide;

            lock (_lock)
            {
                // アクティブウィンドウのコピーを取得（反復中の変更を回避）
                windowsToHide = new List<InPlaceTranslationOverlayWindow>(_activeWindows);
                _activeWindows.Clear();
            }

            foreach (var window in windowsToHide)
            {
                try
                {
                    // Hide()を使用してウィンドウを非表示（Close()ではなく再利用可能）
                    window.Hide();
                    ReturnWindowToPool(window);
                    _logger.LogDebug("✅ ウィンドウ非表示成功");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ オーバーレイウィンドウの非表示中にエラー: {Message}", ex.Message);

                    // エラー時はウィンドウを破棄
                    try
                    {
                        window.Close();
                        window.Dispose();
                    }
                    catch
                    {
                        // Dispose時のエラーは無視
                    }
                }
            }
        }, DispatcherPriority.Normal);

        _logger.LogInformation("✅ 全オーバーレイ非表示完了");
    }

    /// <summary>
    /// すべてのインプレースオーバーレイの可視性を切り替え
    /// </summary>
    public async Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("👁️ 全オーバーレイ可視性切り替え - Visible: {Visible}", visible);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (_lock)
            {
                foreach (var window in _activeWindows)
                {
                    try
                    {
                        if (visible)
                        {
                            window.Show();
                        }
                        else
                        {
                            window.Hide();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ ウィンドウ可視性切り替え中にエラー: {Message}", ex.Message);
                    }
                }
            }
        }, DispatcherPriority.Normal, cancellationToken);

        _logger.LogInformation("✅ 全オーバーレイ可視性切り替え完了");
    }

    /// <summary>
    /// インプレースオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public async Task ResetAsync()
    {
        _logger.LogInformation("🔄 オーバーレイマネージャーリセット");
        await HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 現在アクティブなインプレースオーバーレイの数を取得
    /// </summary>
    public int ActiveOverlayCount
    {
        get
        {
            lock (_lock)
            {
                return _activeWindows.Count;
            }
        }
    }

    /// <summary>
    /// インプレースオーバーレイマネージャーを初期化
    /// </summary>
    public Task InitializeAsync()
    {
        _logger.LogInformation("🚀 オーバーレイマネージャー初期化");
        // シンプル実装では特別な初期化処理は不要
        return Task.CompletedTask;
    }

    /// <summary>
    /// ウィンドウをプールに返却（再利用可能にする）
    /// </summary>
    /// <param name="window">返却するウィンドウ</param>
    private void ReturnWindowToPool(InPlaceTranslationOverlayWindow window)
    {
        lock (_lock)
        {
            // プールサイズ上限チェック（メモリリーク防止）
            if (_windowPool.Count < MaxPoolSize)
            {
                _windowPool.Enqueue(window);
                _logger.LogDebug("♻️ ウィンドウをプールに返却 - Pool数: {PoolCount}", _windowPool.Count);
            }
            else
            {
                // プール満杯の場合は破棄
                try
                {
                    window.Close();
                    window.Dispose();
                    _logger.LogDebug("🗑️ プール満杯のためウィンドウ破棄");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ ウィンドウ破棄中にエラー: {Message}", ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("🗑️ SimpleInPlaceOverlayManager Dispose開始");

        // 全オーバーレイを非表示（同期的に実行）
        HideAllInPlaceOverlaysAsync().GetAwaiter().GetResult();

        // プール内のウィンドウも全て破棄
        lock (_lock)
        {
            while (_windowPool.Count > 0)
            {
                var window = _windowPool.Dequeue();
                try
                {
                    window.Close();
                    window.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ プール内ウィンドウの破棄中にエラー: {Message}", ex.Message);
                }
            }
        }

        _disposed = true;
        _logger.LogInformation("✅ SimpleInPlaceOverlayManager Disposed");
    }
}

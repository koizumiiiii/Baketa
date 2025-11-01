using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Infrastructure.Platform.Windows.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Win32 Layered Window ベースのインプレース翻訳オーバーレイマネージャー
///
/// 🎯 [WIN32_OVERLAY_MIGRATION] Phase 1: Avalonia → Win32 移行完了版
/// 設計原則:
/// - OS-Native透過ウィンドウで角丸・シャドウ問題を根本解決
/// - ILayeredOverlayWindowFactory による依存性注入
/// - ConcurrentBag によるスレッドセーフなコレクション管理（Gemini推奨）
/// - リソースの適切な破棄（IDisposable パターン）
/// </summary>
public sealed class SimpleInPlaceOverlayManager : IInPlaceTranslationOverlayManager, IDisposable
{
    private readonly ILayeredOverlayWindowFactory _windowFactory;
    private readonly ILogger<SimpleInPlaceOverlayManager> _logger;

    // アクティブなオーバーレイウィンドウ（スレッドセーフ）
    private readonly ConcurrentBag<ILayeredOverlayWindow> _activeWindows = new();

    private bool _disposed;

    public SimpleInPlaceOverlayManager(
        ILayeredOverlayWindowFactory windowFactory,
        ILogger<SimpleInPlaceOverlayManager> logger)
    {
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("✅ [WIN32_OVERLAY] SimpleInPlaceOverlayManager初期化完了");
    }

    /// <summary>
    /// TextChunkの翻訳結果をインプレースオーバーレイで表示
    /// </summary>
    /// <param name="textChunk">翻訳結果を含むTextChunk</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SimpleInPlaceOverlayManager));
        }

        if (textChunk == null)
        {
            _logger.LogWarning("⚠️ [WIN32_OVERLAY] TextChunkがnullのため、オーバーレイ表示をスキップ");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(textChunk.TranslatedText))
        {
            _logger.LogWarning("⚠️ [WIN32_OVERLAY] TranslatedTextが空のため、オーバーレイ表示をスキップ - ChunkId: {ChunkId}",
                textChunk.ChunkId);
            return Task.CompletedTask;
        }

        _logger.LogDebug("🎯 [WIN32_OVERLAY] オーバーレイ表示開始 - ChunkId: {ChunkId}, Text: {Text}",
            textChunk.ChunkId, textChunk.TranslatedText);

        try
        {
            // 🎯 [WIN32_COORDINATE_FIX] マルチモニター・異なる解像度対応
            // - TextChunk.CombinedBoundsはCoordinateTransformationService経由で
            //   既にスクリーン絶対座標に変換済み（GetWindowRect使用）
            // - Win32 Layered Windowはスクリーン絶対座標で配置されるため、
            //   CombinedBoundsをそのまま使用することで正しい位置に表示される
            //
            // 🌍 マルチモニター環境の座標系:
            // - プライマリモニター: (0, 0) を基準
            // - セカンダリモニター（右側）: X座標が大きい値（例: 1920~3840）
            // - セカンダリモニター（上側）: Y座標が負の値（例: -1080~0）
            // - セカンダリモニター（下側）: Y座標が大きい値（例: 1080~2160）
            //
            // 🔧 解像度対応:
            // - FHD (1920x1080), QHD (2560x1440), 4K (3840x2160) すべて対応
            // - CoordinateTransformationServiceがDPI/スケール補正を実施済み

            // Win32 Layered Windowを作成
            var window = _windowFactory.Create();

            // 翻訳テキストを設定
            window.SetText(textChunk.TranslatedText);

            // 🔧 [MULTI_MONITOR_DEBUG] 座標値を詳細ログ出力
            _logger.LogInformation("🔍 [WIN32_COORDINATE] CombinedBounds座標: X={X}, Y={Y}, W={W}, H={H} - ChunkId: {ChunkId}",
                textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y,
                textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height,
                textChunk.ChunkId);

            // 🔧 [MARGIN_FIX] 固定マージン + 10%のハイブリッドマージン追加
            var baseX = textChunk.CombinedBounds.X;
            var baseY = textChunk.CombinedBounds.Y;
            var baseWidth = textChunk.CombinedBounds.Width;
            var baseHeight = textChunk.CombinedBounds.Height;

            // マージン計算: 最低10px + 10%（大きいテキストにも対応）
            var marginWidth = Math.Max(10, (int)(baseWidth * 0.1));
            var marginHeight = Math.Max(5, (int)(baseHeight * 0.1));

            var finalWidth = baseWidth + marginWidth;
            var finalHeight = baseHeight + marginHeight;

            // 🔧 [SCREEN_BOUNDS_CHECK] 画面境界を超えないように調整
            var screenBounds = GetScreenBounds(baseX, baseY);

            // 右端チェック
            if (baseX + finalWidth > screenBounds.Right)
            {
                var overflow = (baseX + finalWidth) - screenBounds.Right;
                finalWidth = Math.Max(baseWidth, finalWidth - overflow); // 少なくとも元のサイズは確保
            }

            // 下端チェック
            if (baseY + finalHeight > screenBounds.Bottom)
            {
                var overflow = (baseY + finalHeight) - screenBounds.Bottom;
                finalHeight = Math.Max(baseHeight, finalHeight - overflow); // 少なくとも元のサイズは確保
            }

            // 左端チェック（念のため）
            if (baseX < screenBounds.Left)
            {
                baseX = screenBounds.Left;
            }

            // 上端チェック（念のため）
            if (baseY < screenBounds.Top)
            {
                baseY = screenBounds.Top;
            }

            _logger.LogDebug("📏 [MARGIN_CALC] 元: ({BaseW}x{BaseH}) → マージン追加後: ({FinalW}x{FinalH}), 画面: {ScreenBounds}",
                baseWidth, baseHeight, finalWidth, finalHeight, screenBounds);

            // 座標を設定（調整後）
            window.SetPosition(baseX, baseY);

            // サイズを設定（マージン追加 + 画面境界調整後）
            if (finalWidth > 0 && finalHeight > 0)
            {
                window.SetSize(finalWidth, finalHeight);
            }
            else
            {
                // サイズ未指定の場合はデフォルトサイズ（テキストに応じて自動調整）
                // LayeredOverlayWindowがテキストサイズから自動計算
                _logger.LogDebug("📐 [WIN32_OVERLAY] サイズ未指定 - テキストサイズから自動計算");
            }

            // 背景色を設定（すりガラス風半透明白）
            // ARGB: Alpha=240, RGB=(255, 255, 242) - 淡い黄色がかった白
            window.SetBackgroundColor(240, 255, 255, 242);

            // ウィンドウを表示
            window.Show();

            // アクティブウィンドウコレクションに追加
            _activeWindows.Add(window);

            _logger.LogInformation("✅ [WIN32_OVERLAY] オーバーレイ表示完了 - ChunkId: {ChunkId}, Pos: ({X}, {Y}), Size: ({W}x{H})",
                textChunk.ChunkId, textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y, textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [WIN32_OVERLAY] オーバーレイ表示中にエラー - ChunkId: {ChunkId}, Error: {Message}",
                textChunk.ChunkId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 指定されたチャンクのインプレースオーバーレイを非表示
    /// 簡易実装: 全オーバーレイをクリア（YAGNI原則）
    /// </summary>
    public Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🗑️ [WIN32_OVERLAY] 指定チャンクのオーバーレイ非表示 - ChunkId: {ChunkId}", chunkId);
        // 簡易実装: 個別管理はせず、全クリアで対応
        return HideAllInPlaceOverlaysAsync();
    }

    /// <summary>
    /// 指定領域内のオーバーレイを非表示にする
    /// 簡易実装: 全オーバーレイをクリア（YAGNI原則）
    /// </summary>
    public Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🗑️ [WIN32_OVERLAY] 領域内オーバーレイ非表示 - Area: {Area}, Exclude: {ExcludeChunkId}",
            area, excludeChunkId);
        // 簡易実装: 領域判定はせず、全クリアで対応
        return HideAllInPlaceOverlaysAsync();
    }

    /// <summary>
    /// すべてのインプレースオーバーレイを非表示
    /// </summary>
    public Task HideAllInPlaceOverlaysAsync()
    {
        _logger.LogInformation("🗑️ [WIN32_OVERLAY] 全オーバーレイ非表示開始 - Count: {Count}", _activeWindows.Count);

        // ConcurrentBagからすべてのウィンドウを取り出して処理
        var windows = _activeWindows.ToArray();
        _activeWindows.Clear();

        foreach (var window in windows)
        {
            try
            {
                window.Close();
                window.Dispose();
                _logger.LogDebug("✅ [WIN32_OVERLAY] ウィンドウ閉じて破棄成功");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [WIN32_OVERLAY] オーバーレイウィンドウの破棄中にエラー: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("✅ [WIN32_OVERLAY] 全オーバーレイ非表示完了");
        return Task.CompletedTask;
    }

    /// <summary>
    /// すべてのインプレースオーバーレイの可視性を切り替え
    /// </summary>
    public Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("👁️ [WIN32_OVERLAY] 全オーバーレイ可視性切り替え - Visible: {Visible}", visible);

        var windows = _activeWindows.ToArray();

        foreach (var window in windows)
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
                _logger.LogWarning(ex, "⚠️ [WIN32_OVERLAY] ウィンドウ可視性切り替え中にエラー: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("✅ [WIN32_OVERLAY] 全オーバーレイ可視性切り替え完了");
        return Task.CompletedTask;
    }

    /// <summary>
    /// インプレースオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public Task ResetAsync()
    {
        _logger.LogInformation("🔄 [WIN32_OVERLAY] オーバーレイマネージャーリセット");
        return HideAllInPlaceOverlaysAsync();
    }

    /// <summary>
    /// 現在アクティブなインプレースオーバーレイの数を取得
    /// </summary>
    public int ActiveOverlayCount => _activeWindows.Count;

    /// <summary>
    /// インプレースオーバーレイマネージャーを初期化
    /// </summary>
    public Task InitializeAsync()
    {
        _logger.LogInformation("🚀 [WIN32_OVERLAY] オーバーレイマネージャー初期化");
        // Win32 Layered Window実装では特別な初期化処理は不要
        return Task.CompletedTask;
    }

    /// <summary>
    /// 🔧 [SCREEN_BOUNDS_HELPER] 指定座標が属するモニターの作業領域を取得
    /// マルチモニター対応
    /// </summary>
    /// <param name="x">X座標（スクリーン絶対座標）</param>
    /// <param name="y">Y座標（スクリーン絶対座標）</param>
    /// <returns>モニターの作業領域（タスクバー除外）</returns>
    private Rectangle GetScreenBounds(int x, int y)
    {
        try
        {
            // System.Windows.Forms.Screen APIを使用してマルチモニター対応
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
            var workingArea = screen.WorkingArea; // タスクバーを除外した作業領域

            _logger.LogTrace("🖥️ [SCREEN_BOUNDS] 座標({X}, {Y}) → モニター: {MonitorName}, 作業領域: {Bounds}",
                x, y, screen.DeviceName, workingArea);

            return workingArea;
        }
        catch (Exception ex)
        {
            // フォールバック: プライマリモニター
            _logger.LogWarning(ex, "⚠️ [SCREEN_BOUNDS] モニター検出失敗 - プライマリモニターを使用");
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            return primaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
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

        _logger.LogInformation("🗑️ [WIN32_OVERLAY] SimpleInPlaceOverlayManager Dispose開始");

        // 全オーバーレイを非表示
        HideAllInPlaceOverlaysAsync().GetAwaiter().GetResult();

        _disposed = true;
        _logger.LogInformation("✅ [WIN32_OVERLAY] SimpleInPlaceOverlayManager Disposed");
    }
}

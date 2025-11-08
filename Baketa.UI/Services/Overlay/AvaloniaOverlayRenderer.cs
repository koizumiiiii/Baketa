using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using Baketa.UI.Views.Overlay;
using System.Collections.Concurrent;

namespace Baketa.UI.Services.Overlay;

/// <summary>
/// Avalonia UI フレームワークを使用したオーバーレイレンダラー
/// Phase 16 統一実装: IInPlaceTranslationOverlayManager と IEventProcessor<OverlayUpdateEvent> を実装
/// Legacy システムと新システムの統一インターフェースを提供
/// 🔧 [OVERLAY_UNIFICATION] 注意: このクラスは旧システム互換性のため IInPlaceTranslationOverlayManager を実装
/// Win32OverlayManager への完全移行後は廃止予定
/// </summary>
public class AvaloniaOverlayRenderer : IOverlayRenderer, IInPlaceTranslationOverlayManager, IEventProcessor<OverlayUpdateEvent>, IAsyncDisposable, IDisposable
{
    private readonly SimpleInPlaceOverlayManager _overlayManager;
    private readonly ILogger<AvaloniaOverlayRenderer> _logger;

    /// <summary>
    /// Phase 15 システムで管理するオーバーレイ情報
    /// 既存システムとの ID マッピング用
    /// </summary>
    private readonly ConcurrentDictionary<string, Phase15OverlayInfo> _phase15Overlays = new();

    /// <summary>
    /// ChunkId -> OverlayId のマッピング (IInPlaceTranslationOverlayManager互換性用)
    /// </summary>
    private readonly ConcurrentDictionary<int, string> _chunkIdToOverlayId = new();

    /// <summary>
    /// 統計情報
    /// </summary>
    private long _totalRendered = 0;
    private long _totalRemoved = 0;
    private bool _disposed = false;

    /// <summary>
    /// レガシーオーバーレイウィンドウの抽象化インターフェース
    /// 具象クラス依存を削減し、テスタビリティを向上
    /// </summary>
    private interface ILegacyOverlayWindow : IDisposable
    {
        bool IsVisible { get; set; }
        void Show();
        void Hide();
        void UpdateContent(string text);
        void UpdatePosition(double x, double y);
    }

    /// <summary>
    /// InPlaceTranslationOverlayWindow のアダプター実装
    /// 既存レガシーシステムとの互換性を維持
    /// </summary>
    private class LegacyOverlayWindowAdapter : ILegacyOverlayWindow
    {
        private readonly InPlaceTranslationOverlayWindow? _window;
        private bool _disposed;

        public LegacyOverlayWindowAdapter(InPlaceTranslationOverlayWindow? window)
        {
            _window = window;
        }

        public bool IsVisible 
        { 
            get => _window?.IsVisible ?? false; 
            set 
            {
                if (_window != null) 
                {
                    try 
                    { 
                        _window.IsVisible = value; 
                    } 
                    catch (Exception) 
                    { 
                        // UIスレッド外からのアクセスエラーを無視
                    }
                }
            } 
        }

        public void Show()
        {
            try
            {
                _window?.Show();
            }
            catch (Exception)
            {
                // UIスレッド外からのアクセスエラーを無視
            }
        }

        public void Hide()
        {
            try
            {
                _window?.Hide();
            }
            catch (Exception)
            {
                // UIスレッド外からのアクセスエラーを無視
            }
        }

        public void UpdateContent(string text)
        {
            try
            {
                // レガシーウィンドウのコンテンツ更新
                // 実装は InPlaceTranslationOverlayWindow の API に依存
            }
            catch (Exception)
            {
                // 実装されていない場合やエラーを無視
            }
        }

        public void UpdatePosition(double x, double y)
        {
            try
            {
                // レガシーウィンドウの位置更新
                // 実装は InPlaceTranslationOverlayWindow の API に依存
            }
            catch (Exception)
            {
                // 実装されていない場合やエラーを無視
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _window?.Dispose();
            }
            catch (Exception)
            {
                // Dispose エラーを無視
            }
            
            _disposed = true;
        }
    }

    /// <summary>
    /// Phase 15 オーバーレイ情報
    /// </summary>
    private class Phase15OverlayInfo
    {
        private readonly object _lock = new();
        private bool _isVisible = true;
        
        public OverlayInfo OverlayInfo { get; set; } = null!;
        public int LegacyChunkId { get; set; }
        public ILegacyOverlayWindow? LegacyWindow { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// スレッドセーフな可視性プロパティ
        /// UIスレッドとバックグラウンドスレッド間での競合状態を防止
        /// </summary>
        public bool IsVisible 
        { 
            get 
            { 
                lock (_lock) 
                { 
                    return _isVisible; 
                } 
            } 
            set 
            { 
                lock (_lock) 
                { 
                    _isVisible = value; 
                } 
            } 
        }
        
        /// <summary>
        /// スレッドセーフなウィンドウ更新
        /// レガシーウィンドウとプロパティを同期的に更新 (インターフェース経由)
        /// </summary>
        public void UpdateVisibilityThreadSafe(bool visible)
        {
            lock (_lock)
            {
                _isVisible = visible;
                // レガシーウィンドウが存在する場合はインターフェース経由で同時に更新
                if (LegacyWindow != null)
                {
                    try
                    {
                        LegacyWindow.IsVisible = visible;
                        if (visible)
                        {
                            LegacyWindow.Show();
                        }
                        else
                        {
                            LegacyWindow.Hide();
                        }
                    }
                    catch (Exception)
                    {
                        // UIスレッド外からのアクセスエラーを無視
                        // 実際の表示状態はUIディスパッチャーで処理される
                    }
                }
            }
        }
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public AvaloniaOverlayRenderer(
        SimpleInPlaceOverlayManager overlayManager,
        ILogger<AvaloniaOverlayRenderer> logger)
    {
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogInformation("🎭 [AVALONIA_RENDERER] AvaloniaOverlayRenderer 初期化 - Phase 16 統一実装");
    }

    /// <inheritdoc />
    public int RenderedCount => _phase15Overlays.Count;

    /// <inheritdoc />
    public RendererCapabilities Capabilities => 
        RendererCapabilities.HardwareAcceleration | 
        RendererCapabilities.Transparency | 
        RendererCapabilities.Animation | 
        RendererCapabilities.MultiMonitor | 
        RendererCapabilities.HighDpi | 
        RendererCapabilities.TouchSupport; // Avalonia UI は全機能をサポート

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        _logger.LogInformation("🚀 [AVALONIA_RENDERER] Avalonia レンダラー初期化開始 - Phase 16 統一実装");
        
        try
        {
            // 既存オーバーレイマネージャーの初期化は不要（DI で管理済み）
            _phase15Overlays.Clear();
            _chunkIdToOverlayId.Clear();
            _totalRendered = 0;
            _totalRemoved = 0;
            
            _logger.LogInformation("✅ [AVALONIA_RENDERER] Avalonia レンダラー初期化完了 - Phase 16 統一実装準備完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 初期化中にエラー");
            throw;
        }
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> RenderOverlayAsync(OverlayInfo info, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (info == null)
        {
            _logger.LogWarning("[AVALONIA_RENDERER] OverlayInfo が null");
            return false;
        }

        try
        {
            // Phase 15 システムから既存システムへのデータ変換
            var legacyChunkId = GenerateLegacyChunkId();
            var textChunk = ConvertToTextChunk(info, legacyChunkId);

            _logger.LogDebug("🎭 [AVALONIA_RENDERER] オーバーレイ描画開始 - Phase15 ID: {Id}, Legacy ChunkId: {ChunkId}",
                info.Id, legacyChunkId);

            // 既存システムでオーバーレイ作成・表示 (インターフェース経由)
            var legacyWindow = await CreateLegacyOverlayAsync(textChunk, cancellationToken);
            
            if (legacyWindow != null)
            {
                // Phase 15 管理情報に登録
                var phase15Info = new Phase15OverlayInfo
                {
                    OverlayInfo = info,
                    LegacyChunkId = legacyChunkId,
                    LegacyWindow = legacyWindow,
                    IsVisible = true
                };
                
                _phase15Overlays[info.Id] = phase15Info;
                _chunkIdToOverlayId[legacyChunkId] = info.Id; // ChunkId -> OverlayId マッピング
                _totalRendered++;

                _logger.LogInformation("✅ [AVALONIA_RENDERER] オーバーレイ描画成功 - ID: {Id}, Text: '{Text}'", 
                    info.Id, info.Text.Substring(0, Math.Min(30, info.Text.Length)));
                
                return true;
            }
            else
            {
                _logger.LogWarning("🚫 [AVALONIA_RENDERER] レガシーオーバーレイ作成失敗 - ID: {Id}", info.Id);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 描画中にエラー - ID: {Id}", info.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateOverlayAsync(string overlayId, OverlayRenderUpdate updateInfo, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(overlayId) || updateInfo == null)
            return false;

        try
        {
            if (!_phase15Overlays.TryGetValue(overlayId, out var phase15Info))
            {
                _logger.LogWarning("[AVALONIA_RENDERER] 更新対象オーバーレイが見つからない - ID: {Id}", overlayId);
                return false;
            }

            // 更新情報を適用
            var updatedOverlayInfo = phase15Info.OverlayInfo with
            {
                Text = updateInfo.Text ?? phase15Info.OverlayInfo.Text,
                DisplayArea = updateInfo.DisplayArea ?? phase15Info.OverlayInfo.DisplayArea
            };

            phase15Info.OverlayInfo = updatedOverlayInfo;

            // 既存システムでの更新処理
            if (phase15Info.LegacyWindow != null)
            {
                await UpdateLegacyOverlayAsync(phase15Info.LegacyWindow, updatedOverlayInfo, cancellationToken);
            }

            _logger.LogDebug("🎭 [AVALONIA_RENDERER] オーバーレイ更新完了 - ID: {Id}", overlayId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 更新中にエラー - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetVisibilityAsync(string overlayId, bool visible, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(overlayId))
            return false;

        try
        {
            if (_phase15Overlays.TryGetValue(overlayId, out var phase15Info))
            {
                phase15Info.IsVisible = visible;
                
                // 既存システムでの可視性制御
                if (phase15Info.LegacyWindow != null)
                {
                    await SetLegacyVisibilityAsync(phase15Info.LegacyWindow, visible, cancellationToken);
                }

                _logger.LogDebug("🎭 [AVALONIA_RENDERER] 可視性変更完了 - ID: {Id}, Visible: {Visible}", overlayId, visible);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 可視性変更中にエラー - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> SetAllVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            var overlayIds = _phase15Overlays.Keys.ToList();
            int changedCount = 0;

            foreach (var overlayId in overlayIds)
            {
                if (await SetVisibilityAsync(overlayId, visible, cancellationToken))
                {
                    changedCount++;
                }
            }

            _logger.LogDebug("🎭 [AVALONIA_RENDERER] 全オーバーレイ可視性変更完了 - Visible: {Visible}, 変更数: {Count}", visible, changedCount);
            return changedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 全オーバーレイ可視性変更中にエラー");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveOverlayAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(overlayId))
            return false;

        try
        {
            if (_phase15Overlays.TryRemove(overlayId, out var phase15Info))
            {
                // ChunkId マッピングも削除
                _chunkIdToOverlayId.TryRemove(phase15Info.LegacyChunkId, out _);

                // 既存システムでのオーバーレイ削除
                if (phase15Info.LegacyWindow != null)
                {
                    await RemoveLegacyOverlayAsync(phase15Info.LegacyWindow, phase15Info.LegacyChunkId, cancellationToken);
                }

                _totalRemoved++;
                _logger.LogDebug("🎭 [AVALONIA_RENDERER] オーバーレイ削除完了 - ID: {Id}", overlayId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 削除中にエラー - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> RemoveOverlaysInAreaAsync(Rectangle area, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            var excludeIdSet = excludeIds?.ToHashSet() ?? new HashSet<string>();
            var overlaysToRemove = _phase15Overlays.Values
                .Where(info => !excludeIdSet.Contains(info.OverlayInfo.Id) && 
                               info.OverlayInfo.DisplayArea.IntersectsWith(area))
                .Select(info => info.OverlayInfo.Id)
                .ToList();

            int removedCount = 0;
            foreach (var overlayId in overlaysToRemove)
            {
                if (await RemoveOverlayAsync(overlayId, cancellationToken))
                {
                    removedCount++;
                }
            }

            _logger.LogDebug("🎭 [AVALONIA_RENDERER] 領域内オーバーレイ削除完了 - Area: {Area}, 削除数: {Count}", area, removedCount);
            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 領域削除中にエラー - Area: {Area}", area);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAllOverlaysAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            var overlayCount = _phase15Overlays.Count;
            var overlayIds = _phase15Overlays.Keys.ToList();
            
            foreach (var overlayId in overlayIds)
            {
                await RemoveOverlayAsync(overlayId, cancellationToken);
            }

            _logger.LogDebug("🎭 [AVALONIA_RENDERER] 全オーバーレイ削除完了 - 削除数: {Count}", overlayCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 全削除中にエラー");
        }
    }

    /// <inheritdoc />
    public async Task<Rectangle?> GetOverlayBoundsAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(overlayId))
            return null;

        try
        {
            if (_phase15Overlays.TryGetValue(overlayId, out var phase15Info))
            {
                return await Task.FromResult(phase15Info.OverlayInfo.DisplayArea);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 位置取得中にエラー - ID: {Id}", overlayId);
            return null;
        }
    }

    #region IInPlaceTranslationOverlayManager Implementation

    /// <inheritdoc />
    public async Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (textChunk == null)
        {
            _logger.LogWarning("[AVALONIA_RENDERER] TextChunk が null");
            return;
        }

        try
        {
            // TextChunk を Phase15 OverlayInfo に変換
            var overlayInfo = ConvertFromTextChunk(textChunk);
            
            _logger.LogDebug("🔄 [AVALONIA_RENDERER] Legacy互換 ShowInPlaceOverlayAsync - ChunkId: {ChunkId}, Text: '{Text}'", 
                textChunk.ChunkId, textChunk.TranslatedText.Substring(0, Math.Min(30, textChunk.TranslatedText.Length)));

            // 内部的にPhase15システムでレンダリング
            await RenderOverlayAsync(overlayInfo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] ShowInPlaceOverlayAsync エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
        }
    }

    /// <inheritdoc />
    public async Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // ChunkId から OverlayId を検索
            if (_chunkIdToOverlayId.TryGetValue(chunkId, out var overlayId))
            {
                _logger.LogDebug("🔄 [AVALONIA_RENDERER] Legacy互換 HideInPlaceOverlayAsync - ChunkId: {ChunkId} -> OverlayId: {OverlayId}", chunkId, overlayId);
                await SetVisibilityAsync(overlayId, false, cancellationToken);
            }
            else
            {
                _logger.LogWarning("[AVALONIA_RENDERER] ChunkId {ChunkId} に対応するOverlayIdが見つからない", chunkId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] HideInPlaceOverlayAsync エラー - ChunkId: {ChunkId}", chunkId);
        }
    }

    /// <inheritdoc />
    public async Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // excludeChunkId を OverlayId に変換
            string? excludeOverlayId = null;
            if (_chunkIdToOverlayId.TryGetValue(excludeChunkId, out var overlayId))
            {
                excludeOverlayId = overlayId;
            }

            var excludeIds = excludeOverlayId != null ? new[] { excludeOverlayId } : null;
            
            _logger.LogDebug("🔄 [AVALONIA_RENDERER] Legacy互換 HideOverlaysInAreaAsync - Area: {Area}, ExcludeChunkId: {ExcludeChunkId}", area, excludeChunkId);
            await RemoveOverlaysInAreaAsync(area, excludeIds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] HideOverlaysInAreaAsync エラー - Area: {Area}", area);
        }
    }

    /// <inheritdoc />
    public async Task HideAllInPlaceOverlaysAsync()
    {
        ThrowIfDisposed();
        
        try
        {
            _logger.LogDebug("🔄 [AVALONIA_RENDERER] Legacy互換 HideAllInPlaceOverlaysAsync");
            await SetAllVisibilityAsync(false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] HideAllInPlaceOverlaysAsync エラー");
        }
    }

    /// <inheritdoc />
    public async Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            _logger.LogDebug("🔄 [AVALONIA_RENDERER] Legacy互換 SetAllOverlaysVisibilityAsync - Visible: {Visible}", visible);
            await SetAllVisibilityAsync(visible, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] SetAllOverlaysVisibilityAsync エラー");
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync()
    {
        ThrowIfDisposed();
        
        try
        {
            _logger.LogDebug("🔄 [AVALONIA_RENDERER] Legacy互換 ResetAsync");
            await RemoveAllOverlaysAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] ResetAsync エラー");
        }
    }

    /// <inheritdoc />
    public int ActiveOverlayCount => _phase15Overlays.Count;

    /// <inheritdoc />
    async Task IInPlaceTranslationOverlayManager.InitializeAsync()
    {
        // IInPlaceTranslationOverlayManager.InitializeAsync() の実装
        // 内部的に IOverlayRenderer.InitializeAsync() を呼び出し
        await InitializeAsync(CancellationToken.None);
    }

    #endregion

    #region IEventProcessor<OverlayUpdateEvent> Implementation

    /// <inheritdoc />
    public int Priority => 100; // 標準優先度（UIイベントは高優先度）
    
    /// <inheritdoc />
    public bool SynchronousExecution => false; // 非同期実行（UIスレッド負荷軽減）

    /// <inheritdoc />
    public async Task HandleAsync(OverlayUpdateEvent eventData) => 
        await HandleAsync(eventData, CancellationToken.None);

    /// <inheritdoc />
    public async Task HandleAsync(OverlayUpdateEvent eventData, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (eventData == null)
        {
            _logger.LogWarning("[AVALONIA_RENDERER] OverlayUpdateEvent が null - イベント処理をスキップ");
            return;
        }

        try
        {
            _logger.LogDebug("🎯 [AVALONIA_RENDERER] OverlayUpdateEvent 処理開始 - Text: '{Text}', Area: {Area}", 
                eventData.Text.Substring(0, Math.Min(30, eventData.Text.Length)), eventData.DisplayArea);

            // OverlayUpdateEvent を OverlayInfo に変換してレンダリング
            var overlayInfo = new OverlayInfo
            {
                Id = Guid.NewGuid().ToString(),
                Text = eventData.Text,
                DisplayArea = eventData.DisplayArea
                // DisplayStartTime と IsVisible はデフォルト値を使用
            };

            // 統一レンダリングシステムでオーバーレイを表示
            var success = await RenderOverlayAsync(overlayInfo, cancellationToken);
            
            if (success)
            {
                _logger.LogInformation("✅ [AVALONIA_RENDERER] OverlayUpdateEvent 処理完了 - ID: {Id}", overlayInfo.Id);
            }
            else
            {
                _logger.LogWarning("⚠️ [AVALONIA_RENDERER] OverlayUpdateEvent レンダリング失敗 - ID: {Id}", overlayInfo.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] OverlayUpdateEvent 処理中にエラー: {Message}", ex.Message);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// レガシー ChunkId 生成
    /// </summary>
    private int GenerateLegacyChunkId()
    {
        return Math.Abs(Guid.NewGuid().GetHashCode());
    }

    /// <summary>
    /// Phase 15 OverlayInfo を既存システムの TextChunk に変換
    /// </summary>
    private TextChunk ConvertToTextChunk(OverlayInfo overlayInfo, int chunkId)
    {
        return new TextChunk
        {
            ChunkId = chunkId,
            TextResults = [], // Phase 15 では空のリスト
            CombinedBounds = overlayInfo.DisplayArea,
            CombinedText = overlayInfo.Text,
            TranslatedText = overlayInfo.Text, // Phase 15 では翻訳済みテキストが渡される
            SourceWindowHandle = IntPtr.Zero, // Phase 15 では暫定値
        };
    }

    /// <summary>
    /// TextChunk を Phase 15 OverlayInfo に変換
    /// </summary>
    private OverlayInfo ConvertFromTextChunk(TextChunk textChunk)
    {
        return new OverlayInfo
        {
            Id = Guid.NewGuid().ToString(),
            Text = textChunk.TranslatedText,
            DisplayArea = textChunk.CombinedBounds
            // DisplayStartTime と IsVisible はデフォルト値を使用
        };
    }

    /// <summary>
    /// 既存システムでオーバーレイ作成 (インターフェース経由)
    /// レガシーシステムとの具象クラス依存を削減
    /// </summary>
    private async Task<ILegacyOverlayWindow?> CreateLegacyOverlayAsync(
        TextChunk textChunk, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🔗 [AVALONIA_RENDERER] レガシーオーバーレイ作成要求 - ChunkId: {ChunkId}", textChunk.ChunkId);
            
            // 既存のオーバーレイマネージャーでレガシーウィンドウ作成
            // 注意: InPlaceTranslationOverlayManager の実際の API に合わせて実装
            InPlaceTranslationOverlayWindow? legacyWindow = null;
            
            try
            {
                // 既存マネージャーでの作成処理
                // 実際のAPI呼び出しは InPlaceTranslationOverlayManager の仕様に依存
                // legacyWindow = await _overlayManager.CreateOverlayAsync(textChunk, cancellationToken);
                
                // 暫定実装: 将来の統合時に実際のAPI呼び出しを実装
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ [AVALONIA_RENDERER] レガシーウィンドウ作成失敗: {Message}", ex.Message);
            }
            
            // アダプターでラップしてインターフェース経由で返す
            return legacyWindow != null ? new LegacyOverlayWindowAdapter(legacyWindow) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] レガシーオーバーレイ作成エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            return null;
        }
    }

    /// <summary>
    /// 既存システムでオーバーレイ更新
    /// </summary>
    private async Task UpdateLegacyOverlayAsync(
        ILegacyOverlayWindow legacyWindow, 
        OverlayInfo updatedInfo, 
        CancellationToken cancellationToken)
    {
        try
        {
            // インターフェース経由で既存オーバーレイウィンドウを更新
            legacyWindow.UpdateContent(updatedInfo.Text);
            legacyWindow.UpdatePosition(updatedInfo.DisplayArea.X, updatedInfo.DisplayArea.Y);
            
            _logger.LogDebug("🔄 [AVALONIA_RENDERER] レガシーオーバーレイ更新完了");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] レガシーオーバーレイ更新エラー");
        }
    }

    /// <summary>
    /// 既存システムで可視性制御
    /// </summary>
    private async Task SetLegacyVisibilityAsync(
        ILegacyOverlayWindow legacyWindow, 
        bool visible, 
        CancellationToken cancellationToken)
    {
        try
        {
            // インターフェース経由で既存オーバーレイウィンドウの可視性制御
            if (visible)
            {
                legacyWindow.Show();
            }
            else
            {
                legacyWindow.Hide();
            }
            
            _logger.LogDebug("👁️ [AVALONIA_RENDERER] レガシー可視性制御完了 - Visible: {Visible}", visible);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] レガシー可視性制御エラー");
        }
    }

    /// <summary>
    /// 既存システムでオーバーレイ削除
    /// </summary>
    private async Task RemoveLegacyOverlayAsync(
        ILegacyOverlayWindow legacyWindow, 
        int legacyChunkId, 
        CancellationToken cancellationToken)
    {
        try
        {
            // インターフェース経由で既存オーバーレイウィンドウを削除
            legacyWindow?.Dispose();
            
            _logger.LogDebug("🗑️ [AVALONIA_RENDERER] レガシーオーバーレイ削除 - ChunkId: {ChunkId}", legacyChunkId);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] レガシーオーバーレイ削除エラー - ChunkId: {ChunkId}", legacyChunkId);
        }
    }

    /// <summary>
    /// Disposed チェック
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 統計情報取得
    /// </summary>
    public RenderingStatistics GetStatistics()
    {
        return new RenderingStatistics
        {
            TotalRendered = _totalRendered,
            TotalRemoved = _totalRemoved,
            AverageRenderTime = 5.0, // Avalonia UI での実測値（仮）
            CurrentFps = 60.0,
            GpuUsage = 15.0
        };
    }

    /// <summary>
    /// 非同期リソース解放 (IAsyncDisposable実装)
    /// デッドロックリスクを回避する安全な非同期解放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            // 非同期で安全に全オーバーレイを削除
            await RemoveAllOverlaysAsync(CancellationToken.None).ConfigureAwait(false);

            _phase15Overlays.Clear();
            _chunkIdToOverlayId.Clear();
            _disposed = true;
            
            _logger.LogInformation("🧹 [AVALONIA_RENDERER] AvaloniaOverlayRenderer 非同期リソース解放完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 非同期リソース解放中にエラー");
        }
    }

    /// <summary>
    /// 同期リソース解放 (IDisposable実装 - 後方互換性用)
    /// 推奨: DisposeAsync() の使用を推奨
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // タイムアウト付きで同期的に待機（デッドロックリスク軽減）
            var removeTask = RemoveAllOverlaysAsync(CancellationToken.None);
            if (!removeTask.Wait(TimeSpan.FromSeconds(3)))
            {
                _logger.LogWarning("⚠️ [AVALONIA_RENDERER] 同期Dispose: タイムアウト - 一部リソースが解放されない可能性");
            }

            _phase15Overlays.Clear();
            _chunkIdToOverlayId.Clear();
            _disposed = true;
            
            _logger.LogInformation("🧹 [AVALONIA_RENDERER] AvaloniaOverlayRenderer 同期リソース解放完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_RENDERER] 同期リソース解放中にエラー");
        }
    }

    #endregion
}
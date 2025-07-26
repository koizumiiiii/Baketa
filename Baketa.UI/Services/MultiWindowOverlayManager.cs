using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.Views.Overlay;
using Baketa.Core.Utilities;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Baketa.UI.Services;

/// <summary>
/// 複数ウィンドウオーバーレイマネージャーの実装
/// Phase 2-C: 座標ベース翻訳表示のための複数ウィンドウ管理システム
/// </summary>
public sealed class MultiWindowOverlayManager : IMultiWindowOverlayManager, IDisposable
{
    private readonly ILogger<MultiWindowOverlayManager>? _logger;
    private readonly SemaphoreSlim _operationSemaphore;
    private readonly ConcurrentDictionary<int, TranslationOverlayWindow> _overlayWindows = new();
    private readonly ConcurrentDictionary<int, OverlayWindowInfo> _windowInfos = new();
    private readonly ConcurrentDictionary<int, (string CombinedText, DrawingRectangle CombinedBounds)> _chunkDataCache = new();
    
    private OverlayDisplayOptions _currentOptions = new();
    private bool _disposed;

    public MultiWindowOverlayManager(ILogger<MultiWindowOverlayManager>? logger = null)
    {
        try
        {
            System.Console.WriteLine("🔥🔥🔥 MultiWindowOverlayManager コンストラクタ開始");
            DebugLogUtility.WriteLog("🔥🔥🔥 MultiWindowOverlayManager コンストラクタ開始");
            
            _logger = logger;
            _operationSemaphore = new SemaphoreSlim(1, 1);
            
            System.Console.WriteLine("🔥🔥🔥 MultiWindowOverlayManager コンストラクタ完了");
            DebugLogUtility.WriteLog("🔥🔥🔥 MultiWindowOverlayManager コンストラクタ完了");
            _logger?.LogInformation("🖼️ MultiWindowOverlayManager initialized");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌❌❌ MultiWindowOverlayManager コンストラクタエラー: {ex.Message}");
            System.Console.WriteLine($"❌❌❌ スタックトレース: {ex.StackTrace}");
            DebugLogUtility.WriteLog($"❌❌❌ MultiWindowOverlayManager コンストラクタエラー: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// テキストチャンクのリストを複数のオーバーレイウィンドウで表示
    /// ユーザー要求: 「テキストの塊ごとに」「複数のウィンドウで」「対象のテキストの座標位置付近に表示」
    /// </summary>
    public async Task DisplayTranslationResultsAsync(
        IReadOnlyList<TextChunk> chunks, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            System.Console.WriteLine($"🔥🔥🔥 DisplayTranslationResultsAsync呼び出し開始 - chunks: {chunks?.Count ?? -1}");
            DebugLogUtility.WriteLog($"🔥🔥🔥 DisplayTranslationResultsAsync呼び出し開始 - chunks: {chunks?.Count ?? -1}");
            
            System.Console.WriteLine($"🔍 _disposed: {_disposed}");
            DebugLogUtility.WriteLog($"🔍 _disposed: {_disposed}");
            
            ThrowIfDisposed();
            
            if (chunks == null || chunks.Count == 0)
            {
                _logger?.LogDebug("📝 表示対象のチャンクが0個のため処理をスキップ");
                System.Console.WriteLine("📝 表示対象のチャンクが0個のため処理をスキップ");
                DebugLogUtility.WriteLog("📝 表示対象のチャンクが0個のため処理をスキップ");
                return;
            }

            System.Console.WriteLine($"🔒 セマフォ取得開始");
            await _operationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            System.Console.WriteLine($"🔓 セマフォ取得完了");
            
            try
            {
                _logger?.LogInformation("🖼️ 翻訳結果表示開始 - チャンク数: {ChunkCount}", chunks.Count);
                System.Console.WriteLine($"🖼️ DisplayTranslationResultsAsync開始 - チャンク数: {chunks.Count}");
            
            // チャンクの詳細をログ出力
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                System.Console.WriteLine($"  チャンク[{i}]: ID={chunk.ChunkId}, テキスト='{chunk.CombinedText}', 翻訳='{chunk.TranslatedText}', 位置=({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y})");
            }

            // 既存のチャンクと比較して変化を検出
            var chunksToRemove = new List<int>();
            var chunksToUpdate = new List<TextChunk>();
            var chunksToAdd = new List<TextChunk>();

            // 新しいチャンクをテキストと位置でインデックス化
            var newChunkLookup = chunks.ToDictionary(
                c => (c.CombinedText, c.CombinedBounds),
                c => c
            );

            // 既存のオーバーレイをチェック
            foreach (var (chunkId, windowInfo) in _windowInfos.ToList())
            {
                var existingKey = GetChunkKey(chunkId);
                
                if (_overlayWindows.TryGetValue(chunkId, out _) && newChunkLookup.TryGetValue(existingKey, out var newChunk))
                {
                    // 同じテキストと位置のチャンクが存在する場合は保持
                    newChunkLookup.Remove(existingKey);
                    
                    // 翻訳テキストが変わった場合のみ更新
                    if (GetChunkTranslatedText(chunkId) != newChunk.TranslatedText)
                    {
                        chunksToUpdate.Add(newChunk);
                    }
                }
                else
                {
                    // 対応するチャンクがない場合は削除対象
                    chunksToRemove.Add(chunkId);
                }
            }

            // 残りは新規追加
            chunksToAdd.AddRange(newChunkLookup.Values);

            _logger?.LogInformation("📊 チャンク変化検出 - 削除: {RemoveCount}, 更新: {UpdateCount}, 追加: {AddCount}",
                chunksToRemove.Count, chunksToUpdate.Count, chunksToAdd.Count);

            // 削除対象のオーバーレイを非表示
            foreach (var chunkId in chunksToRemove)
            {
                await HideOverlayInternalAsync(chunkId).ConfigureAwait(false);
            }

            // 更新対象のオーバーレイを更新
            foreach (var chunk in chunksToUpdate)
            {
                await UpdateOverlayAsync(chunk.ChunkId, chunk, cancellationToken).ConfigureAwait(false);
            }

            // 新規チャンクのみ表示処理を実行
            if (chunksToAdd.Count > 0)
            {
                // 画面境界を取得
                var screenBounds = GetPrimaryScreenBounds();
                
                // 既存のオーバーレイの占有領域を収集
                var occupiedRegions = new List<DrawingRectangle>();
                foreach (var windowInfo in _windowInfos.Values)
                {
                    occupiedRegions.Add(new DrawingRectangle(
                        windowInfo.Position.X, windowInfo.Position.Y,
                        windowInfo.Size.Width, windowInfo.Size.Height));
                }

                // 新規チャンクごとにオーバーレイウィンドウを作成・表示
                var displayTasks = chunksToAdd.Select(async chunk =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // テキストサイズを測定
                    var textSize = MeasureTranslatedTextSize(chunk.TranslatedText);
                    
                    // 最適な表示位置を計算（衝突回避付き）
                    var position = CalculateOptimalPositionWithCollisionAvoidance(
                        chunk, textSize, screenBounds, occupiedRegions);

                    // 占有領域を記録
                    var overlayRect = new DrawingRectangle(position.X, position.Y, textSize.Width, textSize.Height);
                    lock (occupiedRegions)
                    {
                        occupiedRegions.Add(overlayRect);
                    }

                    // オーバーレイウィンドウを作成・表示
                    var overlayWindow = await CreateAndShowOverlayAsync(chunk, position, textSize, cancellationToken).ConfigureAwait(false);
                    
                    // ウィンドウ情報を記録
                    _overlayWindows[chunk.ChunkId] = overlayWindow;
                    _windowInfos[chunk.ChunkId] = new OverlayWindowInfo
                    {
                        ChunkId = chunk.ChunkId,
                        Position = position,
                        Size = textSize,
                        State = OverlayState.Visible,
                        SourceWindowHandle = chunk.SourceWindowHandle
                    };
                    
                    // チャンクデータをキャッシュに追加
                    _chunkDataCache[chunk.ChunkId] = (chunk.CombinedText, chunk.CombinedBounds);

                    _logger?.LogInformation("📺 オーバーレイ表示完了 - ChunkId: {ChunkId} | Position: ({X},{Y}) | Size: ({W},{H}) | Text: '{Text}'",
                        chunk.ChunkId, position.X, position.Y, textSize.Width, textSize.Height, chunk.TranslatedText);
                        
                    // デバッグ用にウィンドウ数を出力
                    System.Console.WriteLine($"🪟 現在の表示中オーバーレイ数: {_overlayWindows.Count}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ チャンク表示エラー - ChunkId: {ChunkId}", chunk.ChunkId);
                }
            });

                await Task.WhenAll(displayTasks).ConfigureAwait(false);
            }
            
            _logger?.LogInformation("✅ 翻訳結果表示完了 - 表示中オーバーレイ数: {Count}", _overlayWindows.Count);
            
            // 連続表示時のメモリ管理
            if (_overlayWindows.Count > 20) // オーバーレイが多い場合
            {
                _logger?.LogDebug("🧹 メモリ最適化実行中...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        finally
        {
            _operationSemaphore.Release();
        }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌❌❌ DisplayTranslationResultsAsyncでエラー発生: {ex.Message}");
            System.Console.WriteLine($"❌❌❌ エラータイプ: {ex.GetType().Name}");
            System.Console.WriteLine($"❌❌❌ スタックトレース: {ex.StackTrace}");
            DebugLogUtility.WriteLog($"❌❌❌ DisplayTranslationResultsAsyncでエラー発生: {ex.Message}");
            DebugLogUtility.WriteLog($"❌❌❌ エラータイプ: {ex.GetType().Name}");
            DebugLogUtility.WriteLog($"❌❌❌ スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "❌ 翻訳結果表示でエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 特定のチャンクIDのオーバーレイを更新
    /// </summary>
    public async Task UpdateOverlayAsync(
        int chunkId, 
        TextChunk chunk, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        await _operationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            if (!_overlayWindows.TryGetValue(chunkId, out var existingWindow))
            {
                _logger?.LogWarning("⚠️ 更新対象のオーバーレイが見つかりません - ChunkId: {ChunkId}", chunkId);
                return;
            }

            _logger?.LogInformation("🔄 オーバーレイ更新開始 - ChunkId: {ChunkId}", chunkId);

            // 新しいテキストサイズを測定
            var newTextSize = MeasureTranslatedTextSize(chunk.TranslatedText);
            var screenBounds = GetPrimaryScreenBounds();
            
            // 新しい位置を計算
            var newPosition = chunk.CalculateOptimalOverlayPosition(newTextSize, screenBounds);
            
            // ウィンドウを更新
            await existingWindow.UpdateContentAsync(chunk.TranslatedText, newPosition, newTextSize, cancellationToken).ConfigureAwait(false);
            
            // ウィンドウ情報を更新
            if (_windowInfos.TryGetValue(chunkId, out var windowInfo))
            {
                windowInfo.LastUpdatedAt = DateTime.UtcNow;
            }

            _logger?.LogInformation("✅ オーバーレイ更新完了 - ChunkId: {ChunkId}", chunkId);
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <summary>
    /// すべてのオーバーレイウィンドウを非表示にする
    /// </summary>
    public async Task HideAllOverlaysAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        await _operationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            await HideAllOverlaysInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <summary>
    /// 特定のチャンクIDのオーバーレイを非表示にする
    /// </summary>
    public async Task HideOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        await _operationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            if (_overlayWindows.TryRemove(chunkId, out var window))
            {
                await window.HideAsync(cancellationToken).ConfigureAwait(false);
                window.Dispose();
                
                _windowInfos.TryRemove(chunkId, out _);
                
                _logger?.LogInformation("🚫 オーバーレイ非表示 - ChunkId: {ChunkId}", chunkId);
            }
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <summary>
    /// オーバーレイの表示設定を更新
    /// </summary>
    public async Task ConfigureOverlayOptionsAsync(OverlayDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        
        await _operationSemaphore.WaitAsync().ConfigureAwait(false);
        
        try
        {
            _currentOptions = options;
            
            // 既存のオーバーレイに設定を適用
            var updateTasks = _overlayWindows.Values.Select(window => 
                window.ApplyDisplayOptionsAsync(options));
                
            await Task.WhenAll(updateTasks).ConfigureAwait(false);
            
            _logger?.LogInformation("⚙️ オーバーレイ設定更新完了 - 透明度: {Opacity}, フォントサイズ: {FontSize}", 
                options.Opacity, options.FontSize);
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <summary>
    /// アクティブなオーバーレイウィンドウの数を取得
    /// </summary>
    public int GetActiveOverlayCount()
    {
        ThrowIfDisposed();
        return _overlayWindows.Count;
    }

    /// <summary>
    /// 特定の領域と重複するオーバーレイを検出
    /// </summary>
    public IReadOnlyList<int> GetOverlappingOverlays(DrawingRectangle region)
    {
        ThrowIfDisposed();
        
        var overlapping = new List<int>();
        
        foreach (var (chunkId, windowInfo) in _windowInfos)
        {
            var windowRect = new DrawingRectangle(windowInfo.Position.X, windowInfo.Position.Y, 
                windowInfo.Size.Width, windowInfo.Size.Height);
                
            if (windowRect.IntersectsWith(region))
            {
                overlapping.Add(chunkId);
            }
        }
        
        return overlapping.AsReadOnly();
    }

    /// <summary>
    /// オーバーレイマネージャーのリソースをクリーンアップ
    /// </summary>
    public async Task CleanupAsync()
    {
        if (_disposed) return;
        
        try
        {
            await HideAllOverlaysAsync().ConfigureAwait(false);
            _logger?.LogInformation("🧹 MultiWindowOverlayManager cleanup completed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Cleanup error");
        }
    }

    // チャンク管理用のヘルパーメソッド
    private (string CombinedText, DrawingRectangle CombinedBounds) GetChunkKey(int chunkId)
    {
        return _chunkDataCache.TryGetValue(chunkId, out var data) ? data : default;
    }
    
    private string GetChunkTranslatedText(int chunkId)
    {
        if (_overlayWindows.TryGetValue(chunkId, out var window))
        {
            return window.TranslatedText;
        }
        return string.Empty;
    }
    
    private async Task HideOverlayInternalAsync(int chunkId)
    {
        if (_overlayWindows.TryRemove(chunkId, out var window))
        {
            try
            {
                await window.HideAsync().ConfigureAwait(false);
                window.Dispose();
                _windowInfos.TryRemove(chunkId, out _);
                _chunkDataCache.TryRemove(chunkId, out _);
                _logger?.LogDebug("🚫 オーバーレイ非表示 - ChunkId: {ChunkId}", chunkId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ オーバーレイ非表示エラー - ChunkId: {ChunkId}", chunkId);
            }
        }
    }

    /// <summary>
    /// すべてのオーバーレイを非表示（内部用）
    /// </summary>
    private async Task HideAllOverlaysInternalAsync()
    {
        if (_overlayWindows.IsEmpty) return;
        
        _logger?.LogInformation("🚫 全オーバーレイ非表示開始 - 対象数: {Count}", _overlayWindows.Count);
        
        var hideTasks = _overlayWindows.Values.Select(async window =>
        {
            try
            {
                await window.HideAsync().ConfigureAwait(false);
                window.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ オーバーレイ非表示エラー");
            }
        });
        
        await Task.WhenAll(hideTasks).ConfigureAwait(false);
        
        _overlayWindows.Clear();
        _windowInfos.Clear();
        
        _logger?.LogInformation("✅ 全オーバーレイ非表示完了");
    }

    /// <summary>
    /// オーバーレイウィンドウを作成・表示
    /// </summary>
    private async Task<TranslationOverlayWindow> CreateAndShowOverlayAsync(
        TextChunk chunk, 
        DrawingPoint position, 
        DrawingSize textSize, 
        CancellationToken cancellationToken)
    {
        try
        {
            System.Console.WriteLine($"🛠️ TranslationOverlayWindow作成開始 - ChunkId: {chunk.ChunkId}");
            System.Console.WriteLine($"📋 位置: ({position.X}, {position.Y}), サイズ: ({textSize.Width}, {textSize.Height})");
            
            // UIスレッドでTranslationOverlayWindowを作成
            TranslationOverlayWindow? overlayWindow = null;
            Exception? creationError = null;
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    System.Console.WriteLine($"🎭 UIスレッドでウィンドウ作成開始");
                    overlayWindow = new TranslationOverlayWindow
                    {
                        ChunkId = chunk.ChunkId,
                        OriginalText = chunk.CombinedText,
                        TranslatedText = chunk.TranslatedText,
                        TargetBounds = chunk.CombinedBounds,
                        SourceWindowHandle = chunk.SourceWindowHandle
                    };
                    System.Console.WriteLine($"✅ UIスレッドでウィンドウ作成成功");
                }
                catch (Exception ex)
                {
                    creationError = ex;
                    System.Console.WriteLine($"❌ UIスレッドでウィンドウ作成エラー: {ex.Message}");
                }
            }, Avalonia.Threading.DispatcherPriority.Normal, CancellationToken.None);
            
            if (creationError != null)
            {
                throw creationError;
            }
            
            if (overlayWindow == null)
            {
                throw new InvalidOperationException("Failed to create TranslationOverlayWindow");
            }
            
            System.Console.WriteLine("✅ TranslationOverlayWindow作成成功");

            System.Console.WriteLine("🎯 ShowAtPositionAsync呼び出し開始");
            await overlayWindow.ShowAtPositionAsync(position, textSize, _currentOptions, cancellationToken).ConfigureAwait(false);
            
            System.Console.WriteLine("✅ ShowAtPositionAsync完了");
            return overlayWindow;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ CreateAndShowOverlayAsyncエラー: {ex.Message}");
            System.Console.WriteLine($"❌ エラータイプ: {ex.GetType().Name}");
            System.Console.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "❌ オーバーレイウィンドウ作成・表示エラー - ChunkId: {ChunkId}", chunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 翻訳テキストのサイズを測定
    /// </summary>
    private DrawingSize MeasureTranslatedTextSize(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
            return new DrawingSize(100, 30);

        // 簡易サイズ計算（実際の実装では TextMeasurementService を使用）
        var charCount = translatedText.Length;
        var lineCount = Math.Max(1, translatedText.Count(c => c == '\n') + 1);
        
        var width = Math.Min(_currentOptions.MaxWidth, 
            Math.Max(200, charCount * _currentOptions.FontSize * 0.6));
        var height = Math.Min(_currentOptions.MaxHeight, 
            lineCount * (_currentOptions.FontSize + 4) + _currentOptions.Padding * 2);
            
        return new DrawingSize((int)width, (int)height);
    }

    /// <summary>
    /// 衝突回避付き最適位置計算
    /// </summary>
    private DrawingPoint CalculateOptimalPositionWithCollisionAvoidance(
        TextChunk chunk, 
        DrawingSize overlaySize, 
        DrawingRectangle screenBounds, 
        List<DrawingRectangle> occupiedRegions)
    {
        // 基本位置（TextChunkの計算を使用）
        var basePosition = chunk.CalculateOptimalOverlayPosition(overlaySize, screenBounds);
        var candidateRect = new DrawingRectangle(basePosition.X, basePosition.Y, overlaySize.Width, overlaySize.Height);

        // 衝突チェック
        bool hasCollision;
        lock (occupiedRegions)
        {
            hasCollision = occupiedRegions.Any(rect => rect.IntersectsWith(candidateRect));
        }
        
        if (!hasCollision)
        {
            return basePosition;
        }

        // 衝突がある場合の代替位置を計算
        var alternatives = new[]
        {
            // 右側
            new DrawingPoint(chunk.CombinedBounds.Right + _currentOptions.MinOverlayDistance, chunk.CombinedBounds.Y),
            // 左側
            new DrawingPoint(chunk.CombinedBounds.X - overlaySize.Width - _currentOptions.MinOverlayDistance, chunk.CombinedBounds.Y),
            // 上側
            new DrawingPoint(chunk.CombinedBounds.X, chunk.CombinedBounds.Y - overlaySize.Height - _currentOptions.MinOverlayDistance),
            // 下側（少しずらし）
            new DrawingPoint(chunk.CombinedBounds.X + 20, chunk.CombinedBounds.Bottom + _currentOptions.MinOverlayDistance)
        };

        foreach (var altPosition in alternatives)
        {
            var altRect = new DrawingRectangle(altPosition.X, altPosition.Y, overlaySize.Width, overlaySize.Height);
            
            // 画面内チェック
            if (!screenBounds.Contains(altRect))
                continue;
                
            // 衝突チェック
            bool altHasCollision;
            lock (occupiedRegions)
            {
                altHasCollision = occupiedRegions.Any(rect => rect.IntersectsWith(altRect));
            }
            
            if (!altHasCollision)
            {
                return altPosition;
            }
        }

        // すべて衝突する場合は重複表示
        _logger?.LogWarning("⚠️ オーバーレイ位置で衝突回避不可 - ChunkId: {ChunkId}, 重複表示", chunk.ChunkId);
        return basePosition;
    }

    /// <summary>
    /// プライマリ画面の境界を取得
    /// </summary>
    private static DrawingRectangle GetPrimaryScreenBounds()
    {
        // TODO: マルチモニター対応
        return new DrawingRectangle(0, 0, 1920, 1080); // 仮の値
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // 同期的にクリーンアップ
            Task.Run(async () => await CleanupAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
            
            _operationSemaphore?.Dispose();
            _disposed = true;
            
            _logger?.LogInformation("🧹 MultiWindowOverlayManager disposed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Dispose error");
        }
    }
}
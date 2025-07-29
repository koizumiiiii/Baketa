#pragma warning disable CS0618 // Type or member is obsolete
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
/// AR風UIに置き換えられたため非推奨
/// </summary>
[Obsolete("AR風翻訳UIに置き換えられました。ARTranslationOverlayManagerを使用してください。")]
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
                
                // 既存のオーバーレイをすべて非表示にする
                await HideAllOverlaysAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            
            // 有効なテキストを持つチャンクのみフィルタリング
            var validChunks = chunks.Where(chunk => 
                !string.IsNullOrWhiteSpace(chunk.CombinedText) && 
                !string.IsNullOrWhiteSpace(chunk.TranslatedText)
            ).ToList();
            
            if (validChunks.Count == 0)
            {
                _logger?.LogDebug("📝 有効なテキストを持つチャンクが0個のため、オーバーレイを非表示");
                System.Console.WriteLine("📝 有効なテキストを持つチャンクが0個のため、オーバーレイを非表示");
                DebugLogUtility.WriteLog("📝 有効なテキストを持つチャンクが0個のため、オーバーレイを非表示");
                
                // テキストがない場合はオーバーレイを非表示
                await HideAllOverlaysAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            
            // 以降の処理は有効なチャンクのみで実行
            chunks = validChunks;

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
                    
                    // 詳細デバッグログ: 座標計算情報
                    System.Console.WriteLine($"🎯 座標計算詳細 - ChunkId: {chunk.ChunkId}");
                    System.Console.WriteLine($"   📐 OCRテキスト領域: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y}) サイズ:({chunk.CombinedBounds.Width}x{chunk.CombinedBounds.Height})");
                    System.Console.WriteLine($"   📏 翻訳ウィンドウサイズ: ({textSize.Width}x{textSize.Height})");
                    System.Console.WriteLine($"   🖥️ スクリーン領域: ({screenBounds.X},{screenBounds.Y}) サイズ:({screenBounds.Width}x{screenBounds.Height})");
                    System.Console.WriteLine($"   🎮 ソースウィンドウハンドル: 0x{chunk.SourceWindowHandle.ToInt64():X8}");

                    // ウィンドウ相対座標をスクリーン絶対座標に変換
                    var correctedChunk = await ConvertToScreenCoordinatesAsync(chunk).ConfigureAwait(false);
                    System.Console.WriteLine($"   🔄 座標変換後: ({correctedChunk.CombinedBounds.X},{correctedChunk.CombinedBounds.Y}) サイズ:({correctedChunk.CombinedBounds.Width}x{correctedChunk.CombinedBounds.Height})");

                    // 最適な表示位置を計算（衝突回避付き）
                    var position = CalculateOptimalPositionWithCollisionAvoidance(
                        correctedChunk, textSize, screenBounds, occupiedRegions);

                    System.Console.WriteLine($"   🎯 最終位置決定: ({position.X},{position.Y})");
                    System.Console.WriteLine($"   📊 座標反映確認: 元OCR位置({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y}) → 変換後({correctedChunk.CombinedBounds.X},{correctedChunk.CombinedBounds.Y}) → オーバーレイ位置({position.X},{position.Y})");

                    // 占有領域を記録
                    var overlayRect = new DrawingRectangle(position.X, position.Y, textSize.Width, textSize.Height);
                    lock (occupiedRegions)
                    {
                        occupiedRegions.Add(overlayRect);
                    }

                    // オーバーレイウィンドウを作成・表示（座標修正済みチャンクを使用）
                    var overlayWindow = await CreateAndShowOverlayAsync(correctedChunk, position, textSize, cancellationToken).ConfigureAwait(false);
                    
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
                    
                    // チャンクデータをキャッシュに追加（変換後の座標を使用）
                    _chunkDataCache[chunk.ChunkId] = (correctedChunk.CombinedText, correctedChunk.CombinedBounds);

                    _logger?.LogInformation("📺 オーバーレイ表示完了 - ChunkId: {ChunkId} | 元OCR位置: ({OrigX},{OrigY}) | 変換後位置: ({CorrX},{CorrY}) | オーバーレイ位置: ({X},{Y}) | Size: ({W},{H}) | Text: '{Text}'",
                        chunk.ChunkId, chunk.CombinedBounds.X, chunk.CombinedBounds.Y, correctedChunk.CombinedBounds.X, correctedChunk.CombinedBounds.Y, position.X, position.Y, textSize.Width, textSize.Height, chunk.TranslatedText);
                        
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
    /// 翻訳テキストのサイズを測定（改良版）
    /// </summary>
    private DrawingSize MeasureTranslatedTextSize(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
            return new DrawingSize(100, 30);

        // 改良されたサイズ計算: テキストの実際の内容を考慮
        var text = translatedText.Trim();
        
        // 日本語文字とアルファベットの比率を考慮した幅計算
        var japaneseCharCount = text.Count(c => IsJapaneseCharacter(c));
        var otherCharCount = text.Length - japaneseCharCount;
        
        // 日本語文字は幅が広い、アルファベットは狭い
        var estimatedWidth = japaneseCharCount * (_currentOptions.FontSize * 1.0) + 
                           otherCharCount * (_currentOptions.FontSize * 0.6);
        
        // 改行を考慮した行数計算
        var lines = text.Split('\n', StringSplitOptions.None);
        var lineCount = lines.Length;
        
        // 最長行の幅を基準とする
        var maxLineWidth = lines.Max(line => 
        {
            var jpnCount = line.Count(IsJapaneseCharacter);
            var othCount = line.Length - jpnCount;
            return jpnCount * (_currentOptions.FontSize * 1.0) + othCount * (_currentOptions.FontSize * 0.6);
        });
        
        // 実際の幅: 最長行幅 + パディング
        var actualWidth = Math.Min(_currentOptions.MaxWidth,
            Math.Max(150, (int)maxLineWidth + _currentOptions.Padding * 2));
        
        // 高さ: 行数 × 行高 + パディング
        var lineHeight = _currentOptions.FontSize + 6; // 行間を含む
        var actualHeight = Math.Min(_currentOptions.MaxHeight,
            lineCount * lineHeight + _currentOptions.Padding * 2);
            
        return new DrawingSize(actualWidth, actualHeight);
    }
    
    /// <summary>
    /// 日本語文字かどうかを判定
    /// </summary>
    private static bool IsJapaneseCharacter(char c)
    {
        // ひらがな、カタカナ、漢字の範囲をチェック
        return (c >= 0x3040 && c <= 0x309F) || // ひらがな
               (c >= 0x30A0 && c <= 0x30FF) || // カタカナ
               (c >= 0x4E00 && c <= 0x9FAF) || // 漢字
               (c >= 0x3400 && c <= 0x4DBF);   // 拡張漢字
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
    /// プライマリ画面の境界を取得（改良版）
    /// </summary>
    private static DrawingRectangle GetPrimaryScreenBounds()
    {
        try
        {
            // Win32 APIを使用してプライマリディスプレイサイズを取得
            var screenWidth = GetSystemMetrics(0);  // SM_CXSCREEN
            var screenHeight = GetSystemMetrics(1); // SM_CYSCREEN
            
            if (screenWidth > 0 && screenHeight > 0)
            {
                return new DrawingRectangle(0, 0, screenWidth, screenHeight);
            }
        }
        catch
        {
            // Win32 API呼び出し失敗時はフォールバック
        }
        
        // フォールバック: 一般的なFHD解像度
        return new DrawingRectangle(0, 0, 1920, 1080);
    }
    
    /// <summary>
    /// Win32 API - GetSystemMetrics
    /// </summary>
#pragma warning disable SYSLIB1054 // P/Invokeコード生成のためDllImportを使用
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Win32 API - ClientToScreen (クライアント座標をスクリーン座標に変換)
    /// </summary>
#pragma warning disable SYSLIB1054 // P/Invokeコード生成のためDllImportを使用
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Win32 API - GetClientRect (クライアント矩形を取得)
    /// </summary>
#pragma warning disable SYSLIB1054 // P/Invokeコード生成のためDllImportを使用
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Win32 API - GetWindowRect (ウィンドウ矩形を取得)
    /// </summary>
#pragma warning disable SYSLIB1054 // P/Invokeコード生成のためDllImportを使用
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Win32 API - IsWindow (ウィンドウハンドルが有効かチェック)
    /// </summary>
#pragma warning disable SYSLIB1054 // P/Invokeコード生成のためDllImportを使用
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Win32 POINT構造体
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Win32 RECT構造体
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// ウィンドウ相対座標をスクリーン絶対座標に変換
    /// ゲーム内テキストの正確な位置を反映するための座標変換
    /// </summary>
    private async Task<TextChunk> ConvertToScreenCoordinatesAsync(TextChunk chunk)
    {
        await Task.CompletedTask.ConfigureAwait(false); // 非同期形式維持のため

        try
        {
            // ウィンドウハンドルが無効な場合はそのまま返す
            if (chunk.SourceWindowHandle == IntPtr.Zero || !IsWindow(chunk.SourceWindowHandle))
            {
                System.Console.WriteLine($"⚠️ 無効なウィンドウハンドル、座標変換スキップ: 0x{chunk.SourceWindowHandle.ToInt64():X8}");
                return chunk;
            }

            // クライアント座標からスクリーン座標に変換
            var topLeft = new POINT { X = chunk.CombinedBounds.X, Y = chunk.CombinedBounds.Y };
            var bottomRight = new POINT { X = chunk.CombinedBounds.Right, Y = chunk.CombinedBounds.Bottom };

            bool success1 = ClientToScreen(chunk.SourceWindowHandle, ref topLeft);
            bool success2 = ClientToScreen(chunk.SourceWindowHandle, ref bottomRight);

            if (!success1 || !success2)
            {
                System.Console.WriteLine($"❌ ClientToScreen変換失敗、元の座標を使用: HWND=0x{chunk.SourceWindowHandle.ToInt64():X8}");
                return chunk;
            }

            // ウィンドウ情報の詳細デバッグ
            if (GetClientRect(chunk.SourceWindowHandle, out RECT clientRect))
            {
                System.Console.WriteLine($"📏 クライアント矩形: ({clientRect.Left},{clientRect.Top}) - ({clientRect.Right},{clientRect.Bottom})");
            }
            
            if (GetWindowRect(chunk.SourceWindowHandle, out RECT windowRect))
            {
                System.Console.WriteLine($"🖼️ ウィンドウ矩形: ({windowRect.Left},{windowRect.Top}) - ({windowRect.Right},{windowRect.Bottom})");
            }

            // 変換された座標で新しいバウンディングボックスを作成
            var convertedBounds = new DrawingRectangle(
                topLeft.X, 
                topLeft.Y, 
                bottomRight.X - topLeft.X, 
                bottomRight.Y - topLeft.Y);

            System.Console.WriteLine($"🔄 座標変換詳細:");
            System.Console.WriteLine($"   元の座標: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y}) - ({chunk.CombinedBounds.Right},{chunk.CombinedBounds.Bottom})");
            System.Console.WriteLine($"   変換後座標: ({topLeft.X},{topLeft.Y}) - ({bottomRight.X},{bottomRight.Y})");
            System.Console.WriteLine($"   バウンディング: ({convertedBounds.X},{convertedBounds.Y}) サイズ:({convertedBounds.Width}x{convertedBounds.Height})");

            // 座標変換済みの新しいTextChunkを作成
            return new TextChunk
            {
                ChunkId = chunk.ChunkId,
                TextResults = chunk.TextResults,
                CombinedBounds = convertedBounds,
                CombinedText = chunk.CombinedText,
                TranslatedText = chunk.TranslatedText,
                SourceWindowHandle = chunk.SourceWindowHandle,
                DetectedLanguage = chunk.DetectedLanguage
            };
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ 座標変換中に例外発生: {ex.Message}");
            _logger?.LogError(ex, "座標変換中に例外が発生");
            return chunk; // エラー時は元のチャンクを返す
        }
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
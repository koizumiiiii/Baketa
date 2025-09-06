using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.UI.Monitors;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.UI;

/// <summary>
/// オーバーレイ位置調整サービス実装
/// UltraThink Phase 10.3: Clean Architecture準拠の責務分離
/// TextChunkから分離されたUI位置計算責務を担当
/// </summary>
public sealed class OverlayPositioningService : IOverlayPositioningService
{
    private readonly ILogger<OverlayPositioningService> _logger;
    
    public OverlayPositioningService(ILogger<OverlayPositioningService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// 精密オーバーレイ位置調整（DPI/マルチモニター対応）
    /// Phase 2対応の8段階精密位置調整システム
    /// </summary>
    public PositioningResult CalculateOptimalPosition(
        TextChunk textChunk,
        Size overlaySize,
        MonitorInfo targetMonitor,
        IReadOnlyList<Rectangle>? existingOverlayBounds = null,
        OverlayPositioningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        options ??= new OverlayPositioningOptions();
        existingOverlayBounds ??= [];
        
        try
        {
            _logger.LogDebug("精密位置調整開始 - ChunkId: {ChunkId}, Monitor: {MonitorName}", 
                textChunk.ChunkId, targetMonitor.Name);
                
            // 1. DPI/スケール補正適用
            var scaledTextBounds = ApplyDpiScaling(textChunk.CombinedBounds, targetMonitor);
            var scaledOverlaySize = ApplyDpiScaling(overlaySize, targetMonitor);
            var scaledMonitorBounds = ConvertWorkAreaToRectangle(targetMonitor);
            
            // 2. 8段階戦略による候補位置生成
            var candidatePositions = GenerateAdvancedCandidatePositions(
                scaledTextBounds, scaledOverlaySize, scaledMonitorBounds, options);
            
            // 3. 優先順位に基づく最適位置選択
            var selectedPosition = SelectOptimalPosition(
                candidatePositions, scaledOverlaySize, scaledMonitorBounds, 
                existingOverlayBounds, options);
            
            // 4. 結果情報構築
            var result = new PositioningResult
            {
                Position = selectedPosition.Position,
                UsedStrategy = selectedPosition.Strategy,
                TargetMonitor = targetMonitor,
                CollisionAvoidanceApplied = selectedPosition.CollisionAvoidanceApplied,
                DpiCorrectionApplied = true,
                ComputationTimeMs = stopwatch.Elapsed.TotalMilliseconds
            };
            
            _logger.LogDebug("精密位置調整完了 - Strategy: {Strategy}, Position: {Position}, Time: {TimeMs}ms", 
                result.UsedStrategy, result.Position, result.ComputationTimeMs);
                
            return result;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "位置調整引数エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            return CreateFallbackResult(textChunk, overlaySize, targetMonitor, existingOverlayBounds, stopwatch);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "位置調整処理エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            return CreateFallbackResult(textChunk, overlaySize, targetMonitor, existingOverlayBounds, stopwatch);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
    
    /// <summary>
    /// 基本位置調整（従来互換）
    /// </summary>
    public Point CalculateBasicPosition(TextChunk textChunk, Size overlaySize, Rectangle screenBounds)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        
        _logger.LogDebug("基本位置調整 - ChunkId: {ChunkId}", textChunk.ChunkId);
        
        return CalculateBasicPositionInternal(textChunk.CombinedBounds, overlaySize, screenBounds);
    }
    
    /// <summary>
    /// 衝突回避付き位置調整（従来互換）
    /// </summary>
    public Point CalculatePositionWithCollisionAvoidance(
        TextChunk textChunk, Size overlaySize, Rectangle screenBounds, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ArgumentNullException.ThrowIfNull(existingOverlayBounds);
        
        _logger.LogDebug("衝突回避位置調整 - ChunkId: {ChunkId}, ExistingOverlays: {Count}", 
            textChunk.ChunkId, existingOverlayBounds.Count);
        
        return CalculatePositionWithCollisionAvoidanceInternal(
            textChunk.CombinedBounds, overlaySize, screenBounds, existingOverlayBounds);
    }
    
    #region Private Helper Methods
    
    /// <summary>
    /// フォールバック結果作成
    /// </summary>
    private PositioningResult CreateFallbackResult(
        TextChunk textChunk, Size overlaySize, MonitorInfo targetMonitor, 
        IReadOnlyList<Rectangle> existingOverlayBounds, System.Diagnostics.Stopwatch stopwatch)
    {
        var fallbackPosition = CalculatePositionWithCollisionAvoidanceInternal(
            textChunk.CombinedBounds, overlaySize, ConvertWorkAreaToRectangle(targetMonitor), existingOverlayBounds);
            
        return new PositioningResult
        {
            Position = fallbackPosition,
            UsedStrategy = PositioningStrategy.ForcedClamp,
            TargetMonitor = targetMonitor,
            CollisionAvoidanceApplied = true,
            DpiCorrectionApplied = false,
            ComputationTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }
    
    /// <summary>
    /// MonitorInfo.WorkArea から System.Drawing.Rectangle への変換
    /// </summary>
    private static Rectangle ConvertWorkAreaToRectangle(MonitorInfo monitor)
    {
        var workArea = monitor.WorkArea;
        return new Rectangle(
            (int)Math.Round(workArea.X),
            (int)Math.Round(workArea.Y),
            (int)Math.Round(workArea.Width),
            (int)Math.Round(workArea.Height)
        );
    }
    
    /// <summary>
    /// DPI/スケール補正を適用
    /// </summary>
    private static Rectangle ApplyDpiScaling(Rectangle rect, MonitorInfo monitor)
    {
        return new Rectangle(
            (int)Math.Round(rect.X * monitor.ScaleFactorX),
            (int)Math.Round(rect.Y * monitor.ScaleFactorY),
            (int)Math.Round(rect.Width * monitor.ScaleFactorX),
            (int)Math.Round(rect.Height * monitor.ScaleFactorY)
        );
    }
    
    /// <summary>
    /// サイズにDPI/スケール補正を適用
    /// </summary>
    private static Size ApplyDpiScaling(Size size, MonitorInfo monitor)
    {
        return new Size(
            (int)Math.Round(size.Width * monitor.ScaleFactorX),
            (int)Math.Round(size.Height * monitor.ScaleFactorY)
        );
    }
    
    /// <summary>
    /// 8段階戦略による候補位置生成
    /// </summary>
    private static List<CandidatePosition> GenerateAdvancedCandidatePositions(
        Rectangle textBounds, Size overlaySize, Rectangle monitorBounds, OverlayPositioningOptions options)
    {
        var margin = options.StandardMargin;
        
        return new List<CandidatePosition>
        {
            // 1. テキスト直上（最優先：原文を隠さない）
            new() {
                Position = new Point(textBounds.X, textBounds.Y - overlaySize.Height - margin),
                Strategy = PositioningStrategy.AboveText,
                Priority = GetSafePriority(options.PreferredPositionPriority, 1)
            },
            
            // 2. テキスト直下
            new() {
                Position = new Point(textBounds.X, textBounds.Bottom + margin),
                Strategy = PositioningStrategy.BelowText,
                Priority = GetSafePriority(options.PreferredPositionPriority, 2)
            },
            
            // 3. テキスト右側
            new() {
                Position = new Point(textBounds.Right + margin, textBounds.Y),
                Strategy = PositioningStrategy.RightOfText,
                Priority = GetSafePriority(options.PreferredPositionPriority, 3)
            },
            
            // 4. テキスト左側
            new() {
                Position = new Point(textBounds.X - overlaySize.Width - margin, textBounds.Y),
                Strategy = PositioningStrategy.LeftOfText,
                Priority = GetSafePriority(options.PreferredPositionPriority, 4)
            },
            
            // 5. テキスト右上角
            new() {
                Position = new Point(textBounds.Right + margin, textBounds.Y - overlaySize.Height - margin),
                Strategy = PositioningStrategy.TopRightCorner,
                Priority = GetSafePriority(options.PreferredPositionPriority, 5)
            },
            
            // 6. テキスト左上角
            new() {
                Position = new Point(textBounds.X - overlaySize.Width - margin, textBounds.Y - overlaySize.Height - margin),
                Strategy = PositioningStrategy.TopLeftCorner,
                Priority = GetSafePriority(options.PreferredPositionPriority, 6)
            },
            
            // 7. テキスト右下角
            new() {
                Position = new Point(textBounds.Right + margin, textBounds.Bottom + margin),
                Strategy = PositioningStrategy.BottomRightCorner,
                Priority = GetSafePriority(options.PreferredPositionPriority, 7)
            },
            
            // 8. テキスト左下角
            new() {
                Position = new Point(textBounds.X - overlaySize.Width - margin, textBounds.Bottom + margin),
                Strategy = PositioningStrategy.BottomLeftCorner,
                Priority = GetSafePriority(options.PreferredPositionPriority, 8)
            }
        };
    }
    
    /// <summary>
    /// 安全な優先順位取得（設定ミス対策）
    /// </summary>
    private static int GetSafePriority(int[] priorities, int defaultStrategy)
    {
        var index = Array.IndexOf(priorities, defaultStrategy);
        return index >= 0 ? index + 1 : defaultStrategy; // 見つからない場合はデフォルト値使用
    }
    
    /// <summary>
    /// 優先順位に基づく最適位置選択
    /// </summary>
    private static SelectionResult SelectOptimalPosition(
        List<CandidatePosition> candidates, Size overlaySize, Rectangle monitorBounds,
        IReadOnlyList<Rectangle> existingOverlayBounds, OverlayPositioningOptions options)
    {
        var sortedCandidates = candidates.OrderBy(c => c.Priority).ToList();
        
        foreach (var candidate in sortedCandidates)
        {
            var candidateRect = new Rectangle(candidate.Position.X, candidate.Position.Y, 
                                            overlaySize.Width, overlaySize.Height);
            
            // 1. モニター境界チェック（マージン考慮）
            var adjustedMonitorBounds = new Rectangle(
                monitorBounds.X + options.MonitorBoundaryMargin,
                monitorBounds.Y + options.MonitorBoundaryMargin,
                monitorBounds.Width - (options.MonitorBoundaryMargin * 2),
                monitorBounds.Height - (options.MonitorBoundaryMargin * 2)
            );
            
            if (!adjustedMonitorBounds.Contains(candidateRect))
                continue;
            
            // 2. 衝突検知チェック
            if (HasSignificantCollision(candidateRect, existingOverlayBounds, options.CollisionThreshold))
                continue;
            
            // 3. 有効な位置を発見
            return new SelectionResult
            {
                Position = candidate.Position,
                Strategy = candidate.Strategy,
                CollisionAvoidanceApplied = false
            };
        }
        
        // 4. 基本戦略で配置できない場合は動的オフセット調整
        return ApplyDynamicOffsetAdjustment(candidates.First(), overlaySize, 
                                          monitorBounds, existingOverlayBounds, options);
    }
    
    /// <summary>
    /// 動的オフセット調整による位置最適化
    /// </summary>
    private static SelectionResult ApplyDynamicOffsetAdjustment(
        CandidatePosition baseCandidate, Size overlaySize, Rectangle monitorBounds,
        IReadOnlyList<Rectangle> existingOverlayBounds, OverlayPositioningOptions options)
    {
        var basePosition = baseCandidate.Position;
        var stepSize = options.DynamicOffsetStep;
        var maxSteps = options.MaxDynamicOffsetSteps;
        
        // X軸方向の調整を優先
        for (int step = 1; step <= maxSteps; step++)
        {
            var offsetX = step * stepSize;
            
            // 右方向への調整
            var rightPosition = new Point(basePosition.X + offsetX, basePosition.Y);
            if (IsPositionValid(rightPosition, overlaySize, monitorBounds, existingOverlayBounds, options))
            {
                return new SelectionResult
                {
                    Position = rightPosition,
                    Strategy = PositioningStrategy.DynamicOffset,
                    CollisionAvoidanceApplied = true
                };
            }
            
            // 左方向への調整
            var leftPosition = new Point(basePosition.X - offsetX, basePosition.Y);
            if (IsPositionValid(leftPosition, overlaySize, monitorBounds, existingOverlayBounds, options))
            {
                return new SelectionResult
                {
                    Position = leftPosition,
                    Strategy = PositioningStrategy.DynamicOffset,
                    CollisionAvoidanceApplied = true
                };
            }
        }
        
        // Y軸方向の調整
        for (int step = 1; step <= maxSteps; step++)
        {
            var offsetY = step * stepSize;
            
            // 下方向への調整
            var downPosition = new Point(basePosition.X, basePosition.Y + offsetY);
            if (IsPositionValid(downPosition, overlaySize, monitorBounds, existingOverlayBounds, options))
            {
                return new SelectionResult
                {
                    Position = downPosition,
                    Strategy = PositioningStrategy.DynamicOffset,
                    CollisionAvoidanceApplied = true
                };
            }
            
            // 上方向への調整
            var upPosition = new Point(basePosition.X, basePosition.Y - offsetY);
            if (IsPositionValid(upPosition, overlaySize, monitorBounds, existingOverlayBounds, options))
            {
                return new SelectionResult
                {
                    Position = upPosition,
                    Strategy = PositioningStrategy.DynamicOffset,
                    CollisionAvoidanceApplied = true
                };
            }
        }
        
        // 最終フォールバック: 座標クランプ
        var clampedX = Math.Max(monitorBounds.Left + options.MonitorBoundaryMargin,
                       Math.Min(monitorBounds.Right - overlaySize.Width - options.MonitorBoundaryMargin, basePosition.X));
        var clampedY = Math.Max(monitorBounds.Top + options.MonitorBoundaryMargin,
                       Math.Min(monitorBounds.Bottom - overlaySize.Height - options.MonitorBoundaryMargin, basePosition.Y));
        
        return new SelectionResult
        {
            Position = new Point(clampedX, clampedY),
            Strategy = PositioningStrategy.ForcedClamp,
            CollisionAvoidanceApplied = true
        };
    }
    
    /// <summary>
    /// 位置の有効性を検証
    /// </summary>
    private static bool IsPositionValid(Point position, Size overlaySize, Rectangle monitorBounds,
        IReadOnlyList<Rectangle> existingOverlayBounds, OverlayPositioningOptions options)
    {
        var candidateRect = new Rectangle(position.X, position.Y, overlaySize.Width, overlaySize.Height);
        
        // モニター境界チェック
        var adjustedMonitorBounds = new Rectangle(
            monitorBounds.X + options.MonitorBoundaryMargin,
            monitorBounds.Y + options.MonitorBoundaryMargin,
            monitorBounds.Width - (options.MonitorBoundaryMargin * 2),
            monitorBounds.Height - (options.MonitorBoundaryMargin * 2)
        );
        
        if (!adjustedMonitorBounds.Contains(candidateRect))
            return false;
        
        // 衝突検知チェック
        return !HasSignificantCollision(candidateRect, existingOverlayBounds, options.CollisionThreshold);
    }
    
    /// <summary>
    /// 有意な衝突があるかチェック
    /// </summary>
    private static bool HasSignificantCollision(Rectangle candidateRect, 
        IReadOnlyList<Rectangle> existingOverlayBounds, int threshold)
    {
        foreach (var existingBounds in existingOverlayBounds)
        {
            if (!candidateRect.IntersectsWith(existingBounds))
                continue;
            
            var intersection = Rectangle.Intersect(candidateRect, existingBounds);
            var overlapArea = intersection.Width * intersection.Height;
            
            if (overlapArea > threshold)
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 基本位置調整内部実装
    /// </summary>
    private static Point CalculateBasicPositionInternal(Rectangle textBounds, Size overlaySize, Rectangle screenBounds)
    {
        // 優先順位付きポジショニング戦略（既存ロジック）
        var positions = new[]
        {
            new { textBounds.X, Y = textBounds.Bottom + 5, Priority = 1 },
            new { textBounds.X, Y = textBounds.Y - overlaySize.Height - 5, Priority = 2 },
            new { X = textBounds.Right + 5, textBounds.Y, Priority = 3 },
            new { X = textBounds.X - overlaySize.Width - 5, textBounds.Y, Priority = 4 },
            new { X = textBounds.Right - overlaySize.Width, Y = textBounds.Bottom + 5, Priority = 5 },
            new { textBounds.X, Y = textBounds.Bottom + 5, Priority = 6 }
        };

        foreach (var pos in positions.OrderBy(p => p.Priority))
        {
            var candidateRect = new Rectangle(pos.X, pos.Y, overlaySize.Width, overlaySize.Height);
            
            if (screenBounds.Contains(candidateRect))
            {
                return new Point(pos.X, pos.Y);
            }
        }

        // 座標クランプ
        var clampedX = Math.Max(screenBounds.Left, 
                       Math.Min(screenBounds.Right - overlaySize.Width, textBounds.X));
        var clampedY = Math.Max(screenBounds.Top, 
                       Math.Min(screenBounds.Bottom - overlaySize.Height, textBounds.Bottom + 5));
                       
        return new Point(clampedX, clampedY);
    }
    
    /// <summary>
    /// 衝突回避付き位置調整内部実装
    /// </summary>
    private Point CalculatePositionWithCollisionAvoidanceInternal(
        Rectangle textBounds, Size overlaySize, Rectangle screenBounds, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        // 優先順位付きポジショニング戦略（衝突回避版）
        var positions = new[]
        {
            new { textBounds.X, Y = textBounds.Y - overlaySize.Height - 5, Priority = 1 },
            new { textBounds.X, Y = textBounds.Bottom + 5, Priority = 2 },
            new { X = textBounds.Right + 5, textBounds.Y, Priority = 3 },
            new { X = textBounds.X - overlaySize.Width - 5, textBounds.Y, Priority = 4 },
            new { X = textBounds.Right + 5, Y = textBounds.Y - overlaySize.Height - 5, Priority = 5 },
            new { X = textBounds.X - overlaySize.Width - 5, Y = textBounds.Y - overlaySize.Height - 5, Priority = 6 },
            new { X = textBounds.Right + 5, Y = textBounds.Bottom + 5, Priority = 7 },
            new { X = textBounds.X - overlaySize.Width - 5, Y = textBounds.Bottom + 5, Priority = 8 }
        };

        foreach (var pos in positions.OrderBy(p => p.Priority))
        {
            var candidateRect = new Rectangle(pos.X, pos.Y, overlaySize.Width, overlaySize.Height);
            
            if (!screenBounds.Contains(candidateRect))
                continue;

            bool hasCollision = false;
            foreach (var existingBounds in existingOverlayBounds)
            {
                if (candidateRect.IntersectsWith(existingBounds))
                {
                    hasCollision = true;
                    break;
                }
            }

            if (!hasCollision)
            {
                return new Point(pos.X, pos.Y);
            }
        }

        // 動的オフセット調整
        var basePosition = CalculateBasicPositionInternal(textBounds, overlaySize, screenBounds);
        return FindNonCollidingPositionInternal(basePosition, overlaySize, screenBounds, existingOverlayBounds);
    }
    
    /// <summary>
    /// 衝突回避位置検索内部実装
    /// </summary>
    private Point FindNonCollidingPositionInternal(Point basePosition, Size overlaySize, Rectangle screenBounds, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        const int stepSize = 10;
        const int maxSteps = 20;
        
        // X軸方向の調整
        for (int step = 1; step <= maxSteps; step++)
        {
            var offsetX = step * stepSize;
            
            var rightPosition = new Point(basePosition.X + offsetX, basePosition.Y);
            var rightRect = new Rectangle(rightPosition.X, rightPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(rightRect) && !HasCollisionWithExistingOverlays(rightRect, existingOverlayBounds))
            {
                return rightPosition;
            }
            
            var leftPosition = new Point(basePosition.X - offsetX, basePosition.Y);
            var leftRect = new Rectangle(leftPosition.X, leftPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(leftRect) && !HasCollisionWithExistingOverlays(leftRect, existingOverlayBounds))
            {
                return leftPosition;
            }
        }

        // Y軸方向の調整
        for (int step = 1; step <= maxSteps; step++)
        {
            var offsetY = step * stepSize;
            
            var downPosition = new Point(basePosition.X, basePosition.Y + offsetY);
            var downRect = new Rectangle(downPosition.X, downPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(downRect) && !HasCollisionWithExistingOverlays(downRect, existingOverlayBounds))
            {
                return downPosition;
            }
            
            var upPosition = new Point(basePosition.X, basePosition.Y - offsetY);
            var upRect = new Rectangle(upPosition.X, upPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(upRect) && !HasCollisionWithExistingOverlays(upRect, existingOverlayBounds))
            {
                return upPosition;
            }
        }

        return basePosition;
    }
    
    /// <summary>
    /// 既存オーバーレイとの衝突チェック
    /// </summary>
    private static bool HasCollisionWithExistingOverlays(Rectangle candidateRect, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        return existingOverlayBounds.Any(existingBounds => candidateRect.IntersectsWith(existingBounds));
    }
    
    #endregion
    
    #region Helper Classes
    
    /// <summary>
    /// 候補位置情報
    /// </summary>
    private class CandidatePosition
    {
        public Point Position { get; init; }
        public PositioningStrategy Strategy { get; init; }
        public int Priority { get; init; }
    }
    
    /// <summary>
    /// 選択結果情報
    /// </summary>
    private class SelectionResult
    {
        public Point Position { get; init; }
        public PositioningStrategy Strategy { get; init; }
        public bool CollisionAvoidanceApplied { get; init; }
    }
    
    #endregion
}
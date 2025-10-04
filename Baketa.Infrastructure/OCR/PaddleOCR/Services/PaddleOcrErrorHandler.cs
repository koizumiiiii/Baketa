using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// エラー診断、エラーメッセージ生成、解決策提案を担当するサービス
/// Phase 2.3: PaddleOcrEngineから抽出されたエラーハンドリング実装
/// </summary>
public sealed class PaddleOcrErrorHandler : IPaddleOcrErrorHandler
{
    private readonly ILogger<PaddleOcrErrorHandler>? _logger;
    private readonly IPaddleOcrPerformanceTracker _performanceTracker;

    public PaddleOcrErrorHandler(
        IPaddleOcrPerformanceTracker performanceTracker,
        ILogger<PaddleOcrErrorHandler>? logger = null)
    {
        _performanceTracker = performanceTracker ?? throw new ArgumentNullException(nameof(performanceTracker));
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrErrorHandler初期化完了");
    }

    /// <summary>
    /// PaddleOCRエラー情報を包括的に収集
    /// </summary>
    public string CollectErrorInfo(Mat mat, Exception ex)
    {
        var info = new List<string>();

        try
        {
            // エラーの基本情報
            info.Add($"Error: {ex.Message}");
            info.Add($"Exception Type: {ex.GetType().Name}");
            info.Add($"Consecutive Failures: {_performanceTracker.GetConsecutiveFailureCount()}");

            // 🔍 Mat状態情報（安全な取得）
            try
            {
                var width = mat.Width;
                var height = mat.Height;
                var channels = mat.Channels();
                var totalPixels = mat.Total();

                info.Add($"Mat Size: {width}x{height}");
                info.Add($"Mat Channels: {channels}");
                info.Add($"Mat Type: {mat.Type()}");
                info.Add($"Mat Empty: {mat.Empty()}");
                info.Add($"Mat Continuous: {mat.IsContinuous()}");
                info.Add($"Mat Total Pixels: {totalPixels}");

                // 🎯 奇数幅問題分析
                var widthOdd = width % 2 == 1;
                var heightOdd = height % 2 == 1;
                info.Add($"🔍 [ODD_WIDTH_ANALYSIS] Width Odd: {widthOdd} (Width: {width})");
                info.Add($"🔍 [ODD_HEIGHT_ANALYSIS] Height Odd: {heightOdd} (Height: {height})");

                if (widthOdd || heightOdd)
                {
                    info.Add($"⚠️ [EVIDENCE_CRITICAL] 奇数寸法検出 - NormalizeImageDimensions実行後も奇数！");
                    info.Add($"   📊 Expected: 正規化により偶数化されるべき");
                    info.Add($"   📊 Actual: Width={width}({(widthOdd ? "奇数" : "偶数")}), Height={height}({(heightOdd ? "奇数" : "偶数")})");
                }

                // 🎯 メモリアライメント分析
                var widthAlignment = width % 4;  // 4バイト境界
                var heightAlignment = height % 4;
                info.Add($"🔍 [MEMORY_ALIGNMENT] Width mod 4: {widthAlignment}, Height mod 4: {heightAlignment}");

                // 🎯 画像サイズカテゴリ分析
                var pixelCategory = totalPixels switch
                {
                    < 10000 => "極小(10K未満)",
                    < 100000 => "小(10K-100K)",
                    < 500000 => "中(100K-500K)",
                    < 1000000 => "大(500K-1M)",
                    _ => "極大(1M超)"
                };
                info.Add($"🔍 [SIZE_CATEGORY] Pixel Category: {pixelCategory} ({totalPixels:N0} pixels)");

                // 🎯 SIMD命令互換性分析
                var simdCompatible = (width % 16 == 0) && (height % 16 == 0); // AVX512対応
                var sse2Compatible = (width % 8 == 0) && (height % 8 == 0);   // SSE2対応
                info.Add($"🔍 [SIMD_COMPAT] AVX512 Compatible: {simdCompatible}, SSE2 Compatible: {sse2Compatible}");

                // 🎯 アスペクト比分析
                var aspectRatio = (double)width / height;
                var aspectCategory = aspectRatio switch
                {
                    < 0.5 => "縦長(1:2以上)",
                    < 0.8 => "縦寄り(1:1.25-1:2)",
                    < 1.25 => "正方形寄り(4:5-5:4)",
                    < 2.0 => "横寄り(5:4-2:1)",
                    _ => "横長(2:1以上)"
                };
                info.Add($"🔍 [ASPECT_RATIO] Ratio: {aspectRatio:F3} ({aspectCategory})");
            }
            catch
            {
                info.Add("Mat properties inaccessible (corrupted)");
            }

            // メモリ情報
            try
            {
                var memoryBefore = GC.GetTotalMemory(false);
                info.Add($"Memory Usage: {memoryBefore / (1024 * 1024):F1} MB");
            }
            catch
            {
                info.Add("Memory info unavailable");
            }

            // スタックトレース（最初の数行のみ）
            if (ex.StackTrace != null)
            {
                var stackLines = ex.StackTrace.Split('\n').Take(3);
                info.Add($"Stack Trace: {string.Join(" -> ", stackLines.Select(l => l.Trim()))}");
            }
        }
        catch (Exception infoEx)
        {
            info.Add($"Error collecting info: {infoEx.Message}");
        }

        return string.Join(", ", info);
    }

    /// <summary>
    /// PaddlePredictor実行エラーに基づく対処提案を生成
    /// </summary>
    public string GenerateErrorSuggestion(string errorMessage)
    {
        if (errorMessage.Contains("PaddlePredictor(Detector) run failed"))
        {
            return "検出器エラー: 画像の前処理またはサイズ調整が必要。画像品質またはPaddleOCRモデルの確認を推奨";
        }
        else if (errorMessage.Contains("PaddlePredictor(Recognizer) run failed"))
        {
            return "認識器エラー: テキスト認識段階での問題。検出されたテキスト領域のサイズまたは品質を確認";
        }
        else if (errorMessage.Contains("run failed"))
        {
            // 連続失敗回数に基づく提案
            var consecutiveFailures = _performanceTracker.GetConsecutiveFailureCount();
            if (consecutiveFailures >= 3)
            {
                return "連続失敗検出: OCRエンジンの再初期化またはシステム再起動を推奨";
            }
            else if (consecutiveFailures >= 2)
            {
                return "複数回失敗: 画像の前処理方法の変更または解像度調整を推奨";
            }
            else
            {
                return "初回エラー: 画像形式またはサイズの調整を試行";
            }
        }
        else
        {
            return "不明なPaddleOCRエラー: ログ確認とシステム状態の点検を推奨";
        }
    }

    /// <summary>
    /// エラーからのリカバリーを試行
    /// </summary>
    /// <param name="ex">発生した例外</param>
    /// <param name="retryAction">リトライするアクション</param>
    /// <returns>リカバリー成功の場合true</returns>
    public async Task<bool> TryRecoverFromError(Exception ex, Func<Task<bool>> retryAction)
    {
        ArgumentNullException.ThrowIfNull(retryAction);

        _logger?.LogWarning(ex, "🔄 エラーリカバリー試行開始: {ExceptionType}", ex.GetType().Name);

        // リカバリー可能なエラーかどうか判定
        if (!IsRecoverableError(ex))
        {
            _logger?.LogError("❌ リカバリー不可能なエラー: {ExceptionType}", ex.GetType().Name);
            return false;
        }

        // 短い遅延を挟んでリトライ
        await Task.Delay(100).ConfigureAwait(false);

        try
        {
            var result = await retryAction().ConfigureAwait(false);
            if (result)
            {
                _logger?.LogInformation("✅ エラーリカバリー成功");
                return true;
            }
            else
            {
                _logger?.LogWarning("⚠️ エラーリカバリー失敗（結果false）");
                return false;
            }
        }
        catch (Exception retryEx)
        {
            _logger?.LogError(retryEx, "❌ リトライ中に再度エラー発生: {ExceptionType}", retryEx.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// リカバリー可能なエラーかどうかを判定
    /// </summary>
    private static bool IsRecoverableError(Exception ex)
    {
        // 一時的なエラーはリカバリー可能
        return ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    }
}

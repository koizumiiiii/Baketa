using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// Issue #264: サーバーエラー検出ヘルパー
/// PythonServerManagerとSuryaServerManagerで共通のエラー検出ロジックを提供
/// </summary>
public static class ServerErrorDetector
{
    /// <summary>
    /// モジュール名抽出用正規表現（ドット付きモジュール名対応）
    /// </summary>
    private static readonly Regex ModuleNameRegex = new(
        @"No module named ['""]*([\w\.]+)['""]*",
        RegexOptions.Compiled);

    /// <summary>
    /// stderr出力からサーバーエラーを検出
    /// </summary>
    /// <param name="line">stderrの1行</param>
    /// <param name="source">エラーソース（TranslationServer/OcrServer）</param>
    /// <param name="context">コンテキスト情報（Port番号等）</param>
    /// <returns>検出されたエラーイベント、検出されなければnull</returns>
    public static ServerErrorEvent? Detect(string line, string source, string context)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // Intel MKLメモリエラー
        if (line.Contains("mkl_malloc", StringComparison.OrdinalIgnoreCase))
        {
            return ServerErrorEvent.CreateMemoryError(source, $"[{context}] {line}");
        }

        // Python標準メモリエラー
        if (line.Contains("MemoryError") || line.Contains("OutOfMemoryError"))
        {
            return ServerErrorEvent.CreateMemoryError(source, $"[{context}] {line}");
        }

        // 一般的なメモリ割り当て失敗
        if (line.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
        {
            return ServerErrorEvent.CreateMemoryError(source, $"[{context}] {line}");
        }

        // CUDAメモリエラー
        if (line.Contains("CUDA out of memory", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("hipErrorOutOfMemory", StringComparison.OrdinalIgnoreCase))
        {
            return ServerErrorEvent.CreateCudaMemoryError(source, $"[{context}] {line}");
        }

        // モジュール不足エラー
        if (line.Contains("ModuleNotFoundError"))
        {
            var moduleMatch = ModuleNameRegex.Match(line);
            var moduleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : "unknown";
            return ServerErrorEvent.CreateModuleNotFoundError(source, moduleName, $"[{context}] {line}");
        }

        return null;
    }

    /// <summary>
    /// 検出されたエラーを非同期でイベント発行
    /// </summary>
    /// <param name="errorEvent">発行するエラーイベント</param>
    /// <param name="eventAggregator">イベントアグリゲーター</param>
    /// <param name="logger">ロガー</param>
    /// <param name="logContext">ログ出力用コンテキスト</param>
    public static void PublishAsync(
        ServerErrorEvent? errorEvent,
        IEventAggregator eventAggregator,
        ILogger logger,
        string logContext)
    {
        if (errorEvent == null || eventAggregator == null)
        {
            return;
        }

        logger.LogWarning(
            "[Issue #264] サーバーエラー検出: {ErrorType} - {Context}",
            errorEvent.ErrorType, logContext);

        // 非同期でイベント発行（Fire-and-forget、ErrorDataReceivedはvoidなので）
        _ = Task.Run(async () =>
        {
            try
            {
                await eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Issue #264] ServerErrorEvent発行失敗. Context: {Context}", logContext);
            }
        });
    }

    /// <summary>
    /// エラー検出と発行を一括で実行
    /// </summary>
    public static void DetectAndPublish(
        string line,
        string source,
        string context,
        IEventAggregator? eventAggregator,
        ILogger logger)
    {
        if (eventAggregator == null)
        {
            return;
        }

        var errorEvent = Detect(line, source, context);
        PublishAsync(errorEvent, eventAggregator, logger, context);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Utilities;

/// <summary>
/// Baketa統一例外処理ハンドラー
/// フォールバック機能と自動復旧戦略を提供
/// </summary>
public static class BaketaExceptionHandler
{
    /// <summary>
    /// プライマリ処理→フォールバック→エラー処理の統一パターン
    /// </summary>
    /// <typeparam name="TResult">戻り値の型</typeparam>
    /// <param name="primary">プライマリ処理</param>
    /// <param name="fallback">フォールバック処理</param>
    /// <param name="onError">エラー時の処理（オプション）</param>
    /// <returns>プライマリまたはフォールバック結果</returns>
    public static async Task<TResult> HandleWithFallbackAsync<TResult>(
        Func<Task<TResult>> primary,
        Func<Task<TResult>> fallback,
        Func<Exception, Task>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        try
        {
            return await primary().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // エラー処理実行（ログ出力、通知等）
            if (onError != null)
            {
                await onError(ex).ConfigureAwait(false);
            }

            // フォールバック処理実行
            return await fallback().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 複数フォールバック戦略での例外処理
    /// </summary>
    /// <typeparam name="TResult">戻り値の型</typeparam>
    /// <param name="strategies">実行戦略のリスト（順番に試行）</param>
    /// <param name="onError">エラー時の処理（オプション）</param>
    /// <returns>最初に成功した戦略の結果</returns>
    public static async Task<TResult> HandleWithMultipleFallbacksAsync<TResult>(
        IReadOnlyList<Func<Task<TResult>>> strategies,
        Func<Exception, string, Task>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        
        if (strategies.Count == 0)
            throw new ArgumentException("At least one strategy must be provided", nameof(strategies));

        Exception? lastException = null;

        for (int i = 0; i < strategies.Count; i++)
        {
            try
            {
                return await strategies[i]().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                // 最後の戦略以外での失敗はログ出力のみ
                if (i < strategies.Count - 1 && onError != null)
                {
                    await onError(ex, $"Strategy {i + 1} failed, trying next...").ConfigureAwait(false);
                }
            }
        }

        // 全戦略が失敗した場合
        if (lastException != null)
            throw new AggregateException("All fallback strategies failed", lastException);

        throw new InvalidOperationException("Unexpected error: no strategy succeeded and no exception was thrown");
    }

    /// <summary>
    /// 同期処理版フォールバック処理
    /// </summary>
    /// <typeparam name="TResult">戻り値の型</typeparam>
    /// <param name="primary">プライマリ処理</param>
    /// <param name="fallback">フォールバック処理</param>
    /// <param name="onError">エラー時の処理（オプション）</param>
    /// <returns>プライマリまたはフォールバック結果</returns>
    public static TResult HandleWithFallback<TResult>(
        Func<TResult> primary,
        Func<TResult> fallback,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        try
        {
            return primary();
        }
        catch (Exception ex)
        {
            // エラー処理実行
            onError?.Invoke(ex);

            // フォールバック処理実行
            return fallback();
        }
    }

    /// <summary>
    /// ユーザーフレンドリーなエラーメッセージ生成
    /// </summary>
    /// <param name="ex">例外オブジェクト</param>
    /// <param name="context">エラー発生コンテキスト</param>
    /// <returns>ユーザーフレンドリーなエラーメッセージ</returns>
    public static string GetUserFriendlyErrorMessage(Exception ex, string context)
    {
        ArgumentNullException.ThrowIfNull(ex);
        
        return ex switch
        {
            TimeoutException => $"{context}中にタイムアウトが発生しました。処理に時間がかかっています。",
            UnauthorizedAccessException => $"{context}中にアクセス権限エラーが発生しました。",
            System.Net.NetworkInformation.NetworkInformationException => $"{context}中にネットワーク接続エラーが発生しました。",
            System.IO.IOException => $"{context}中にファイル操作エラーが発生しました。",
            ArgumentException => $"{context}中に不正な設定値が検出されました。設定を確認してください。",
            InvalidOperationException => $"{context}中に操作エラーが発生しました。アプリケーションを再起動してください。",
            _ => $"{context}中に予期しないエラーが発生しました。"
        };
    }

    /// <summary>
    /// 翻訳エンジン切り替え戦略
    /// </summary>
    /// <param name="engines">利用可能な翻訳エンジンのリスト</param>
    /// <param name="text">翻訳対象テキスト</param>
    /// <param name="sourceLanguage">ソース言語</param>
    /// <param name="targetLanguage">ターゲット言語</param>
    /// <param name="logger">ロガー（オプション）</param>
    /// <returns>翻訳成功した結果</returns>
    public static async Task<string> TryTranslationEnginesAsync(
        IReadOnlyList<Func<string, string, string, Task<string>>> engines,
        string text,
        string sourceLanguage,
        string targetLanguage,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(text);

        if (engines.Count == 0)
            return text; // エンジンが無い場合は元テキストをそのまま返す

        var strategies = engines.Select<Func<string, string, string, Task<string>>, Func<Task<string>>>(
            engine => () => engine(text, sourceLanguage, targetLanguage)
        ).ToList();

        return await HandleWithMultipleFallbacksAsync(
            strategies,
            onError: async (ex, message) =>
            {
                logger?.LogWarning(ex, "Translation engine failed: {Message}", message);
                await Task.CompletedTask;
            }
        ).ConfigureAwait(false);
    }
}
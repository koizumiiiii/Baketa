using System.IO;
using System.Text;

namespace Baketa.Core.Utilities;

/// <summary>
/// ファイルアクセス競合を完全に回避する安全なファイル書き込みユーティリティ
/// </summary>
public static class SafeFileWriter
{
    private static readonly object _fileLock = new();
    private static readonly Dictionary<string, object> _fileLocks = new();

    /// <summary>
    /// ファイルアクセス競合を回避して安全にログ書き込み
    /// </summary>
    public static void AppendTextSafely(string filePath, string content, Encoding? encoding = null)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(content))
            return;

        encoding ??= Encoding.UTF8;

        // ファイルパス別の専用ロック取得
        object fileLock = null!;
        lock (_fileLock)
        {
#pragma warning disable CS8600 // TryGetValue失敗時にnullが設定されるが、次の行で必ず非null値を設定
            if (!_fileLocks.TryGetValue(filePath, out fileLock))
#pragma warning restore CS8600
            {
                fileLock = new object();
                _fileLocks[filePath] = fileLock;
            }
        }

        // ファイル専用ロックで安全に書き込み
        lock (fileLock)
        {
            var maxRetries = 3;
            var retryDelay = 50; // 50ms

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // ディレクトリ存在確認
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 安全な書き込み実行
                    File.AppendAllText(filePath, content, encoding);
                    return; // 成功時は即座にreturn
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    // リトライ可能な回数内でのIOException
                    Thread.Sleep(retryDelay * attempt); // 指数バックオフ
                }
                catch (UnauthorizedAccessException)
                {
                    // アクセス権限エラー - リトライしない
                    Console.WriteLine($"⚠️ [FILE_ACCESS] ファイルアクセス権限エラー: {filePath}");
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    // ディレクトリ不存在エラー - リトライしない
                    Console.WriteLine($"⚠️ [FILE_ACCESS] ディレクトリ不存在エラー: {filePath}");
                    return;
                }
                catch (Exception ex)
                {
                    // その他の予期しないエラー
                    Console.WriteLine($"⚠️ [FILE_ACCESS] 予期しないファイル書き込みエラー: {ex.Message}");
                    return;
                }
            }

            // 最大リトライ回数に達した場合
            Console.WriteLine($"⚠️ [FILE_ACCESS] ファイル書き込み失敗: {filePath} (最大リトライ回数到達)");
        }
    }

    /// <summary>
    /// デバッグログ専用の安全書き込みメソッド
    /// </summary>
    public static void WriteDebugLog(string message)
    {
        // 診断レポートシステム実装完了により、debug_app_logs.txtへの出力を無効化
        // 構造化された診断データはPipelineDiagnosticEventとDiagnosticCollectionServiceで管理
        return;
    }

    /// <summary>
    /// デバッグログ専用の安全書き込みメソッド（パラメータ付き）
    /// </summary>
    public static void WriteDebugLog(string format, params object[] args)
    {
        // 診断レポートシステム実装完了により、debug_app_logs.txtへの出力を無効化
        // 構造化された診断データはPipelineDiagnosticEventとDiagnosticCollectionServiceで管理
        return;
    }
}

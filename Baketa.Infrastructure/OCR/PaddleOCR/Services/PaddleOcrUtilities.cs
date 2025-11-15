using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// ユーティリティメソッド、テスト環境判定、ログ出力を担当するサービス
/// Phase 2.2: PaddleOcrEngineから抽出された121行のユーティリティ実装
/// </summary>
public sealed class PaddleOcrUtilities : IPaddleOcrUtilities
{
    /// <summary>
    /// テスト環境判定
    /// </summary>
    public bool IsTestEnvironment()
    {
        try
        {
            // より厳格なテスト環境検出
            var processName = Process.GetCurrentProcess().ProcessName;

            // 実行中のプロセス名による検出
            var isTestProcess = processName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
                               processName.Contains("vstest", StringComparison.OrdinalIgnoreCase);

            // スタックトレースによるテスト検出（より確実）
            var stackTrace = Environment.StackTrace;
            var isTestFromStack = stackTrace.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                                 stackTrace.Contains(".Tests.", StringComparison.OrdinalIgnoreCase) ||
                                 stackTrace.Contains("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                                 stackTrace.Contains("TestMethodInvoker", StringComparison.OrdinalIgnoreCase);

            // 環境変数による検出
            var isTestEnvironmentVar = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")) ||
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) || // Azure DevOps
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) || // GitHub Actions
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_BUILDID"));

            // コマンドライン引数による検出
            var isTestCommand = Environment.CommandLine.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                               Environment.CommandLine.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                               Environment.CommandLine.Contains("vstest", StringComparison.OrdinalIgnoreCase);

            // アセンブリ名による検出
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
            var isTestAssembly = entryAssembly?.FullName?.Contains("test", StringComparison.OrdinalIgnoreCase) == true ||
                                entryAssembly?.FullName?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true;

            var isTest = isTestProcess || isTestFromStack || isTestEnvironmentVar || isTestCommand || isTestAssembly;

            // 詳細な判定結果（静的メソッドのためコメントのみ）
            // Debug: Process={isTestProcess}, Stack={isTestFromStack}, Env={isTestEnvironmentVar}, Command={isTestCommand}, Assembly={isTestAssembly} → Result={isTest}

            return isTest;
        }
        catch (SecurityException ex)
        {
            // セキュリティ上の理由で情報取得できない場合は本番環境と判定（テスト環境誤判定防止）
            // Log: IsTestEnvironment: SecurityException発生 - 本番環境として継続: {ex.Message}
            Debug.WriteLine($"IsTestEnvironment: SecurityException発生 - 本番環境として継続: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // 操作エラーが発生した場合は本番環境と判定（テスト環境誤判定防止）
            // Log: IsTestEnvironment: InvalidOperationException発生 - 本番環境として継続: {ex.Message}
            Debug.WriteLine($"IsTestEnvironment: InvalidOperationException発生 - 本番環境として継続: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            // アクセス拒否の場合は本番環境と判定（テスト環境誤判定防止）
            // Log: IsTestEnvironment: UnauthorizedAccessException発生 - 本番環境として継続: {ex.Message}
            Debug.WriteLine($"IsTestEnvironment: UnauthorizedAccessException発生 - 本番環境として継続: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ダミーMat作成
    /// </summary>
    public Mat CreateDummyMat()
    {
        try
        {
            // 最小限のMatを作成
            return new Mat(1, 1, MatType.CV_8UC3);
        }
        catch (TypeInitializationException ex)
        {
            // OpenCvSharp初期化エラー
            throw new OcrException($"テスト環境でOpenCvSharpライブラリ初期化エラー: {ex.Message}", ex);
        }
        catch (DllNotFoundException ex)
        {
            // ネイティブDLLが見つからない
            throw new OcrException($"テスト環境でOpenCvSharpライブラリが利用できません: {ex.Message}", ex);
        }
        catch (BadImageFormatException ex)
        {
            // プラットフォームミスマッチ
            throw new OcrException($"テスト環境でOpenCvSharpライブラリのプラットフォームエラー: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            // Mat操作エラー
            throw new OcrException($"テスト環境でOpenCvSharpMat操作エラー: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// デバッグログパス取得
    /// </summary>
    public string GetDebugLogPath()
    {
        var debugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BaketaDebugLogs",
            "debug_app_logs.txt"
        );

        try
        {
            var directory = Path.GetDirectoryName(debugLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch
        {
            // フォールバック: Tempディレクトリを使用
            debugLogPath = Path.Combine(Path.GetTempPath(), "BaketaDebugLogs", "debug_app_logs.txt");
            var directory = Path.GetDirectoryName(debugLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        return debugLogPath;
    }

    /// <summary>
    /// 安全なデバッグログ書き込み
    /// </summary>
    public void SafeWriteDebugLog(string message)
    {
        try
        {
            var debugLogPath = GetDebugLogPath();
            File.AppendAllText(debugLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"デバッグログ書き込みエラー: {ex.Message}");
        }
    }
}

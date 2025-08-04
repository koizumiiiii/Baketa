using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// HuggingFace Transformers基盤OPUS-MT翻訳エンジン
/// Python統合により語彙サイズ不整合問題を完全解決
/// </summary>
public class TransformersOpusMtEngine : TranslationEngineBase
{
    private readonly ILogger<TransformersOpusMtEngine> _logger;
    private readonly string _pythonPath;
    private readonly string _scriptPath;
    private bool _isInitialized;
    private bool _disposed;

    /// <inheritdoc/>
    public override string Name => "OPUS-MT Transformers";

    /// <inheritdoc/>
    public override string Description => "HuggingFace Transformers基盤の高品質OPUS-MT翻訳エンジン";

    /// <inheritdoc/>
    public override bool RequiresNetwork => false;

    public TransformersOpusMtEngine(ILogger<TransformersOpusMtEngine> logger) : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        Console.WriteLine("🔧 [DEBUG] TransformersOpusMtEngineのコンストラクタが呼び出されました");
        _logger.LogInformation("TransformersOpusMtEngineが作成されました");
        
        // Python実行環境設定
        _pythonPath = @"C:\Users\suke0\.pyenv\pyenv-win\versions\3.10.9\python.exe";
        
        // スクリプトパス設定
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        _scriptPath = Path.Combine(projectRoot, "scripts", "opus_mt_service.py");
        
        Console.WriteLine($"🔧 [DEBUG] TransformersOpusMtEngine設定完了 - Python: {_pythonPath}, Script: {_scriptPath}");
        
        // バックグラウンドで初期化を開始（ブロックしない）
        _ = Task.Run(async () =>
        {
            try
            {
                await InitializeAsync().ConfigureAwait(false);
                Console.WriteLine("🔧 [DEBUG] TransformersOpusMtEngineのバックグラウンド初期化完了");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔧 [DEBUG] TransformersOpusMtEngineのバックグラウンド初期化失敗: {ex.Message}");
            }
        });
    }

    /// <inheritdoc/>
    protected override async Task<bool> InitializeInternalAsync()
    {
        try
        {
            _logger.LogInformation("OPUS-MT Transformers翻訳エンジンの初期化開始");
            
            // Python環境とスクリプトファイル確認
            if (!File.Exists(_pythonPath))
            {
                _logger.LogError("Python実行ファイルが見つかりません: {PythonPath}", _pythonPath);
                return false;
            }
            _logger.LogInformation("Python実行ファイル確認完了: {PythonPath}", _pythonPath);

            if (!File.Exists(_scriptPath))
            {
                _logger.LogError("翻訳スクリプトが見つかりません: {ScriptPath}", _scriptPath);
                return false;
            }
            _logger.LogInformation("翻訳スクリプト確認完了: {ScriptPath}", _scriptPath);

            // ファイルの存在確認のみで初期化完了とする（モデルロードは初回翻訳実行時）
            _logger.LogInformation("ファイル確認完了 - 初期化成功（モデルロードは初回翻訳時に実行）");
            _isInitialized = true;
            IsInitialized = true; // 基底クラスのプロパティも更新
            Console.WriteLine("🔧 [DEBUG] TransformersOpusMtEngine初期化完了（軽量初期化）");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初期化中にエラーが発生しました");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override async Task<TranslationResponse> TranslateInternalAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🚀 [DEBUG] TransformersOpusMtEngine.TranslateInternalAsync 呼び出し - テキスト: '{request.SourceText}'");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [DEBUG] TransformersOpusMtEngine.TranslateInternalAsync 呼び出し - テキスト: '{request.SourceText}'{Environment.NewLine}");
        Console.WriteLine($"🔧 [DEBUG] 初回翻訳実行 - HuggingFace Transformersモデルロード開始（時間がかかります）");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DEBUG] 初回翻訳実行 - HuggingFace Transformersモデルロード開始（時間がかかります）{Environment.NewLine}");
        _logger.LogInformation("TransformersOpusMtEngineで翻訳開始: '{Text}' - モデルロードが必要な場合は数分かかる可能性があります", request.SourceText);
        
        if (!request.SourceLanguage.Equals(Language.Japanese) || 
            !request.TargetLanguage.Equals(Language.English))
        {
            throw new ArgumentException("このエンジンは日英翻訳のみサポートしています");
        }

        var pythonResult = await TranslatePythonAsync(request.SourceText).ConfigureAwait(false);

        Console.WriteLine($"🔧 [TRANSLATE_DEBUG] Python結果取得 - Result: {pythonResult != null}, Success: {pythonResult?.Success}, Translation: '{pythonResult?.Translation}'");

        if (pythonResult?.Success == true)
        {
            var response = new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = pythonResult.Translation,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.95f, // HuggingFace Transformersは高品質
                EngineName = Name,
                IsSuccess = true
            };
            
            Console.WriteLine($"🔧 [TRANSLATE_DEBUG] 成功レスポンス作成 - TranslatedText: '{response.TranslatedText}'");
            _logger.LogInformation("翻訳成功 - RequestId: {RequestId}, TranslatedText: '{TranslatedText}'", response.RequestId, response.TranslatedText);
            return response;
        }

        var errorResponse = new TranslationResponse
        {
            RequestId = request.RequestId,
            TranslatedText = pythonResult?.Error ?? "翻訳に失敗しました",
            SourceText = request.SourceText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            ConfidenceScore = 0.0f,
            EngineName = Name,
            IsSuccess = false
        };
        
        Console.WriteLine($"🔧 [TRANSLATE_DEBUG] エラーレスポンス作成 - Error: '{errorResponse.TranslatedText}'");
        _logger.LogError("翻訳失敗 - RequestId: {RequestId}, Error: '{Error}'", errorResponse.RequestId, errorResponse.TranslatedText);
        return errorResponse;
    }

    private async Task<PythonTranslationResult?> TranslatePythonAsync(string text)
    {
        Console.WriteLine($"🐍 [PYTHON_DEBUG] Python翻訳開始: '{text}' - HuggingFaceモデルロード中...");
        _logger.LogInformation("Python翻訳開始: '{Text}' - モデルロードのため初回は数分かかる可能性があります", text);
        
        // 一時ファイルを使って確実にUTF-8で渡す
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, text, System.Text.Encoding.UTF8).ConfigureAwait(false);
            _logger.LogInformation("一時ファイル作成完了: {TempFile}", tempFile);
            
            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{_scriptPath}\" \"@{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            _logger.LogInformation("Pythonプロセス開始: {FileName} {Arguments}", processInfo.FileName, processInfo.Arguments);

            using var process = new Process { StartInfo = processInfo };
            Console.WriteLine($"🐍 [PYTHON_DEBUG] Process.Start()直前");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Process.Start()直前{Environment.NewLine}");
            
            process.Start();
            
            Console.WriteLine($"🐍 [PYTHON_DEBUG] Process.Start()完了 - PID: {process.Id}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Process.Start()完了 - PID: {process.Id}{Environment.NewLine}");

            // タイムアウト制御 (初回モデルロードのため300秒=5分でタイムアウト)
            Console.WriteLine($"🐍 [PYTHON_DEBUG] 非同期タスク作成開始");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] 非同期タスク作成開始{Environment.NewLine}");
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var processTask = process.WaitForExitAsync();
            
            Console.WriteLine($"🐍 [PYTHON_DEBUG] 非同期タスク作成完了");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] 非同期タスク作成完了{Environment.NewLine}");

            var timeout = TimeSpan.FromSeconds(300); // 5分に延長
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                Console.WriteLine($"🔄 [PYTHON_DEBUG] Python処理実行中... (最大5分待機)");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [PYTHON_DEBUG] Python処理実行中... (最大5分待機){Environment.NewLine}");
                
                var startTime = DateTime.Now;
                
                // 10秒ごとに進行状況を表示
                var progressTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(10000, cts.Token).ConfigureAwait(false);
                        var elapsed = DateTime.Now - startTime;
                        Console.WriteLine($"⏱️ [PROGRESS] 処理継続中... 経過時間: {elapsed.TotalSeconds:F0}秒");
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏱️ [PROGRESS] 処理継続中... 経過時間: {elapsed.TotalSeconds:F0}秒{Environment.NewLine}");
                        if (elapsed.TotalSeconds > 300) break;
                    }
                }, cts.Token);
                
                Console.WriteLine($"🐍 [PYTHON_DEBUG] processTask.WaitAsync()呼び出し直前");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] processTask.WaitAsync()呼び出し直前{Environment.NewLine}");
                
                await processTask.WaitAsync(cts.Token).ConfigureAwait(false);
                
                Console.WriteLine($"🐍 [PYTHON_DEBUG] processTask.WaitAsync()完了");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] processTask.WaitAsync()完了{Environment.NewLine}");
                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                Console.WriteLine($"🐍 [PYTHON_DEBUG] Pythonプロセス終了 - ExitCode: {process.ExitCode}");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Output長さ: {output?.Length}文字");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Output (RAW): '{output}'");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Output (HEX最初の20バイト): '{BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(output ?? "").Take(20).ToArray())}'");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Error: '{error}'");
                
                // ExitCode 143 (SIGTERM) の場合はタイムアウトエラーとして扱う
                if (process.ExitCode == 143)
                {
                    _logger.LogError("Pythonプロセスがタイムアウトにより強制終了されました (SIGTERM)");
                    return new PythonTranslationResult 
                    { 
                        Success = false, 
                        Error = "翻訳プロセスがタイムアウトしました。初回実行時はモデルダウンロードのため数分かかります。", 
                        Source = text 
                    };
                }
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Pythonプロセス終了 - ExitCode: {process.ExitCode}{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Output: '{output}'{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Error: '{error}'{Environment.NewLine}");
                _logger.LogInformation("Pythonプロセス終了 - ExitCode: {ExitCode}, Output: {Output}, Error: {Error}", 
                    process.ExitCode, output, error);

                if (process.ExitCode != 0)
                {
                    _logger.LogError("Python翻訳プロセスがエラーで終了しました: {Error}", error);
                    return null;
                }

                Console.WriteLine($"🔍 [TRANSLATE_DEBUG] ParseResult呼び出し開始");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [TRANSLATE_DEBUG] ParseResult呼び出し開始{Environment.NewLine}");
                var result = ParseResult(output);
                Console.WriteLine($"🔍 [TRANSLATE_DEBUG] ParseResult呼び出し完了 - Result: {result?.Success}, Translation: '{result?.Translation}'");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [TRANSLATE_DEBUG] ParseResult呼び出し完了 - Result: {result?.Success}, Translation: '{result?.Translation}'{Environment.NewLine}");
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Python翻訳プロセスがタイムアウトしました ({Timeout}秒)", timeout.TotalSeconds);
                process.Kill();
                return null;
            }

        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
                _logger.LogInformation("一時ファイル削除完了: {TempFile}", tempFile);
            }
        }
    }

    private PythonTranslationResult? ParseResult(string output)
    {
        try
        {
            Console.WriteLine($"🔧 [JSON_DEBUG] ParseResult開始");
            _logger.LogInformation("Python出力をJSON解析中: '{Output}' (長さ: {Length})", output, output?.Length);
            
            // 出力がnullまたは空の場合
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine($"💥 [JSON_DEBUG] Python出力がnullまたは空です");
                return null;
            }
            
            // JSON修復とクリーンアップ
            string jsonStr = output.Trim();
            
            // BOMを除去
            if (jsonStr.StartsWith("\uFEFF"))
            {
                jsonStr = jsonStr.Substring(1);
                Console.WriteLine($"🔧 [JSON_DEBUG] BOMを除去しました");
            }
            
            // 改行文字を削除
            jsonStr = jsonStr.Replace("\r", "").Replace("\n", "");
            
            // JSON形式の自動修復
            // {が欠落している場合の修復
            if (!jsonStr.StartsWith("{") && jsonStr.Contains("\"success\""))
            {
                jsonStr = "{" + jsonStr;
                Console.WriteLine($"🔧 [JSON_DEBUG] 先頭に {{ を追加して修復");
            }
            
            // }が欠落している場合の修復
            if (!jsonStr.EndsWith("}") && jsonStr.StartsWith("{"))
            {
                // 最後の}を探す
                int lastBrace = jsonStr.LastIndexOf('}');
                if (lastBrace == -1)
                {
                    jsonStr = jsonStr + "}";
                    Console.WriteLine($"🔧 [JSON_DEBUG] 末尾に }} を追加して修復");
                }
                else
                {
                    // 最後の}以降の文字を削除
                    jsonStr = jsonStr.Substring(0, lastBrace + 1);
                }
            }
            
            Console.WriteLine($"🔧 [JSON_DEBUG] 修復後のJSON: '{jsonStr}'");
            
            // JSON解析
            Console.WriteLine($"🔧 [JSON_DEBUG] JsonSerializer.Deserialize開始");
            var result = JsonSerializer.Deserialize<PythonTranslationResult>(jsonStr);
            
            Console.WriteLine($"🔧 [JSON_DEBUG] 解析結果 - Success: {result?.Success}, Translation: '{result?.Translation}', Source: '{result?.Source}'");
            _logger.LogInformation("JSON解析成功 - Success: {Success}, Translation: '{Translation}', Source: '{Source}'", 
                result?.Success, result?.Translation, result?.Source);
            return result;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"💥 [JSON_DEBUG] JSON解析失敗: {ex.Message}");
            Console.WriteLine($"💥 [JSON_DEBUG] 問題のある出力: '{output}'");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] JSON解析失敗: {ex.Message}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] 問題のある出力: '{output}'{Environment.NewLine}");
            _logger.LogError(ex, "Python出力のJSONパースに失敗しました: {Output}", output);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [JSON_DEBUG] 予期しないエラー: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"💥 [JSON_DEBUG] スタックトレース: {ex.StackTrace}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] 予期しないエラー: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] スタックトレース: {ex.StackTrace}{Environment.NewLine}");
            _logger.LogError(ex, "ParseResult処理中に予期しないエラーが発生しました: {Output}", output);
            return null;
        }
    }

    private static string FindProjectRoot(string currentDir)
    {
        var dir = new DirectoryInfo(currentDir);
        
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Baketa.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        
        throw new DirectoryNotFoundException("Baketaプロジェクトルートが見つかりません");
    }

    /// <inheritdoc/>
    public override async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        return new[]
        {
            new LanguagePair { SourceLanguage = Language.Japanese, TargetLanguage = Language.English }
        };
    }

    /// <inheritdoc/>
    public override async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        return languagePair.SourceLanguage.Equals(Language.Japanese) && 
               languagePair.TargetLanguage.Equals(Language.English);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _logger.LogInformation("OPUS-MT Transformers翻訳エンジンが破棄されました");
        }
        base.Dispose(disposing);
    }

    private class PythonTranslationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("translation")]
        public string Translation { get; set; } = string.Empty;
        
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
        
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }
}
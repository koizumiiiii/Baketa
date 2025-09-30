using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// stdin/stdout 経由でPython翻訳サーバーと通信するクライアント
/// UltraPhase 14.25: ハイブリッド通信アーキテクチャ実装
///
/// 設計原則:
/// - Strategy パターン: ITranslationClient 実装
/// - 単一責務: stdin/stdout通信のみ担当
/// - スレッドセーフ: SemaphoreSlim による排他制御
/// - 堅牢性: JSON/エラー解析の厳密な区別
/// </summary>
public sealed class StdinStdoutTranslationClient : ITranslationClient, IDisposable
{
    private readonly IPythonServerManager _serverManager;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _stdinLock = new(1, 1); // 単一プロセスのstdin排他制御
    private readonly string _languagePair; // サーバーインスタンス特定用
    private bool _disposed;

    /// <inheritdoc/>
    public string CommunicationMode => "StdinStdout";

    public StdinStdoutTranslationClient(
        IPythonServerManager serverManager,
        string languagePair,
        ILogger logger)
    {
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _languagePair = languagePair ?? throw new ArgumentNullException(nameof(languagePair));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 🔥 ULTRA_DEBUG: StdinStdoutTranslationClient 確実に作成されたことを確認
        Console.WriteLine($"🔥 [ULTRA_DEBUG] StdinStdoutTranslationClient作成 - 言語ペア: '{_languagePair}'");
        _logger.LogInformation("🔥 [ULTRA_DEBUG] StdinStdoutTranslationClient作成 - 言語ペア: '{LanguagePair}'", _languagePair);
    }

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🔄 [StdinStdout] 翻訳リクエスト開始: {Text}", request.SourceText);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // サーバーインスタンス取得（存在しない場合は起動）
            var serverInfo = await _serverManager.GetServerAsync(_languagePair).ConfigureAwait(false);
            if (serverInfo == null)
            {
                serverInfo = await _serverManager.StartServerAsync(_languagePair).ConfigureAwait(false);
            }

            // PythonServerInstanceにキャストしてProcessを取得
            if (serverInfo is not PythonServerInstance instance || instance.Process == null || instance.Process.HasExited)
            {
                throw new TranslationException(
                    TranslationErrorType.ServiceUnavailable,
                    "Python翻訳サーバープロセスが利用できません");
            }

            var process = instance.Process;

            // stdin排他制御（単一プロセスのstdinは並行書き込み不可）
            await _stdinLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 翻訳コマンド作成
                var command = new
                {
                    command = "translate",
                    text = request.SourceText,
                    source_lang = request.SourceLanguage.Code,
                    target_lang = request.TargetLanguage.Code
                };

                var commandJson = JsonSerializer.Serialize(command);
                _logger.LogDebug("📤 [StdinStdout] コマンド送信: {Command}", commandJson);

                // stdin書き込み
                await process.StandardInput.WriteLineAsync(commandJson).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);

                // stdout読み取り（タイムアウト付き）
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 翻訳タイムアウト30秒

                var responseLine = await process.StandardOutput.ReadLineAsync()
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);

                _logger.LogDebug("📥 [StdinStdout] レスポンス受信: {Response}", responseLine);

                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    throw new TranslationException(
                        TranslationErrorType.ServiceUnavailable,
                        "Python翻訳サーバーからの応答が空です");
                }

                // JSON vs エラーメッセージ解析
                var translationResponse = ParseResponse(responseLine, request, stopwatch.ElapsedMilliseconds);

                return translationResponse;
            }
            finally
            {
                _stdinLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⏰ [StdinStdout] 翻訳リクエストがキャンセルされました");
            throw;
        }
        catch (TranslationException)
        {
            throw; // TranslationException はそのまま再スロー
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [StdinStdout] 翻訳リクエストエラー: {Message}", ex.Message);
            throw new TranslationException(
                TranslationErrorType.UnexpectedError,
                $"stdin/stdout通信エラー: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 🔥 ULTRA_DEBUG: IsReadyAsync実行開始を確実に記録
            Console.WriteLine($"🔥 [ULTRA_DEBUG] IsReadyAsync開始 - 言語ペア: '{_languagePair}'");
            _logger.LogInformation("🔥 [ULTRA_DEBUG] IsReadyAsync開始 - 言語ペア: '{LanguagePair}'", _languagePair);

            _logger.LogDebug("🔍 [IsReady] 言語ペアキーでサーバー検索: '{LanguagePair}'", _languagePair);
            var serverInfo = await _serverManager.GetServerAsync(_languagePair).ConfigureAwait(false);
            if (serverInfo == null)
            {
                _logger.LogWarning("⚠️ [IsReady] サーバーが見つからない: '{LanguagePair}'", _languagePair);
                return false;
            }

            if (serverInfo is not PythonServerInstance instance || instance.Process == null || instance.Process.HasExited)
            {
                return false;
            }

            // is_readyコマンドで確認
            await _stdinLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var command = new { command = "is_ready" };
                var commandJson = JsonSerializer.Serialize(command);

                await instance.Process.StandardInput.WriteLineAsync(commandJson).ConfigureAwait(false);
                await instance.Process.StandardInput.FlushAsync().ConfigureAwait(false);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var responseLine = await instance.Process.StandardOutput.ReadLineAsync()
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    return false;
                }

                using var jsonDoc = JsonDocument.Parse(responseLine);
                var root = jsonDoc.RootElement;

                return root.TryGetProperty("success", out var success) && success.GetBoolean() &&
                       root.TryGetProperty("ready", out var ready) && ready.GetBoolean();
            }
            finally
            {
                _stdinLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "❌ [StdinStdout] IsReady確認失敗: {Message}", ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        // IsReadyAsync と同じロジック
        return IsReadyAsync(cancellationToken);
    }

    /// <summary>
    /// Python応答をパース（JSON vs エラートレースバック区別）
    /// </summary>
    private TranslationResponse ParseResponse(
        string responseLine,
        TranslationRequest request,
        long elapsedMs)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(responseLine);
            var root = jsonDoc.RootElement;

            // success フィールド確認
            if (!root.TryGetProperty("success", out var successProp))
            {
                throw new TranslationException(
                    TranslationErrorType.InvalidResponse,
                    $"Python応答にsuccessフィールドがありません: {responseLine}");
            }

            bool success = successProp.GetBoolean();

            if (success)
            {
                // 成功レスポンス
                if (!root.TryGetProperty("translation", out var translationProp))
                {
                    throw new TranslationException(
                        TranslationErrorType.InvalidResponse,
                        "翻訳結果が含まれていません");
                }

                var translation = translationProp.GetString() ?? string.Empty;
                var confidence = root.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetDouble()
                    : 0.0;

                return TranslationResponse.CreateSuccessWithConfidence(
                    request,
                    translation,
                    "StdinStdout",
                    elapsedMs,
                    (float)confidence);
            }
            else
            {
                // エラーレスポンス
                var errorMessage = root.TryGetProperty("error", out var errorProp)
                    ? errorProp.GetString() ?? "不明なエラー"
                    : "不明なエラー";

                _logger.LogWarning("⚠️ [StdinStdout] Python側エラー: {Error}", errorMessage);

                var error = TranslationError.Create(
                    "TranslationFailed",
                    errorMessage,
                    true,
                    TranslationErrorType.ProcessingError);

                return TranslationResponse.CreateError(request, error, "StdinStdout");
            }
        }
        catch (JsonException jsonEx)
        {
            // JSON解析失敗 → Pythonトレースバックの可能性
            _logger.LogError(jsonEx, "❌ [StdinStdout] JSON解析失敗、Python例外の可能性: {Response}",
                responseLine);

            throw new TranslationException(
                TranslationErrorType.InvalidResponse,
                $"Python応答がJSON形式ではありません（例外トレースバック?）: {responseLine}",
                jsonEx);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _stdinLock.Dispose();
        _disposed = true;
    }
}
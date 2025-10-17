using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Translation.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using CoreTranslationError = Baketa.Core.Translation.Models.TranslationError;
using ProtoLanguage = Baketa.Translation.V1.Language;

namespace Baketa.Infrastructure.Translation.Clients;

/// <summary>
/// gRPC通信ベース翻訳クライアント
/// Phase 2.3: Python gRPCサーバーとの通信クライアント実装
///
/// 特徴:
/// - HTTP/2ベースの高速双方向通信
/// - バッチ翻訳対応（TranslateBatch RPC）
/// - 接続プール自動管理
/// - エラーハンドリング（gRPC Status codes）
/// </summary>
public sealed class GrpcTranslationClient : ITranslationClient, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly TranslationService.TranslationServiceClient _client;
    private readonly ILogger _logger;
    private readonly string _serverAddress;
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="serverAddress">gRPCサーバーアドレス（例: "http://localhost:50051"）</param>
    /// <param name="logger">ロガー</param>
    public GrpcTranslationClient(string serverAddress, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);
        ArgumentNullException.ThrowIfNull(logger);

        _serverAddress = serverAddress;
        _logger = logger;

        // gRPC Channel作成（HTTP/2接続プール）
        _channel = GrpcChannel.ForAddress(_serverAddress, new GrpcChannelOptions
        {
            // HTTP/2 Keep-Alive設定（接続維持）
            // 🔧 [GEMINI_DEEP_FIX] TCP層KeepAlive強化による接続維持
            // 根本原因: 中間機器（ファイアウォール、NAT等）が112秒アイドルでTCP切断
            // 制約: Grpc.Net.ClientはgRPCレベル(L7)KeepAliveをサポートしていない
            //       (HttpClient and Kestrel limitation - grpc-dotnet公式ドキュメント)
            // 解決策: TCP層(L4)KeepAlivePingDelayを60秒→30秒に短縮し、中間機器タイムアウトを回避
            HttpHandler = new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan, // クライアント側のアイドルタイムアウト無効化
                KeepAlivePingDelay = TimeSpan.FromSeconds(10), // 🔧 [PHASE5.2E_FIX] 接続維持強化 - 30秒→10秒（2分アイドル後の再接続問題対策）
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10), // PING応答待ち時間
                EnableMultipleHttp2Connections = true
            }
        });

        _client = new TranslationService.TranslationServiceClient(_channel);
        _logger.LogInformation("GrpcTranslationClient initialized: {ServerAddress}", _serverAddress);
    }

    /// <inheritdoc/>
    public string CommunicationMode => "gRPC";

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        var sw = Stopwatch.StartNew();

        try
        {
            // TranslationRequest → gRPC TranslateRequest 変換
            var grpcRequest = new TranslateRequest
            {
                RequestId = request.RequestId.ToString(),
                SourceText = request.SourceText,
                SourceLanguage = new ProtoLanguage { Code = request.SourceLanguage.Code },
                TargetLanguage = new ProtoLanguage { Code = request.TargetLanguage.Code }
            };

            // 🔥 [PHASE3.1_DEBUG] 必ず出力される詳細ログ
            Console.WriteLine($"🔥 [gRPC_CLIENT] TranslateAsync開始 - SourceLang: {request.SourceLanguage.Code}, TargetLang: {request.TargetLanguage.Code}");
            Console.WriteLine($"🔥 [gRPC_CLIENT] SourceText: '{request.SourceText}'");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] 🔥 [gRPC_CLIENT] TranslateAsync - {request.SourceLanguage.Code} → {request.TargetLanguage.Code}, Text: '{request.SourceText}'\r\n"
            );

            _logger.LogDebug(
                "[gRPC] Translate: {SourceLang} -> {TargetLang}, Text: {Text}",
                request.SourceLanguage.Code,
                request.TargetLanguage.Code,
                request.SourceText.Length > 50 ? request.SourceText[..50] + "..." : request.SourceText
            );

            // gRPC Translate RPC呼び出し
            // 🔥 [PHASE5.2D_FIX] WaitForReady=true で TCP接続初期化待機
            // 問題: gRPC Channelは lazy initialization のため、最初のRPC呼び出し時にTCP接続を確立
            // 解決策: WaitForReady()で接続確立を待機し、初期化中のUNAVAILABLEエラーを防止
            var callOptions = new CallOptions(cancellationToken: cancellationToken)
                .WithWaitForReady(true); // 接続確立まで待機

            Console.WriteLine($"🔥 [gRPC_CLIENT] gRPC Translate RPC呼び出し開始（WaitForReady=true）...");
            var grpcResponse = await _client.TranslateAsync(grpcRequest, callOptions)
                .ConfigureAwait(false);

            sw.Stop();

            Console.WriteLine($"🔥 [gRPC_CLIENT] gRPC応答受信 - IsSuccess: {grpcResponse.IsSuccess}, TranslatedText: '{grpcResponse.TranslatedText}'");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] 🔥 [gRPC_CLIENT] Response - IsSuccess: {grpcResponse.IsSuccess}, Text: '{grpcResponse.TranslatedText}'\r\n"
            );

            // gRPC TranslateResponse → TranslationResponse 変換
            if (grpcResponse.IsSuccess)
            {
                return TranslationResponse.CreateSuccessWithConfidence(
                    request,
                    grpcResponse.TranslatedText,
                    grpcResponse.EngineName,
                    sw.ElapsedMilliseconds,
                    grpcResponse.ConfidenceScore
                );
            }
            else
            {
                var error = new CoreTranslationError
                {
                    ErrorCode = grpcResponse.Error?.ErrorCode ?? "UNKNOWN",
                    Message = grpcResponse.Error?.Message ?? "Translation failed"
                };

                return TranslationResponse.CreateError(request, error, grpcResponse.EngineName);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            sw.Stop();
            Console.WriteLine($"❌ [gRPC_CLIENT] TIMEOUT: {ex.Message}");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] ❌ [gRPC_CLIENT] TIMEOUT: {ex.Message}\r\n"
            );
            _logger.LogWarning("[gRPC] Translation timeout: {Message}", ex.Message);

            return TranslationResponse.CreateErrorFromException(
                request,
                "gRPC",
                "TIMEOUT",
                "Translation request timed out",
                ex,
                sw.ElapsedMilliseconds
            );
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            sw.Stop();
            Console.WriteLine($"❌ [gRPC_CLIENT] UNAVAILABLE: {ex.Message}");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] ❌ [gRPC_CLIENT] UNAVAILABLE - Server: {_serverAddress}, Error: {ex.Message}\r\n"
            );
            _logger.LogError("[gRPC] Server unavailable: {Message}", ex.Message);

            return TranslationResponse.CreateErrorFromException(
                request,
                "gRPC",
                "UNAVAILABLE",
                $"gRPC server unavailable: {_serverAddress}",
                ex,
                sw.ElapsedMilliseconds
            );
        }
        catch (RpcException ex)
        {
            sw.Stop();
            Console.WriteLine($"❌ [gRPC_CLIENT] RPC ERROR: StatusCode={ex.StatusCode}, Message={ex.Message}");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] ❌ [gRPC_CLIENT] RPC ERROR: StatusCode={ex.StatusCode}, Message={ex.Message}\r\n"
            );
            _logger.LogError("[gRPC] RPC error (Status: {StatusCode}): {Message}", ex.StatusCode, ex.Message);

            return TranslationResponse.CreateErrorFromException(
                request,
                "gRPC",
                ex.StatusCode.ToString(),
                $"gRPC error: {ex.Message}",
                ex,
                sw.ElapsedMilliseconds
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"❌ [gRPC_CLIENT] UNEXPECTED ERROR: {ex.GetType().Name} - {ex.Message}");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] ❌ [gRPC_CLIENT] UNEXPECTED: {ex.GetType().Name} - {ex.Message}\r\n{ex.StackTrace}\r\n"
            );
            _logger.LogError(ex, "[gRPC] Unexpected error during translation");

            return TranslationResponse.CreateErrorFromException(
                request,
                "gRPC",
                "UNKNOWN",
                "Unexpected error occurred",
                ex,
                sw.ElapsedMilliseconds
            );
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var request = new IsReadyRequest();
            // 🔥 [PHASE5.2D_FIX] WaitForReady=true で TCP接続初期化待機
            var callOptions = new CallOptions(cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            var response = await _client.IsReadyAsync(request, callOptions)
                .ConfigureAwait(false);

            _logger.LogDebug("[gRPC] IsReady: {Ready}, Status: {Status}", response.IsReady, response.Status);

            return response.IsReady;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning("[gRPC] IsReady check failed (Status: {StatusCode}): {Message}", ex.StatusCode, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[gRPC] IsReady check error");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var request = new HealthCheckRequest();
            // 🔥 [PHASE5.2D_FIX] WaitForReady=true で TCP接続初期化待機
            var callOptions = new CallOptions(cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            var response = await _client.HealthCheckAsync(request, callOptions)
                .ConfigureAwait(false);

            _logger.LogDebug("[gRPC] HealthCheck: {Healthy}, Status: {Status}", response.IsHealthy, response.Status);

            return response.IsHealthy;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning("[gRPC] HealthCheck failed (Status: {StatusCode}): {Message}", ex.StatusCode, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[gRPC] HealthCheck error");
            return false;
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _channel?.Dispose();
            _logger.LogInformation("GrpcTranslationClient disposed: {ServerAddress}", _serverAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing GrpcTranslationClient");
        }
        finally
        {
            _disposed = true;
        }
    }
}

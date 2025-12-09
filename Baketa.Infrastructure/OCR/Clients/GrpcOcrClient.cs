using System.Diagnostics;
using Baketa.Ocr.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Clients;

/// <summary>
/// gRPC通信ベースOCRクライアント
/// Issue #189: Surya OCR gRPCサーバーとの通信クライアント実装
///
/// 特徴:
/// - HTTP/2ベースの高速通信
/// - 画像データの効率的な転送（バイナリ形式）
/// - Keep-Alive設定で接続維持
/// - エラーハンドリング（gRPC Status codes）
/// </summary>
public sealed class GrpcOcrClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly OcrService.OcrServiceClient _client;
    private readonly ILogger _logger;
    private readonly string _serverAddress;
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="serverAddress">gRPCサーバーアドレス（例: "http://localhost:50052"）</param>
    /// <param name="logger">ロガー</param>
    public GrpcOcrClient(string serverAddress, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);
        ArgumentNullException.ThrowIfNull(logger);

        _serverAddress = serverAddress;
        _logger = logger;

        // gRPC Channel作成（HTTP/2接続プール）
        _channel = GrpcChannel.ForAddress(_serverAddress, new GrpcChannelOptions
        {
            // HTTP/2 Keep-Alive設定（接続維持）
            HttpHandler = new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(10),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                EnableMultipleHttp2Connections = true,
                // OCR画像転送用に大きなメッセージサイズを許可
                MaxResponseHeadersLength = 64 * 1024 // 64KB
            },
            // 最大メッセージサイズ（画像データ用）
            MaxReceiveMessageSize = 50 * 1024 * 1024, // 50MB
            MaxSendMessageSize = 50 * 1024 * 1024     // 50MB
        });

        _client = new OcrService.OcrServiceClient(_channel);
        _logger.LogInformation("GrpcOcrClient initialized: {ServerAddress}", _serverAddress);
    }

    /// <summary>
    /// 通信モード識別子
    /// </summary>
    public string CommunicationMode => "gRPC-OCR";

    /// <summary>
    /// 画像からテキストを認識します
    /// </summary>
    /// <param name="imageData">画像データ（PNG/JPEG）</param>
    /// <param name="imageFormat">画像フォーマット（"png" or "jpeg"）</param>
    /// <param name="languages">認識対象言語（空の場合は自動検出）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCRレスポンス</returns>
    public async Task<OcrResponse> RecognizeAsync(
        byte[] imageData,
        string imageFormat = "png",
        IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageData);

        var sw = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString();

        try
        {
            var grpcRequest = new OcrRequest
            {
                RequestId = requestId,
                ImageData = Google.Protobuf.ByteString.CopyFrom(imageData),
                ImageFormat = imageFormat,
                Engine = OcrEngineType.Surya,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            // 言語設定
            if (languages != null && languages.Count > 0)
            {
                grpcRequest.Languages.AddRange(languages);
            }

            _logger.LogDebug(
                "[gRPC-OCR] Recognize: ImageSize={ImageSize}KB, Format={Format}, Languages={Languages}",
                imageData.Length / 1024,
                imageFormat,
                languages != null ? string.Join(",", languages) : "auto");

            // gRPC呼び出し（WaitForReadyで接続待ち）
            var callOptions = new CallOptions(
                deadline: DateTime.UtcNow.AddSeconds(60),
                cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            var response = await _client.RecognizeAsync(grpcRequest, callOptions).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "[gRPC-OCR] Success: {RegionCount} regions detected in {ElapsedMs}ms (Server: {ServerMs}ms)",
                    response.RegionCount,
                    sw.ElapsedMilliseconds,
                    response.ProcessingTimeMs);
            }
            else
            {
                _logger.LogWarning(
                    "[gRPC-OCR] Failed: {ErrorType} - {ErrorMessage}",
                    response.Error?.ErrorType,
                    response.Error?.Message);
            }

            return response;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] Server unavailable: {ServerAddress}", _serverAddress);

            return CreateErrorResponse(
                requestId,
                OcrErrorType.ServiceUnavailable,
                $"OCR server unavailable: {_serverAddress}",
                ex.Message,
                true);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] Timeout after {ElapsedMs}ms", sw.ElapsedMilliseconds);

            return CreateErrorResponse(
                requestId,
                OcrErrorType.Timeout,
                "OCR request timed out",
                ex.Message,
                true);
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] RPC error: {StatusCode}", ex.StatusCode);

            return CreateErrorResponse(
                requestId,
                OcrErrorType.ProcessingError,
                $"RPC error: {ex.StatusCode}",
                ex.Message,
                ex.StatusCode != StatusCode.InvalidArgument);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogDebug("[gRPC-OCR] Request cancelled after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] Unexpected error");

            return CreateErrorResponse(
                requestId,
                OcrErrorType.Unknown,
                "Unexpected error during OCR",
                ex.Message,
                false);
        }
    }

    /// <summary>
    /// ヘルスチェックを実行します
    /// </summary>
    public async Task<OcrHealthCheckResponse> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var callOptions = new CallOptions(
                deadline: DateTime.UtcNow.AddSeconds(10),
                cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            return await _client.HealthCheckAsync(new OcrHealthCheckRequest(), callOptions).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "[gRPC-OCR] Health check failed: {StatusCode}", ex.StatusCode);

            return new OcrHealthCheckResponse
            {
                IsHealthy = false,
                Status = $"Error: {ex.StatusCode}",
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    /// <summary>
    /// サーバーの準備状態を確認します
    /// </summary>
    public async Task<OcrIsReadyResponse> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var callOptions = new CallOptions(
                deadline: DateTime.UtcNow.AddSeconds(10),
                cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            return await _client.IsReadyAsync(new OcrIsReadyRequest(), callOptions).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "[gRPC-OCR] IsReady check failed: {StatusCode}", ex.StatusCode);

            return new OcrIsReadyResponse
            {
                IsReady = false,
                Status = $"Error: {ex.StatusCode}",
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    /// <summary>
    /// エラーレスポンスを生成
    /// </summary>
    private static OcrResponse CreateErrorResponse(
        string requestId,
        OcrErrorType errorType,
        string message,
        string details,
        bool isRetryable)
    {
        return new OcrResponse
        {
            RequestId = requestId,
            IsSuccess = false,
            RegionCount = 0,
            ProcessingTimeMs = 0,
            Error = new OcrError
            {
                ErrorType = errorType,
                Message = message,
                Details = details,
                IsRetryable = isRetryable
            },
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _channel.Dispose();
            _logger.LogDebug("GrpcOcrClient disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing GrpcOcrClient");
        }
    }
}

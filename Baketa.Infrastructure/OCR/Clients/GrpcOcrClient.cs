using System.Diagnostics;
using Baketa.Ocr.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Clients;

/// <summary>
/// gRPC通信ベースOCRクライアント
/// Issue #189: Surya OCR gRPCサーバーとの通信クライアント実装
/// Issue #300: サイレントリトライ機構追加
///
/// 特徴:
/// - HTTP/2ベースの高速通信
/// - 画像データの効率的な転送（バイナリ形式）
/// - Keep-Alive設定で接続維持
/// - エラーハンドリング（gRPC Status codes）
/// - サイレントリトライ（3回、エクスポネンシャルバックオフ）
/// </summary>
public sealed class GrpcOcrClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly OcrService.OcrServiceClient _client;
    private readonly ILogger _logger;
    private readonly string _serverAddress;
    private bool _disposed;

    // Issue #300: リトライ設定
    private const int MaxRetryCount = 2;  // 2回リトライ（計3回試行）
    private static readonly int[] RetryDelaysMs = [500, 1000]; // 短いバックオフ
    private const int RetryTimeoutSeconds = 5; // リトライ時は短いタイムアウト（通常60秒→5秒）

    // [Issue #334] CPUフォールバック用の連続タイムアウト検知
    private int _consecutiveTimeouts;
    private bool _hasSwitchedToCpu;

    /// <summary>
    /// [Issue #334] CPUフォールバックまでの連続タイムアウト閾値（デフォルト: 3）
    /// </summary>
    public int CpuFallbackTimeoutThreshold { get; set; } = 3;

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
    /// Issue #300: サイレントリトライ機構追加（最大3回、エクスポネンシャルバックオフ）
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

        var requestId = Guid.NewGuid().ToString();
        OcrResponse? lastResponse = null;
        RpcException? lastException = null;

        // Issue #300: サイレントリトライループ
        for (var attempt = 0; attempt <= MaxRetryCount; attempt++)
        {
            var sw = Stopwatch.StartNew();

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

                if (attempt == 0)
                {
                    _logger.LogDebug(
                        "[gRPC-OCR] Recognize: ImageSize={ImageSize}KB, Format={Format}, Languages={Languages}",
                        imageData.Length / 1024,
                        imageFormat,
                        languages != null ? string.Join(",", languages) : "auto");
                }
                else
                {
                    _logger.LogDebug(
                        "[gRPC-OCR] Retry attempt {Attempt}/{MaxRetry} (timeout: {Timeout}s)",
                        attempt, MaxRetryCount, RetryTimeoutSeconds);
                }

                // gRPC呼び出し（WaitForReadyで接続待ち）
                // Issue #300: リトライ時は短いタイムアウト（初回60秒、リトライ5秒）
                var timeoutSeconds = attempt == 0 ? 60 : RetryTimeoutSeconds;
                var callOptions = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: cancellationToken)
                    .WithWaitForReady(true);

                var response = await _client.RecognizeAsync(grpcRequest, callOptions).ConfigureAwait(false);

                sw.Stop();

                if (response.IsSuccess)
                {
                    // [Issue #334] 成功時はタイムアウトカウンタをリセット
                    _consecutiveTimeouts = 0;

                    if (attempt > 0)
                    {
                        _logger.LogInformation(
                            "[gRPC-OCR] Success after {Attempt} retries: {RegionCount} regions in {ElapsedMs}ms",
                            attempt, response.RegionCount, sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[gRPC-OCR] Success: {RegionCount} regions detected in {ElapsedMs}ms (Server: {ServerMs}ms)",
                            response.RegionCount,
                            sw.ElapsedMilliseconds,
                            response.ProcessingTimeMs);
                    }
                    return response;
                }

                _logger.LogWarning(
                    "[gRPC-OCR] Failed: {ErrorType} - {ErrorMessage}",
                    response.Error?.ErrorType,
                    response.Error?.Message);

                // サーバー側エラーはリトライ対象外
                return response;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                sw.Stop();
                lastException = ex;

                if (attempt < MaxRetryCount)
                {
                    var delayMs = RetryDelaysMs[attempt];
                    _logger.LogWarning(
                        "[gRPC-OCR] Server unavailable (attempt {Attempt}/{MaxRetry}), retrying in {DelayMs}ms...",
                        attempt + 1, MaxRetryCount, delayMs);

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogError(ex, "[gRPC-OCR] Server unavailable after {MaxRetry} retries: {ServerAddress}",
                    MaxRetryCount, _serverAddress);

                lastResponse = CreateErrorResponse(
                    requestId,
                    OcrErrorType.ServiceUnavailable,
                    $"OCR server unavailable after {MaxRetryCount} retries: {_serverAddress}",
                    ex.Message,
                    true);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                sw.Stop();
                lastException = ex;

                // [Issue #334] タイムアウトカウント増加とCPUフォールバック判定
                _consecutiveTimeouts++;
                _logger.LogWarning(
                    "[gRPC-OCR] Timeout detected (consecutive: {Count}/{Threshold})",
                    _consecutiveTimeouts, CpuFallbackTimeoutThreshold);

                // CPUフォールバック実行（まだ切り替えていない場合のみ）
                if (_consecutiveTimeouts >= CpuFallbackTimeoutThreshold && !_hasSwitchedToCpu)
                {
                    await TrySwitchToCpuAsync(cancellationToken).ConfigureAwait(false);
                }

                if (attempt < MaxRetryCount)
                {
                    var delayMs = RetryDelaysMs[attempt];
                    _logger.LogWarning(
                        "[gRPC-OCR] Timeout (attempt {Attempt}/{MaxRetry}), retrying in {DelayMs}ms...",
                        attempt + 1, MaxRetryCount, delayMs);

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogError(ex, "[gRPC-OCR] Timeout after {MaxRetry} retries ({ElapsedMs}ms total)",
                    MaxRetryCount, sw.ElapsedMilliseconds);

                lastResponse = CreateErrorResponse(
                    requestId,
                    OcrErrorType.Timeout,
                    $"OCR request timed out after {MaxRetryCount} retries",
                    ex.Message,
                    true);
            }
            catch (RpcException ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[gRPC-OCR] RPC error: {StatusCode}", ex.StatusCode);

                // 非リトライ可能なエラーは即座に返す
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

                // 予期しないエラーはリトライ対象外
                return CreateErrorResponse(
                    requestId,
                    OcrErrorType.Unknown,
                    "Unexpected error during OCR",
                    ex.Message,
                    false);
            }
        }

        // リトライ回数超過時
        return lastResponse ?? CreateErrorResponse(
            requestId,
            OcrErrorType.ServiceUnavailable,
            $"OCR failed after {MaxRetryCount} retries",
            lastException?.Message ?? "Unknown error",
            true);
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
    /// [Issue #320] テキスト領域の位置のみを検出します（Recognition をスキップ、約10倍高速）
    /// ROI学習用の高速検出に使用
    /// </summary>
    /// <param name="imageData">画像データ（PNG/JPEG）</param>
    /// <param name="imageFormat">画像フォーマット（"png" or "jpeg"）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>Detection専用レスポンス（テキスト内容なし、バウンディングボックスのみ）</returns>
    public async Task<DetectResponse> DetectAsync(
        byte[] imageData,
        string imageFormat = "png",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageData);

        var requestId = Guid.NewGuid().ToString();
        DetectResponse? lastResponse = null;
        RpcException? lastException = null;

        // サイレントリトライループ（CPUモードでは3秒以上かかることがある）
        const int detectTimeoutSeconds = 15;
        const int detectRetryTimeoutSeconds = 10;

        for (var attempt = 0; attempt <= MaxRetryCount; attempt++)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var grpcRequest = new DetectRequest
                {
                    RequestId = requestId,
                    ImageData = Google.Protobuf.ByteString.CopyFrom(imageData),
                    ImageFormat = imageFormat,
                    Engine = OcrEngineType.Surya,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };

                if (attempt == 0)
                {
                    _logger.LogDebug(
                        "[gRPC-OCR] Detect (Detection-Only): ImageSize={ImageSize}KB, Format={Format}",
                        imageData.Length / 1024,
                        imageFormat);
                }
                else
                {
                    _logger.LogDebug(
                        "[gRPC-OCR] Detect retry attempt {Attempt}/{MaxRetry}",
                        attempt, MaxRetryCount);
                }

                // gRPC呼び出し（Detection は高速なのでタイムアウト短め）
                var timeoutSeconds = attempt == 0 ? detectTimeoutSeconds : detectRetryTimeoutSeconds;
                var callOptions = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: cancellationToken)
                    .WithWaitForReady(true);

                var response = await _client.DetectAsync(grpcRequest, callOptions).ConfigureAwait(false);

                sw.Stop();

                if (response.IsSuccess)
                {
                    _logger.LogInformation(
                        "[gRPC-OCR] Detection-Only: {RegionCount} regions in {ElapsedMs}ms (Server: {ServerMs}ms)",
                        response.RegionCount,
                        sw.ElapsedMilliseconds,
                        response.ProcessingTimeMs);
                    return response;
                }

                _logger.LogWarning(
                    "[gRPC-OCR] Detection failed: {ErrorType} - {ErrorMessage}",
                    response.Error?.ErrorType,
                    response.Error?.Message);

                return response;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                sw.Stop();
                lastException = ex;

                if (attempt < MaxRetryCount)
                {
                    var delayMs = RetryDelaysMs[attempt];
                    _logger.LogWarning(
                        "[gRPC-OCR] Detect: Server unavailable (attempt {Attempt}/{MaxRetry}), retrying in {DelayMs}ms...",
                        attempt + 1, MaxRetryCount, delayMs);

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogError(ex, "[gRPC-OCR] Detect: Server unavailable after {MaxRetry} retries",
                    MaxRetryCount);

                lastResponse = CreateDetectErrorResponse(
                    requestId,
                    OcrErrorType.ServiceUnavailable,
                    $"OCR server unavailable after {MaxRetryCount} retries",
                    ex.Message,
                    true);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                sw.Stop();
                lastException = ex;

                if (attempt < MaxRetryCount)
                {
                    var delayMs = RetryDelaysMs[attempt];
                    _logger.LogWarning(
                        "[gRPC-OCR] Detect: Timeout (attempt {Attempt}/{MaxRetry}), retrying in {DelayMs}ms...",
                        attempt + 1, MaxRetryCount, delayMs);

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogError(ex, "[gRPC-OCR] Detect: Timeout after {MaxRetry} retries",
                    MaxRetryCount);

                lastResponse = CreateDetectErrorResponse(
                    requestId,
                    OcrErrorType.Timeout,
                    $"Detection request timed out after {MaxRetryCount} retries",
                    ex.Message,
                    true);
            }
            catch (RpcException ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[gRPC-OCR] Detect RPC error: {StatusCode}", ex.StatusCode);

                return CreateDetectErrorResponse(
                    requestId,
                    OcrErrorType.ProcessingError,
                    $"RPC error: {ex.StatusCode}",
                    ex.Message,
                    ex.StatusCode != StatusCode.InvalidArgument);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                _logger.LogDebug("[gRPC-OCR] Detect request cancelled after {ElapsedMs}ms", sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[gRPC-OCR] Detect unexpected error");

                return CreateDetectErrorResponse(
                    requestId,
                    OcrErrorType.Unknown,
                    "Unexpected error during detection",
                    ex.Message,
                    false);
            }
        }

        return lastResponse ?? CreateDetectErrorResponse(
            requestId,
            OcrErrorType.ServiceUnavailable,
            $"Detection failed after {MaxRetryCount} retries",
            lastException?.Message ?? "Unknown error",
            true);
    }

    /// <summary>
    /// [Issue #330] 複数画像を一括でOCR処理します（バッチ認識）
    /// 部分OCRで複数領域を処理する際のgRPC呼び出しオーバーヘッドを削減
    /// </summary>
    /// <param name="imageDataList">画像データのリスト（各要素はPNG/JPEG）</param>
    /// <param name="imageFormat">画像フォーマット（"png" or "jpeg"）</param>
    /// <param name="languages">認識対象言語（空の場合は自動検出）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>バッチOCRレスポンス</returns>
    public async Task<RecognizeBatchResponse> RecognizeBatchAsync(
        IReadOnlyList<byte[]> imageDataList,
        string imageFormat = "png",
        IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageDataList);

        if (imageDataList.Count == 0)
        {
            return new RecognizeBatchResponse
            {
                BatchId = Guid.NewGuid().ToString(),
                IsSuccess = true,
                SuccessCount = 0,
                TotalCount = 0,
                TotalProcessingTimeMs = 0,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        var batchId = Guid.NewGuid().ToString();
        var sw = Stopwatch.StartNew();

        try
        {
            var grpcRequest = new RecognizeBatchRequest
            {
                BatchId = batchId,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            // 各画像のリクエストを作成
            for (var i = 0; i < imageDataList.Count; i++)
            {
                var ocrRequest = new OcrRequest
                {
                    RequestId = $"{batchId}-{i}",
                    ImageData = Google.Protobuf.ByteString.CopyFrom(imageDataList[i]),
                    ImageFormat = imageFormat,
                    Engine = OcrEngineType.Surya,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };

                if (languages != null && languages.Count > 0)
                {
                    ocrRequest.Languages.AddRange(languages);
                }

                grpcRequest.Requests.Add(ocrRequest);
            }

            var totalImageSizeKb = imageDataList.Sum(data => data.Length) / 1024;
            _logger.LogDebug(
                "[gRPC-OCR] RecognizeBatch: Count={Count}, TotalSize={TotalSizeKB}KB, Format={Format}",
                imageDataList.Count,
                totalImageSizeKb,
                imageFormat);

            // バッチOCRは時間がかかるのでタイムアウトを長めに設定
            // 1領域あたり約1秒 + オーバーヘッド
            var timeoutSeconds = Math.Max(60, imageDataList.Count * 2 + 30);
            var callOptions = new CallOptions(
                deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            var response = await _client.RecognizeBatchAsync(grpcRequest, callOptions).ConfigureAwait(false);

            sw.Stop();

            _logger.LogInformation(
                "[gRPC-OCR] RecognizeBatch completed: {SuccessCount}/{TotalCount} success in {ElapsedMs}ms (Server: {ServerMs}ms)",
                response.SuccessCount,
                response.TotalCount,
                sw.ElapsedMilliseconds,
                response.TotalProcessingTimeMs);

            return response;
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] RecognizeBatch RPC error: {StatusCode}", ex.StatusCode);

            var errorType = ex.StatusCode switch
            {
                StatusCode.Unavailable => OcrErrorType.ServiceUnavailable,
                StatusCode.DeadlineExceeded => OcrErrorType.Timeout,
                StatusCode.ResourceExhausted => OcrErrorType.OutOfMemory,
                _ => OcrErrorType.ProcessingError
            };

            return new RecognizeBatchResponse
            {
                BatchId = batchId,
                IsSuccess = false,
                SuccessCount = 0,
                TotalCount = imageDataList.Count,
                TotalProcessingTimeMs = sw.ElapsedMilliseconds,
                Error = new OcrError
                {
                    ErrorType = errorType,
                    Message = $"Batch OCR failed: {ex.StatusCode}",
                    Details = ex.Message,
                    IsRetryable = ex.StatusCode == StatusCode.Unavailable || ex.StatusCode == StatusCode.DeadlineExceeded
                },
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogDebug("[gRPC-OCR] RecognizeBatch cancelled after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] RecognizeBatch unexpected error");

            return new RecognizeBatchResponse
            {
                BatchId = batchId,
                IsSuccess = false,
                SuccessCount = 0,
                TotalCount = imageDataList.Count,
                TotalProcessingTimeMs = sw.ElapsedMilliseconds,
                Error = new OcrError
                {
                    ErrorType = OcrErrorType.Unknown,
                    Message = "Unexpected error during batch OCR",
                    Details = ex.Message,
                    IsRetryable = false
                },
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
    /// [Issue #320] Detection用エラーレスポンスを生成
    /// </summary>
    private static DetectResponse CreateDetectErrorResponse(
        string requestId,
        OcrErrorType errorType,
        string message,
        string details,
        bool isRetryable)
    {
        return new DetectResponse
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
    /// [Issue #334] デバイスを切り替えます（GPU/CPU）
    /// </summary>
    /// <param name="targetDevice">切り替え先デバイス ("cpu" or "cuda")</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>切り替え結果</returns>
    public async Task<SwitchDeviceResponse> SwitchDeviceAsync(
        string targetDevice,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDevice);

        var requestId = Guid.NewGuid().ToString();
        var sw = Stopwatch.StartNew();

        try
        {
            var request = new SwitchDeviceRequest
            {
                TargetDevice = targetDevice,
                RequestId = requestId
            };

            _logger.LogInformation("[gRPC-OCR] SwitchDevice: Switching to {TargetDevice}...", targetDevice);

            var callOptions = new CallOptions(
                deadline: DateTime.UtcNow.AddSeconds(60), // デバイス切り替えは時間がかかる可能性あり
                cancellationToken: cancellationToken)
                .WithWaitForReady(true);

            var response = await _client.SwitchDeviceAsync(request, callOptions).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "[gRPC-OCR] SwitchDevice success: {PreviousDevice} → {CurrentDevice} ({SwitchTimeMs}ms)",
                    response.PreviousDevice,
                    response.CurrentDevice,
                    response.SwitchTimeMs);

                // CPUに切り替えた場合はフラグを設定
                if (targetDevice.Equals("cpu", StringComparison.OrdinalIgnoreCase))
                {
                    _hasSwitchedToCpu = true;
                }
            }
            else
            {
                _logger.LogWarning(
                    "[gRPC-OCR] SwitchDevice failed: {ErrorType} - {Message}",
                    response.Error?.ErrorType,
                    response.Error?.Message);
            }

            return response;
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] SwitchDevice RPC error: {StatusCode}", ex.StatusCode);

            return new SwitchDeviceResponse
            {
                IsSuccess = false,
                Message = $"RPC error: {ex.StatusCode}",
                Error = new OcrError
                {
                    ErrorType = OcrErrorType.ProcessingError,
                    Message = $"Failed to switch device: {ex.StatusCode}",
                    Details = ex.Message,
                    IsRetryable = ex.StatusCode == StatusCode.Unavailable
                },
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[gRPC-OCR] SwitchDevice unexpected error");

            return new SwitchDeviceResponse
            {
                IsSuccess = false,
                Message = $"Unexpected error: {ex.Message}",
                Error = new OcrError
                {
                    ErrorType = OcrErrorType.Unknown,
                    Message = "Failed to switch device",
                    Details = ex.Message,
                    IsRetryable = false
                },
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    /// <summary>
    /// [Issue #334] CPUモードへのフォールバックを試行
    /// </summary>
    private async Task TrySwitchToCpuAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "[gRPC-OCR] [Issue #334] Consecutive timeouts reached threshold ({Threshold}), switching to CPU mode...",
            CpuFallbackTimeoutThreshold);

        try
        {
            var response = await SwitchDeviceAsync("cpu", cancellationToken).ConfigureAwait(false);

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "[gRPC-OCR] [Issue #334] Successfully switched to CPU mode: {Message}",
                    response.Message);
                _consecutiveTimeouts = 0; // カウンタリセット
            }
            else
            {
                _logger.LogWarning(
                    "[gRPC-OCR] [Issue #334] Failed to switch to CPU mode: {Message}",
                    response.Message ?? response.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[gRPC-OCR] [Issue #334] Error switching to CPU mode");
        }
    }

    /// <summary>
    /// [Issue #334] 現在のCPUフォールバック状態を取得
    /// </summary>
    public bool HasSwitchedToCpu => _hasSwitchedToCpu;

    /// <summary>
    /// [Issue #334] CPUフォールバック状態をリセット（テスト用）
    /// </summary>
    public void ResetCpuFallbackState()
    {
        _consecutiveTimeouts = 0;
        _hasSwitchedToCpu = false;
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

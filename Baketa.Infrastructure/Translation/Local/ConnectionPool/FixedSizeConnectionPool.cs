using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Translation.Local.ConnectionPool;

/// <summary>
/// 固定サイズ接続プール実装（Issue #147: Phase 1）
/// Channel&lt;T&gt;ベースの高性能接続管理によりOptimizedPythonTranslationEngineの接続ロック競合を解決
/// </summary>
public sealed class FixedSizeConnectionPool : IConnectionPool
{
    private readonly ILogger<FixedSizeConnectionPool> _logger;
    private readonly IConfiguration _configuration;
    private readonly TranslationSettings _settings;
    private readonly Channel<PersistentConnection> _connectionChannel;
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly int _maxConnections;
    private readonly int _minConnections;
    private readonly System.Threading.Timer? _healthCheckTimer;
    private readonly CancellationTokenSource _disposalCts = new();
    
    private int _activeConnections;
    private int _totalConnectionsCreated;
    private bool _disposed;
    
    /// <summary>
    /// アクティブな接続数を取得する
    /// </summary>
    public int ActiveConnections => _activeConnections;

    /// <summary>
    /// 固定サイズ接続プールを初期化
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="configuration">設定サービス</param>
    /// <param name="options">翻訳設定オプション</param>
    public FixedSizeConnectionPool(
        ILogger<FixedSizeConnectionPool> logger,
        IConfiguration configuration,
        IOptions<TranslationSettings> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        
        // 接続数の計算
        _maxConnections = _settings.MaxConnections ?? Math.Max(8, Environment.ProcessorCount / 2);  // 🔧 CONCURRENT_OPTIMIZATION: 最小8接続を保証
        _minConnections = _settings.MinConnections;
        
        if (_maxConnections < 1) _maxConnections = 1;
        if (_minConnections < 1) _minConnections = 1;
        if (_minConnections > _maxConnections) _minConnections = _maxConnections;
        
        _logger.LogInformation("接続プール初期化: 最大接続数={MaxConnections}, 最小接続数={MinConnections}", 
            _maxConnections, _minConnections);
        
        // Channel設定
        var channelOptions = new BoundedChannelOptions(_maxConnections)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };
        
        _connectionChannel = Channel.CreateBounded<PersistentConnection>(channelOptions);
        _poolSemaphore = new SemaphoreSlim(_maxConnections, _maxConnections);
        
        // ヘルスチェックタイマー設定
        if (_settings.HealthCheckIntervalMs > 0)
        {
            _healthCheckTimer = new System.Threading.Timer(
                PerformHealthCheck, 
                null, 
                TimeSpan.FromMilliseconds(_settings.HealthCheckIntervalMs),
                TimeSpan.FromMilliseconds(_settings.HealthCheckIntervalMs));
        }
        
        _ = Task.Run(InitializeMinConnectionsAsync, _disposalCts.Token);
    }

    /// <summary>
    /// 設定に基づいて動的にポート番号を取得
    /// NLLB-200: 5556、その他: 5556（レガシー互換性）
    /// </summary>
    private int GetServerPort()
    {
        var defaultEngineString = _configuration["Translation:DefaultEngine"];
        var defaultEngine = Enum.TryParse<TranslationEngine>(defaultEngineString, out var parsedEngine) 
            ? parsedEngine 
            : TranslationEngine.NLLB200;

        return defaultEngine switch
        {
            TranslationEngine.NLLB200 => 5556,
            _ => 5556 // レガシー互換性のため維持
        };
    }

    /// <summary>
    /// 接続プールから接続を取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>永続接続インスタンス</returns>
    public async Task<PersistentConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);
        
        try
        {
            var connectionTimeoutMs = _settings.ConnectionTimeoutMs;
            using var timeoutCts = new CancellationTokenSource(connectionTimeoutMs);
            using var finalCts = CancellationTokenSource.CreateLinkedTokenSource(
                combinedCts.Token, timeoutCts.Token);

            // セマフォで同時接続数制御
            await _poolSemaphore.WaitAsync(finalCts.Token);
            
            try
            {
                // 既存接続の取得を試行
                if (_connectionChannel.Reader.TryRead(out var existingConnection))
                {
                    if (await IsConnectionHealthyAsync(existingConnection, finalCts.Token))
                    {
                        _logger.LogDebug("既存接続を再利用: {ConnectionId}", existingConnection.Id);
                        return existingConnection;
                    }
                    
                    // 不健全な接続は破棄
                    _logger.LogWarning("不健全な接続を破棄: {ConnectionId}", existingConnection.Id);
                    await DisposeConnectionSafelyAsync(existingConnection);
                    Interlocked.Decrement(ref _activeConnections);
                }
                
                // 新しい接続を作成
                var newConnection = await CreateNewConnectionAsync(finalCts.Token);
                Interlocked.Increment(ref _activeConnections);
                Interlocked.Increment(ref _totalConnectionsCreated);
                
                _logger.LogDebug("新規接続を作成: {ConnectionId}", newConnection.Id);
                return newConnection;
            }
            catch
            {
                _poolSemaphore.Release(); // 例外時のセマフォリリース
                throw;
            }
        }
        finally
        {
            combinedCts.Dispose();
        }
    }

    /// <summary>
    /// 接続を接続プールに返却
    /// </summary>
    /// <param name="connection">返却する接続</param>
    public async Task ReturnConnectionAsync(PersistentConnection connection, CancellationToken cancellationToken = default)
    {
        if (_disposed || connection == null)
        {
            _poolSemaphore.Release();
            return;
        }

        try
        {
            // 接続の健全性チェック
            if (await IsConnectionHealthyAsync(connection, _disposalCts.Token))
            {
                // 健全な接続をプールに返却
                if (_connectionChannel.Writer.TryWrite(connection))
                {
                    _logger.LogDebug("接続をプールに返却: {ConnectionId}", connection.Id);
                }
                else
                {
                    // Channel満杯の場合は接続を破棄
                    _logger.LogDebug("プール満杯のため接続を破棄: {ConnectionId}", connection.Id);
                    await connection.DisposeAsync();
                    Interlocked.Decrement(ref _activeConnections);
                }
            }
            else
            {
                // 不健全な接続は破棄
                _logger.LogWarning("不健全な接続を破棄して返却: {ConnectionId}", connection.Id);
                await connection.DisposeAsync();
                Interlocked.Decrement(ref _activeConnections);
            }
        }
        finally
        {
            _poolSemaphore.Release();
        }
    }

    /// <summary>
    /// 接続プールメトリクスを取得
    /// </summary>
    public ConnectionPoolMetrics GetMetrics()
    {
        var queuedConnections = _connectionChannel.Reader.CanCount ? 
            _connectionChannel.Reader.Count : 0;
        
        return new ConnectionPoolMetrics
        {
            ActiveConnections = _activeConnections,
            QueuedConnections = queuedConnections,
            TotalConnectionsCreated = _totalConnectionsCreated,
            MaxConnections = _maxConnections,
            MinConnections = _minConnections,
            ConnectionUtilization = _maxConnections > 0 ? (double)_activeConnections / _maxConnections : 0,
            AvailableConnections = _poolSemaphore.CurrentCount
        };
    }

    /// <summary>
    /// 最小接続数の初期化
    /// </summary>
    private async Task InitializeMinConnectionsAsync()
    {
        try
        {
            _logger.LogInformation("最小接続数({MinConnections})の初期化開始", _minConnections);
            
            for (int i = 0; i < _minConnections; i++)
            {
                if (_disposalCts.Token.IsCancellationRequested) break;
                
                try
                {
                    var connection = await CreateNewConnectionAsync(_disposalCts.Token);
                    
                    if (_connectionChannel.Writer.TryWrite(connection))
                    {
                        Interlocked.Increment(ref _activeConnections);
                        Interlocked.Increment(ref _totalConnectionsCreated);
                        _logger.LogDebug("初期接続を作成: {ConnectionId}", connection.Id);
                    }
                    else
                    {
                        await connection.DisposeAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "初期接続の作成に失敗: {Index}", i);
                }
            }
            
            _logger.LogInformation("最小接続数の初期化完了: {ActiveConnections}個", _activeConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "最小接続数の初期化中にエラーが発生");
        }
    }

    /// <summary>
    /// 新しい永続接続を作成
    /// </summary>
    private async Task<PersistentConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        TcpClient? tcpClient = null;
        NetworkStream? stream = null;
        StreamReader? reader = null;
        StreamWriter? writer = null;
        
        try
        {
            tcpClient = new TcpClient();
            var serverPort = GetServerPort();
            await tcpClient.ConnectAsync("127.0.0.1", serverPort, cancellationToken);
            
            stream = tcpClient.GetStream();
            
            // タイムアウトとバッファサイズの最適化
            stream.ReadTimeout = _settings.ConnectionTimeoutMs;
            stream.WriteTimeout = _settings.ConnectionTimeoutMs;
            
            // 🔧 [CRITICAL_ENCODING_FIX] システムレベルUTF-8エンコーディング指定（Windows問題対応）
            var utf8EncodingNoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            reader = new StreamReader(stream, utf8EncodingNoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            writer = new StreamWriter(stream, utf8EncodingNoBom, bufferSize: 8192, leaveOpen: true) 
            { 
                AutoFlush = false // パフォーマンス向上のため手動フラッシュ
            };
            
            var connection = new PersistentConnection(connectionId, tcpClient, stream, reader, writer);
            
            _logger.LogDebug("新規TCP接続を確立: {ConnectionId}", connectionId);
            return connection;
        }
        catch (Exception ex)
        {
            // 例外時のリソース確実解放
            writer?.Dispose();
            reader?.Dispose();
            stream?.Dispose();
            tcpClient?.Dispose();
            
            _logger.LogError(ex, "TCP接続の確立に失敗: {ConnectionId}", connectionId);
            throw;
        }
    }
    
    /// <summary>
    /// 接続を安全に破棄
    /// </summary>
    private async Task DisposeConnectionSafelyAsync(PersistentConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "接続破棄中にエラーが発生: {ConnectionId}", connection.Id);
        }
    }

    /// <summary>
    /// 接続の健全性をチェック
    /// </summary>
    private async Task<bool> IsConnectionHealthyAsync(PersistentConnection connection, CancellationToken cancellationToken)
    {
        if (connection == null || connection.IsDisposed)
            return false;

        try
        {
            // TCPクライアントの基本状態確認
            if (!connection.TcpClient.Connected)
                return false;

            // NetworkStreamの読み書き可能性確認
            if (!connection.Stream.CanRead || !connection.Stream.CanWrite)
                return false;

            // Socket level の確認（詳細な接続状態）
            var socket = connection.TcpClient.Client;
            if (socket == null || !socket.Connected)
                return false;

            // Poll機能でソケット状態確認（非ブロッキング）
            bool isConnected = !(socket.Poll(0, System.Net.Sockets.SelectMode.SelectRead) && socket.Available == 0);
            if (!isConnected)
                return false;

            // 簡易的なping確認（オプション）
            using var timeoutCts = new CancellationTokenSource(1000); // 1秒タイムアウト
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                await connection.Writer.WriteLineAsync("{\"ping\":true}");
                await connection.Writer.FlushAsync(); // 手動フラッシュ（AutoFlush=falseのため）
                
                var response = await connection.Reader.ReadLineAsync();
                return !string.IsNullOrEmpty(response);
            }
            catch (OperationCanceledException)
            {
                // タイムアウトの場合は接続不良と判定
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// すべての接続のヘルスチェックを実行する
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    public async Task PerformHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            var metrics = GetMetrics();
            _logger.LogDebug(
                "接続プールヘルスチェック: アクティブ={Active}, キュー={Queued}, 利用率={Utilization:P1}",
                metrics.ActiveConnections, metrics.QueuedConnections, metrics.ConnectionUtilization);

            // 不健全な接続の削除（実装は将来の拡張として残す）
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ヘルスチェック中にエラーが発生");
        }
    }

    /// <summary>
    /// 定期的なヘルスチェック実行
    /// </summary>
    private async void PerformHealthCheck(object? state)
    {
        await PerformHealthCheckAsync(_disposalCts.Token);
    }

    /// <summary>
    /// リソースの非同期解放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("接続プールの解放開始");

        try
        {
            _disposalCts.Cancel();
            
            _healthCheckTimer?.Dispose();
            _connectionChannel.Writer.Complete();

            // 全接続の解放
            await foreach (var connection in _connectionChannel.Reader.ReadAllAsync())
            {
                await connection.DisposeAsync();
            }

            _poolSemaphore.Dispose();
            _disposalCts.Dispose();

            _logger.LogInformation("接続プール解放完了: 総作成接続数={TotalCreated}", _totalConnectionsCreated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "接続プール解放中にエラーが発生");
        }
    }
}

/// <summary>
/// 永続接続を表すクラス
/// </summary>
public sealed class PersistentConnection(string id, TcpClient tcpClient, NetworkStream stream,
    StreamReader reader, StreamWriter writer) : IAsyncDisposable
{
    public string Id { get; } = id ?? throw new ArgumentNullException(nameof(id));
    public TcpClient TcpClient { get; } = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
    public NetworkStream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));
    public StreamReader Reader { get; } = reader ?? throw new ArgumentNullException(nameof(reader));
    public StreamWriter Writer { get; } = writer ?? throw new ArgumentNullException(nameof(writer));
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public bool IsDisposed { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        try
        {
            Writer?.Dispose();
            Reader?.Dispose();
            Stream?.Dispose();
            TcpClient?.Dispose();
        }
        catch
        {
            // 例外は無視（リソース解放時）
        }
    }
}

/// <summary>
/// 接続プールのメトリクス情報
/// </summary>
public sealed class ConnectionPoolMetrics
{
    public int ActiveConnections { get; set; }
    public int QueuedConnections { get; set; }
    public int TotalConnectionsCreated { get; set; }
    public int MaxConnections { get; set; }
    public int MinConnections { get; set; }
    public double ConnectionUtilization { get; set; }
    public int AvailableConnections { get; set; }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using System.Collections.Concurrent;
using System.Diagnostics;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// デフォルトONNXセッションファクトリー実装
/// DI Container完全統合とMulti-GPU対応
/// Issue #143 Week 2: 高度なセッション管理とGPU最適化
/// </summary>
public sealed class DefaultOnnxSessionFactory : IOnnxSessionFactory, IDisposable
{
    private readonly IOnnxSessionProvider _sessionProvider;
    private readonly ILogger<DefaultOnnxSessionFactory> _logger;
    private readonly OcrSettings _ocrSettings;
    
    // セッション統計管理
    private readonly ConcurrentDictionary<string, SessionMetrics> _sessionMetrics = new();
    private readonly object _statsLock = new();
    private int _totalSessionsCreated = 0;
    private int _gpuAcceleratedSessions = 0;
    private int _cpuFallbackSessions = 0;
    private int _tdrFallbackCount = 0;
    private DateTime _lastSessionCreatedAt = DateTime.MinValue;
    private readonly List<double> _creationTimes = new();
    
    // リソース管理
    private readonly ConcurrentBag<InferenceSession> _managedSessions = new();
    private bool _disposed = false;

    public DefaultOnnxSessionFactory(
        IOnnxSessionProvider sessionProvider,
        ILogger<DefaultOnnxSessionFactory> logger,
        IOptions<OcrSettings> ocrSettings)
    {
        _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrSettings = ocrSettings?.Value ?? throw new ArgumentNullException(nameof(ocrSettings));
        
        _logger.LogInformation("🏭 DefaultOnnxSessionFactory初期化完了 - DI Container統合");
    }

    public async Task<IOnnxSession> CreateDetectionSessionAsync(GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("🔍 テキスト検出用ONNXセッション作成開始");
            
            var modelPath = _ocrSettings.GpuSettings.DetectionModelPath;
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new InvalidOperationException("検出モデルパスが設定されていません");
            }
            
            var session = await _sessionProvider.CreateSessionAsync(modelPath, gpuInfo, cancellationToken);
            var wrapper = new OnnxSessionWrapper(session, gpuInfo, "TextDetection", modelPath);
            
            _managedSessions.Add(session);
            RecordSessionCreation(stopwatch.ElapsedMilliseconds, gpuInfo, "Detection");
            
            _logger.LogInformation("✅ テキスト検出用ONNXセッション作成完了 - 時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ テキスト検出用ONNXセッション作成失敗");
            throw;
        }
    }

    public async Task<IOnnxSession> CreateRecognitionSessionAsync(GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("📝 テキスト認識用ONNXセッション作成開始");
            
            var modelPath = _ocrSettings.GpuSettings.RecognitionModelPath;
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new InvalidOperationException("認識モデルパスが設定されていません");
            }
            
            var session = await _sessionProvider.CreateSessionAsync(modelPath, gpuInfo, cancellationToken);
            var wrapper = new OnnxSessionWrapper(session, gpuInfo, "TextRecognition", modelPath);
            
            _managedSessions.Add(session);
            RecordSessionCreation(stopwatch.ElapsedMilliseconds, gpuInfo, "Recognition");
            
            _logger.LogInformation("✅ テキスト認識用ONNXセッション作成完了 - 時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ テキスト認識用ONNXセッション作成失敗");
            throw;
        }
    }

    public async Task<IOnnxSession> CreateLanguageIdentificationSessionAsync(GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("🌐 言語識別用ONNXセッション作成開始");
            
            var modelPath = _ocrSettings.GpuSettings.LanguageIdentificationModelPath;
            if (string.IsNullOrEmpty(modelPath))
            {
                _logger.LogWarning("言語識別モデルパスが未設定 - スキップ");
                throw new InvalidOperationException("言語識別モデルパスが設定されていません");
            }
            
            // 言語識別は軽量なのでCPU実行を推奨
            var cpuGpuInfo = new GpuEnvironmentInfo
            {
                GpuName = "CPU Only (Language ID)",
                RecommendedProviders = [ExecutionProvider.CPU]
            };
            
            var session = await _sessionProvider.CreateSessionAsync(modelPath, cpuGpuInfo, cancellationToken);
            var wrapper = new OnnxSessionWrapper(session, cpuGpuInfo, "LanguageIdentification", modelPath);
            
            _managedSessions.Add(session);
            RecordSessionCreation(stopwatch.ElapsedMilliseconds, cpuGpuInfo, "LanguageID");
            
            _logger.LogInformation("✅ 言語識別用ONNXセッション作成完了 - 時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 言語識別用ONNXセッション作成失敗");
            throw;
        }
    }

    public async Task<IOnnxSession> CreateSessionForGpuAsync(string modelPath, int gpuDeviceId, 
        ExecutionProvider[] executionProviders, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("🎯 GPU特化ONNXセッション作成開始 - GPU ID: {GpuDeviceId}", gpuDeviceId);
            
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new ArgumentException("モデルパスが指定されていません", nameof(modelPath));
            }
            
            // 指定GPUに特化した環境情報を作成
            var gpuSpecificInfo = new GpuEnvironmentInfo
            {
                GpuName = $"GPU Device {gpuDeviceId}",
                GpuDeviceId = gpuDeviceId,
                RecommendedProviders = executionProviders?.ToList() ?? [ExecutionProvider.CPU]
            };
            
            var session = await _sessionProvider.CreateSessionAsync(modelPath, gpuSpecificInfo, cancellationToken);
            var wrapper = new OnnxSessionWrapper(session, gpuSpecificInfo, $"GPU{gpuDeviceId}", modelPath);
            
            _managedSessions.Add(session);
            RecordSessionCreation(stopwatch.ElapsedMilliseconds, gpuSpecificInfo, $"GPU{gpuDeviceId}");
            
            _logger.LogInformation("✅ GPU特化ONNXセッション作成完了 - GPU ID: {GpuDeviceId}, 時間: {ElapsedMs}ms", 
                gpuDeviceId, stopwatch.ElapsedMilliseconds);
            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU特化ONNXセッション作成失敗 - GPU ID: {GpuDeviceId}", gpuDeviceId);
            throw;
        }
    }

    public OnnxSessionCreationStats GetCreationStats()
    {
        lock (_statsLock)
        {
            var averageCreationTime = _creationTimes.Count > 0 ? _creationTimes.Average() : 0.0;
            
            return new OnnxSessionCreationStats
            {
                TotalSessionsCreated = _totalSessionsCreated,
                AverageCreationTimeMs = averageCreationTime,
                GpuAcceleratedSessions = _gpuAcceleratedSessions,
                CpuFallbackSessions = _cpuFallbackSessions,
                TdrFallbackCount = _tdrFallbackCount,
                LastSessionCreatedAt = _lastSessionCreatedAt
            };
        }
    }

    public async Task DisposeAsync()
    {
        if (_disposed) return;
        
        _logger.LogInformation("🧹 DefaultOnnxSessionFactory リソース解放開始");
        
        // 管理されているセッションをすべて解放
        var disposeTasks = _managedSessions.Select(session => Task.Run(() =>
        {
            try
            {
                session?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "セッション解放中に警告が発生");
            }
        }));
        
        await Task.WhenAll(disposeTasks);
        
        // 統計情報をログ出力
        var stats = GetCreationStats();
        _logger.LogInformation("📊 ファクトリー統計 - 総セッション数: {Total}, GPU加速: {Gpu}, CPU: {Cpu}, TDR: {Tdr}", 
            stats.TotalSessionsCreated, stats.GpuAcceleratedSessions, stats.CpuFallbackSessions, stats.TdrFallbackCount);
        
        _disposed = true;
        _logger.LogInformation("✅ DefaultOnnxSessionFactory リソース解放完了");
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    private void RecordSessionCreation(double elapsedMs, GpuEnvironmentInfo gpuInfo, string sessionType)
    {
        lock (_statsLock)
        {
            _totalSessionsCreated++;
            _creationTimes.Add(elapsedMs);
            _lastSessionCreatedAt = DateTime.UtcNow;
            
            // GPU使用状況を分析
            var isGpuAccelerated = gpuInfo.RecommendedProviders.Any(p => 
                p == ExecutionProvider.CUDA || 
                p == ExecutionProvider.DirectML || 
                p == ExecutionProvider.TensorRT ||
                p == ExecutionProvider.OpenVINO);
            
            if (isGpuAccelerated)
            {
                _gpuAcceleratedSessions++;
            }
            else
            {
                _cpuFallbackSessions++;
            }
            
            // メトリクス記録
            var key = $"{sessionType}_{gpuInfo.GpuDeviceId}";
            _sessionMetrics.AddOrUpdate(key, 
                new SessionMetrics { CreationTimeMs = elapsedMs, CreatedAt = DateTime.UtcNow },
                (_, existing) => new SessionMetrics { CreationTimeMs = elapsedMs, CreatedAt = DateTime.UtcNow });
        }
    }

    private class SessionMetrics
    {
        public double CreationTimeMs { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
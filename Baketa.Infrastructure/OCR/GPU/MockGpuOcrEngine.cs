using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Drawing;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// GPU OCRエンジン モック実装
/// テスト・開発環境用の仮想GPU加速処理
/// Issue #143 Week 3 Phase 2: 統合システムテスト対応
/// </summary>
public sealed class MockGpuOcrEngine : IGpuOcrEngine
{
    private readonly ILogger<MockGpuOcrEngine> _logger;
    private readonly Random _random = new();
    private bool _disposed = false;
    
    // 統計情報
    private long _totalExecutions = 0;
    private long _successfulExecutions = 0;
    private double _totalExecutionTimeMs = 0;
    private long _peakMemoryUsageMB = 0;
    private long _errorCount = 0;

    public MockGpuOcrEngine(ILogger<MockGpuOcrEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("🎮 MockGpuOcrEngine初期化完了 - 仮想GPU加速OCR開始");
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // モック環境では常にGPU利用可能
        return Task.FromResult(true);
    }

    public async Task<OcrResult> RecognizeTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Interlocked.Increment(ref _totalExecutions);
        
        try
        {
            _logger.LogDebug("🔍 Mock GPU OCR実行開始 - データサイズ: {Size}B", imageData.Length);
            
            // GPU処理時間をシミュレート（200-500ms）
            var processingTimeMs = _random.Next(200, 500);
            await Task.Delay(Math.Min(processingTimeMs / 10, 50), cancellationToken); // テスト環境では短縮
            
            // ランダムなテキスト検出結果を生成
            var detectedTexts = GenerateRandomDetectedTexts();
            
            stopwatch.Stop();
            Interlocked.Increment(ref _successfulExecutions);
            _totalExecutionTimeMs += stopwatch.Elapsed.TotalMilliseconds;
            
            var result = new OcrResult
            {
                DetectedTexts = detectedTexts,
                IsSuccessful = true,
                ProcessingTime = TimeSpan.FromMilliseconds(processingTimeMs),
                Metadata = new Dictionary<string, object>
                {
                    ["ProcessingMode"] = "MockGPU",
                    ["SimulatedTimeMs"] = processingTimeMs,
                    ["ActualTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["GpuAccelerated"] = true
                }
            };
            
            _logger.LogDebug("✅ Mock GPU OCR完了 - 検出数: {Count}, 時間: {Time}ms", 
                detectedTexts.Count, processingTimeMs);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _errorCount);
            _logger.LogError(ex, "❌ Mock GPU OCR失敗");
            
            return new OcrResult
            {
                DetectedTexts = Array.Empty<DetectedText>(),
                IsSuccessful = false,
                ProcessingTime = stopwatch.Elapsed,
                Metadata = new Dictionary<string, object>
                {
                    ["ProcessingMode"] = "MockGPU",
                    ["Error"] = ex.Message
                }
            };
        }
    }

    public Task<GpuEnvironmentInfo> GetGpuEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        var environment = new GpuEnvironmentInfo
        {
            IsDedicatedGpu = true,
            SupportsCuda = true,
            SupportsDirectML = true,
            SupportsOpenCL = false,
            SupportsOpenVINO = false,
            SupportsTensorRT = true,
            AvailableMemoryMB = 20000,
            GpuName = "Mock GPU Device RTX 4090",
            GpuDeviceId = 0,
            ComputeCapability = ComputeCapability.Compute89,
            RecommendedProviders = new[] { ExecutionProvider.CUDA, ExecutionProvider.DirectML },
            MaximumTexture2DDimension = 16384,
            DirectXFeatureLevel = DirectXFeatureLevel.D3D12_2
        };

        return Task.FromResult(environment);
    }

    public Task<bool> UpdateExecutionProviderAsync(
        ExecutionProviderType providerType, 
        string? deviceId = null, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("⚙️ Mock GPU実行プロバイダー更新: {Provider}, デバイス: {Device}", 
            providerType, deviceId ?? "default");
        
        // モック環境では常に成功
        return Task.FromResult(true);
    }

    public Task<long> GetMemoryUsageAsync(CancellationToken cancellationToken = default)
    {
        // メモリ使用量をシミュレート（2-8GB）
        var memoryUsage = _random.Next(2048, 8192);
        _peakMemoryUsageMB = Math.Max(_peakMemoryUsageMB, memoryUsage);
        
        return Task.FromResult((long)memoryUsage);
    }

    public Task<GpuOcrStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var avgExecutionTime = _totalExecutions > 0 ? 
            TimeSpan.FromMilliseconds(_totalExecutionTimeMs / _totalExecutions) : TimeSpan.Zero;
        
        var gpuUtilization = _totalExecutions > 0 ? 
            Math.Min(1.0, (double)_successfulExecutions / _totalExecutions * 0.8) : 0.0;
        
        var statistics = new GpuOcrStatistics
        {
            TotalExecutions = _totalExecutions,
            SuccessfulExecutions = _successfulExecutions,
            AverageExecutionTime = avgExecutionTime,
            PeakMemoryUsageMB = _peakMemoryUsageMB,
            GpuUtilization = gpuUtilization,
            ErrorCount = _errorCount,
            LastUpdated = DateTime.UtcNow
        };

        return Task.FromResult(statistics);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 最終統計ログ
        var successRate = _totalExecutions > 0 ? (double)_successfulExecutions / _totalExecutions : 0.0;
        var avgTime = _totalExecutions > 0 ? _totalExecutionTimeMs / _totalExecutions : 0.0;
        
        _logger.LogInformation("📊 MockGpuOcrEngine統計 - " +
            "総実行: {Total}, 成功: {Success}, 成功率: {Rate:P1}, 平均時間: {AvgTime:F1}ms, エラー: {Errors}",
            _totalExecutions, _successfulExecutions, successRate, avgTime, _errorCount);
        
        _disposed = true;
        _logger.LogInformation("🧹 MockGpuOcrEngine リソース解放完了");
    }

    private List<DetectedText> GenerateRandomDetectedTexts()
    {
        var textCount = _random.Next(1, 6); // 1-5個のテキスト
        var detectedTexts = new List<DetectedText>();

        var sampleTexts = new[]
        {
            "こんにちは世界", "Hello World", "テスト文字列", "Sample Text",
            "日本語テキスト", "GPU Accelerated", "高速処理", "Machine Learning",
            "文字認識", "OCR Engine", "ディープラーニング", "Neural Network"
        };

        for (int i = 0; i < textCount; i++)
        {
            var x = _random.Next(0, 1600);
            var y = _random.Next(0, 900);
            var width = _random.Next(100, 300);
            var height = _random.Next(20, 60);
            var confidence = 0.8 + _random.NextDouble() * 0.2; // 0.8-1.0の信頼度
            var text = sampleTexts[_random.Next(sampleTexts.Length)];

            detectedTexts.Add(new DetectedText
            {
                Text = text,
                Confidence = confidence,
                BoundingBox = new Rectangle(x, y, width, height),
                Language = text.Contains("こんにちは") || text.Contains("日本語") || text.Contains("文字") || text.Contains("ディープ") ? "ja" : "en",
                ProcessingTechnique = OptimizationTechnique.GpuOnly,
                ProcessingTime = TimeSpan.FromMilliseconds(_random.Next(50, 150)),
                Metadata = new Dictionary<string, object>
                {
                    ["MockGenerated"] = true,
                    ["Confidence"] = confidence,
                    ["BoundingBox"] = $"({x},{y},{width},{height})"
                }
            });
        }

        return detectedTexts;
    }
}
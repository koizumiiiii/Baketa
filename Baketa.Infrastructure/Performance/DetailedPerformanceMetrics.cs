using System.Diagnostics;
using System.Text.Json;

namespace Baketa.Infrastructure.Performance;

/// <summary>
/// 詳細パフォーマンスメトリクスコレクター
/// システム全体の詳細な性能情報を収集・分析
/// </summary>
public sealed class DetailedPerformanceMetrics : IDisposable
{
    private readonly Dictionary<string, PerformanceCounter> _performanceCounters = new();
    private readonly Dictionary<string, double> _metricHistory = new();
    private readonly object _metricsLock = new();
    private bool _disposed;

    /// <summary>
    /// ROI処理時間（累積）
    /// </summary>
    public TimeSpan RoiProcessingTime { get; set; }

    /// <summary>
    /// 処理されたROI数（累積）
    /// </summary>
    public int ProcessedRoiCount { get; set; }

    /// <summary>
    /// メモリ使用量（MB）
    /// </summary>
    public long MemoryUsageMB { get; set; }

    /// <summary>
    /// GPU使用率（％）
    /// </summary>
    public double GpuUtilizationPercent { get; set; }

    /// <summary>
    /// CPU使用率（％）
    /// </summary>
    public double CpuUtilizationPercent { get; set; }

    /// <summary>
    /// スレッド数
    /// </summary>
    public int ThreadCount { get; set; }

    /// <summary>
    /// GCコレクション数（Gen 0/1/2）
    /// </summary>
    public (int Gen0, int Gen1, int Gen2) GcCollectionCounts { get; set; }

    /// <summary>
    /// プール効率率（％）
    /// </summary>
    public double PoolEfficiencyPercent { get; set; }

    /// <summary>
    /// 翻訳処理時間（累積）
    /// </summary>
    public TimeSpan TranslationProcessingTime { get; set; }

    /// <summary>
    /// OCR処理時間（累積）
    /// </summary>
    public TimeSpan OcrProcessingTime { get; set; }

    public DetailedPerformanceMetrics()
    {
        InitializePerformanceCounters();
    }

    /// <summary>
    /// パフォーマンスカウンタの初期化
    /// </summary>
    private void InitializePerformanceCounters()
    {
        try
        {
            var processName = Process.GetCurrentProcess().ProcessName;

            // プロセス関連カウンタ
            _performanceCounters["CPU"] = new PerformanceCounter("Process", "% Processor Time", processName);
            _performanceCounters["Memory"] = new PerformanceCounter("Process", "Working Set", processName);
            _performanceCounters["Threads"] = new PerformanceCounter("Process", "Thread Count", processName);
            _performanceCounters["HandleCount"] = new PerformanceCounter("Process", "Handle Count", processName);

            // システム関連カウンタ
            _performanceCounters["SystemCPU"] = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _performanceCounters["AvailableMemory"] = new PerformanceCounter("Memory", "Available MBytes");

            // .NET関連カウンタ
            _performanceCounters["Gen0Collections"] = new PerformanceCounter(".NET CLR Memory", "# Gen 0 Collections", processName);
            _performanceCounters["Gen1Collections"] = new PerformanceCounter(".NET CLR Memory", "# Gen 1 Collections", processName);
            _performanceCounters["Gen2Collections"] = new PerformanceCounter(".NET CLR Memory", "# Gen 2 Collections", processName);
            _performanceCounters["ManagedHeap"] = new PerformanceCounter(".NET CLR Memory", "# Bytes in all Heaps", processName);
        }
        catch (Exception)
        {
            // パフォーマンスカウンタが利用できない環境での代替処理
            // 基本メトリクスのみを使用
        }
    }

    /// <summary>
    /// 現在のメトリクスを収集
    /// </summary>
    public async Task<DetailedPerformanceSnapshot> CollectMetricsAsync()
    {
        var snapshot = new DetailedPerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            RoiProcessingTime = RoiProcessingTime,
            ProcessedRoiCount = ProcessedRoiCount,
            TranslationProcessingTime = TranslationProcessingTime,
            OcrProcessingTime = OcrProcessingTime,
            PoolEfficiencyPercent = PoolEfficiencyPercent
        };

        await Task.Run(() =>
        {
            lock (_metricsLock)
            {
                try
                {
                    // プロセスメトリクス
                    var process = Process.GetCurrentProcess();
                    snapshot.MemoryUsageMB = process.WorkingSet64 / (1024 * 1024);
                    snapshot.ThreadCount = process.Threads.Count;
                    snapshot.CpuUtilizationPercent = CpuUtilizationPercent;

                    // GCメトリクス
                    snapshot.GcCollectionCounts = (
                        GC.CollectionCount(0),
                        GC.CollectionCount(1),
                        GC.CollectionCount(2)
                    );

                    // パフォーマンスカウンタから値を取得
                    if (_performanceCounters.ContainsKey("Memory"))
                    {
                        snapshot.MemoryUsageMB = (long)(_performanceCounters["Memory"].NextValue() / (1024 * 1024));
                    }

                    if (_performanceCounters.ContainsKey("Threads"))
                    {
                        snapshot.ThreadCount = (int)_performanceCounters["Threads"].NextValue();
                    }

                    if (_performanceCounters.ContainsKey("HandleCount"))
                    {
                        snapshot.HandleCount = (int)_performanceCounters["HandleCount"].NextValue();
                    }

                    if (_performanceCounters.ContainsKey("AvailableMemory"))
                    {
                        snapshot.AvailableSystemMemoryMB = (long)_performanceCounters["AvailableMemory"].NextValue();
                    }

                    // GPU使用率（推定値）
                    snapshot.GpuUtilizationPercent = GpuUtilizationPercent;

                    // 効率計算
                    snapshot.RoiProcessingEfficiency = ProcessedRoiCount > 0 ?
                        ProcessedRoiCount / RoiProcessingTime.TotalSeconds : 0;

                    snapshot.OverallPerformanceScore = CalculatePerformanceScore(snapshot);
                }
                catch (Exception)
                {
                    // エラー時も基本メトリクスは提供
                    var process = Process.GetCurrentProcess();
                    snapshot.MemoryUsageMB = process.WorkingSet64 / (1024 * 1024);
                    snapshot.ThreadCount = process.Threads.Count;
                }
            }
        });

        return snapshot;
    }

    /// <summary>
    /// ROI処理メトリクスの更新
    /// </summary>
    public void UpdateRoiMetrics(TimeSpan processingTime, int roiCount)
    {
        lock (_metricsLock)
        {
            RoiProcessingTime = RoiProcessingTime.Add(processingTime);
            ProcessedRoiCount += roiCount;
        }
    }

    /// <summary>
    /// 翻訳処理メトリクスの更新
    /// </summary>
    public void UpdateTranslationMetrics(TimeSpan processingTime)
    {
        lock (_metricsLock)
        {
            TranslationProcessingTime = TranslationProcessingTime.Add(processingTime);
        }
    }

    /// <summary>
    /// OCR処理メトリクスの更新
    /// </summary>
    public void UpdateOcrMetrics(TimeSpan processingTime)
    {
        lock (_metricsLock)
        {
            OcrProcessingTime = OcrProcessingTime.Add(processingTime);
        }
    }

    /// <summary>
    /// GPU使用率の更新
    /// </summary>
    public void UpdateGpuUtilization(double utilizationPercent)
    {
        lock (_metricsLock)
        {
            GpuUtilizationPercent = utilizationPercent;
        }
    }

    /// <summary>
    /// プール効率の更新
    /// </summary>
    public void UpdatePoolEfficiency(double efficiencyPercent)
    {
        lock (_metricsLock)
        {
            PoolEfficiencyPercent = efficiencyPercent;
        }
    }

    /// <summary>
    /// 総合パフォーマンススコアの計算
    /// </summary>
    private double CalculatePerformanceScore(DetailedPerformanceSnapshot snapshot)
    {
        // 0-100スケールでパフォーマンスを評価
        double score = 100;

        // CPU使用率でペナルティ（80%超で減点）
        if (snapshot.CpuUtilizationPercent > 80)
        {
            score -= (snapshot.CpuUtilizationPercent - 80) * 0.5;
        }

        // メモリ使用量でペナルティ（1GB超で減点）
        if (snapshot.MemoryUsageMB > 1024)
        {
            score -= (snapshot.MemoryUsageMB - 1024) / 102.4; // 1GBごとに10点減点
        }

        // プール効率でボーナス
        score += (snapshot.PoolEfficiencyPercent - 70) * 0.2;

        // ROI処理効率でボーナス
        if (snapshot.RoiProcessingEfficiency > 10) // 10ROI/秒以上で高効率
        {
            score += Math.Min(20, snapshot.RoiProcessingEfficiency - 10);
        }

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// メトリクスのJSON形式での出力
    /// </summary>
    public async Task<string> ExportMetricsAsJsonAsync()
    {
        var snapshot = await CollectMetricsAsync().ConfigureAwait(false);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(snapshot, options);
    }

    /// <summary>
    /// パフォーマンス傾向分析
    /// </summary>
    public async Task<PerformanceTrendAnalysis> AnalyzeTrendsAsync(List<DetailedPerformanceSnapshot> historicalData)
    {
        return await Task.Run(() =>
        {
            if (historicalData.Count < 2)
                return new PerformanceTrendAnalysis { IsValid = false };

            var analysis = new PerformanceTrendAnalysis { IsValid = true };

            var latestSnapshot = historicalData.Last();
            var previousSnapshot = historicalData[historicalData.Count - 2];

            // CPU使用率トレンド
            analysis.CpuTrend = latestSnapshot.CpuUtilizationPercent - previousSnapshot.CpuUtilizationPercent;

            // メモリ使用量トレンド
            analysis.MemoryTrend = latestSnapshot.MemoryUsageMB - previousSnapshot.MemoryUsageMB;

            // ROI処理効率トレンド
            analysis.RoiEfficiencyTrend = latestSnapshot.RoiProcessingEfficiency - previousSnapshot.RoiProcessingEfficiency;

            // 総合パフォーマンススコアトレンド
            analysis.OverallScoreTrend = latestSnapshot.OverallPerformanceScore - previousSnapshot.OverallPerformanceScore;

            // 警告レベルの判定
            analysis.WarningLevel = DetermineWarningLevel(analysis, latestSnapshot);

            // 改善推奨事項の生成
            analysis.Recommendations = GenerateRecommendations(analysis, latestSnapshot);

            return analysis;
        });
    }

    /// <summary>
    /// 警告レベルの判定
    /// </summary>
    private WarningLevel DetermineWarningLevel(PerformanceTrendAnalysis analysis, DetailedPerformanceSnapshot snapshot)
    {
        // Critical: 深刻な問題
        if (snapshot.CpuUtilizationPercent > 95 || snapshot.MemoryUsageMB > 2048 ||
            snapshot.OverallPerformanceScore < 30)
        {
            return WarningLevel.Critical;
        }

        // Warning: 注意が必要
        if (analysis.CpuTrend > 10 || analysis.MemoryTrend > 500 ||
            snapshot.OverallPerformanceScore < 60)
        {
            return WarningLevel.Warning;
        }

        // Info: 情報提供
        if (analysis.RoiEfficiencyTrend < -2 || snapshot.PoolEfficiencyPercent < 70)
        {
            return WarningLevel.Info;
        }

        return WarningLevel.Normal;
    }

    /// <summary>
    /// 改善推奨事項の生成
    /// </summary>
    private List<string> GenerateRecommendations(PerformanceTrendAnalysis analysis, DetailedPerformanceSnapshot snapshot)
    {
        var recommendations = new List<string>();

        if (analysis.CpuTrend > 10)
        {
            recommendations.Add("CPU使用率が上昇傾向です。プロセス負荷分散を検討してください。");
        }

        if (analysis.MemoryTrend > 200)
        {
            recommendations.Add("メモリ使用量が急増しています。メモリリークの可能性があります。");
        }

        if (snapshot.PoolEfficiencyPercent < 60)
        {
            recommendations.Add("プール効率が低下しています。プール容量の調整を推奨します。");
        }

        if (analysis.RoiEfficiencyTrend < -5)
        {
            recommendations.Add("ROI処理効率が低下しています。処理アルゴリズムの最適化を検討してください。");
        }

        if (snapshot.GcCollectionCounts.Gen2 > 10)
        {
            recommendations.Add("Gen2 GCが頻発しています。長寿命オブジェクトの管理を改善してください。");
        }

        return recommendations;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var counter in _performanceCounters.Values)
        {
            counter?.Dispose();
        }

        _performanceCounters.Clear();
        _disposed = true;
    }
}

/// <summary>
/// 詳細パフォーマンススナップショット
/// </summary>
public sealed class DetailedPerformanceSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan RoiProcessingTime { get; set; }
    public int ProcessedRoiCount { get; set; }
    public long MemoryUsageMB { get; set; }
    public double GpuUtilizationPercent { get; set; }
    public double CpuUtilizationPercent { get; set; }
    public int ThreadCount { get; set; }
    public (int Gen0, int Gen1, int Gen2) GcCollectionCounts { get; set; }
    public double PoolEfficiencyPercent { get; set; }
    public TimeSpan TranslationProcessingTime { get; set; }
    public TimeSpan OcrProcessingTime { get; set; }
    public double RoiProcessingEfficiency { get; set; }
    public double OverallPerformanceScore { get; set; }
    public int HandleCount { get; set; }
    public long AvailableSystemMemoryMB { get; set; }
}

/// <summary>
/// パフォーマンス傾向分析結果
/// </summary>
public sealed class PerformanceTrendAnalysis
{
    public bool IsValid { get; set; }
    public double CpuTrend { get; set; }
    public long MemoryTrend { get; set; }
    public double RoiEfficiencyTrend { get; set; }
    public double OverallScoreTrend { get; set; }
    public WarningLevel WarningLevel { get; set; }
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// 警告レベル
/// </summary>
public enum WarningLevel
{
    Normal = 0,
    Info = 1,
    Warning = 2,
    Critical = 3
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.ResourceManagement;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// Phase 3: 予測的動的クールダウン計算システム
/// ゲーム負荷パターン学習、GPU温度、VRAM使用率を統合した高度なクールダウン算出
/// </summary>
public sealed class PredictiveCooldownCalculator
{
    private readonly ILogger<PredictiveCooldownCalculator> _logger;
    private readonly GameLoadPatternLearner _gameLoadLearner;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly IOptionsMonitor<PredictiveControlSettings> _settings;
    private readonly VramCapacityDetector _vramDetector;
    
    // 動的学習データ
    private readonly Dictionary<string, CooldownLearningData> _cooldownHistory = new();
    private readonly Queue<CooldownMeasurement> _recentMeasurements = new();
    private readonly object _dataLock = new();
    
    // 予測精度追跡
    private readonly Queue<PredictionAccuracyMeasurement> _accuracyHistory = new();
    private double _currentPredictionAccuracy = 0.7; // デフォルト70%

    public PredictiveCooldownCalculator(
        ILogger<PredictiveCooldownCalculator> logger,
        GameLoadPatternLearner gameLoadLearner,
        IResourceMonitor resourceMonitor,
        IOptionsMonitor<PredictiveControlSettings> settings,
        VramCapacityDetector vramDetector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gameLoadLearner = gameLoadLearner ?? throw new ArgumentNullException(nameof(gameLoadLearner));
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _vramDetector = vramDetector ?? throw new ArgumentNullException(nameof(vramDetector));

        _logger.LogInformation("🕒 [PHASE3] 予測的クールダウン計算システム初期化完了 - 動的VRAM検出統合");
    }

    /// <summary>
    /// Phase 3: 高度な予測的クールダウン時間計算
    /// 複数要素を統合した智能的アルゴリズム
    /// </summary>
    public async Task<TimeSpan> CalculatePredictiveCooldownAsync(
        string? gameProcessName = null,
        SystemLoad? currentSystemLoad = null,
        GpuVramMetrics? currentGpuMetrics = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = _settings.CurrentValue;
            var currentMetrics = currentGpuMetrics ?? await GetCurrentGpuMetricsAsync(cancellationToken).ConfigureAwait(false);
            var systemLoad = currentSystemLoad ?? await GetCurrentSystemLoadAsync(cancellationToken).ConfigureAwait(false);

            // 1. ベースライン計算（従来アルゴリズム）
            var baselineCooldown = CalculateBaselineCooldown(currentMetrics, systemLoad);

            // 2. ゲーム負荷パターン学習による調整
            var gamePatternMultiplier = gameProcessName != null 
                ? CalculateGamePatternMultiplier(gameProcessName, baselineCooldown)
                : 1.0;

            // 3. GPU温度による動的調整
            var temperatureMultiplier = CalculateTemperatureMultiplier(currentMetrics.GpuTemperatureCelsius, settings);

            // 4. VRAM圧迫度による調整
            var vramPressureMultiplier = CalculateVramPressureMultiplier(currentMetrics.GetVramPressureLevel(), settings);

            // 5. 予測精度による信頼性調整
            var confidenceMultiplier = CalculateConfidenceMultiplier(_currentPredictionAccuracy, settings);

            // 6. システム安定性による調整
            var stabilityMultiplier = await CalculateSystemStabilityMultiplierAsync(cancellationToken).ConfigureAwait(false);

            // 最終クールダウン時間の計算
            var totalMultiplier = gamePatternMultiplier * temperatureMultiplier * vramPressureMultiplier * 
                                 confidenceMultiplier * stabilityMultiplier * settings.CooldownBaseMultiplier;
                                 
            var finalCooldown = TimeSpan.FromMilliseconds(baselineCooldown.TotalMilliseconds * totalMultiplier);

            // 範囲制限（最小500ms、最大30秒）
            finalCooldown = TimeSpan.FromMilliseconds(Math.Max(500, Math.Min(30000, finalCooldown.TotalMilliseconds)));

            // 学習データ記録
            RecordCooldownMeasurement(gameProcessName, finalCooldown, currentMetrics, systemLoad);

            _logger.LogDebug("🎯 [PHASE3] 予測的クールダウン計算完了: {FinalCooldown}ms " +
                "(ベース={Baseline}ms, ゲーム係数={Game:F2}, 温度係数={Temp:F2}, VRAM係数={Vram:F2}, " +
                "信頼性係数={Confidence:F2}, 安定性係数={Stability:F2})",
                finalCooldown.TotalMilliseconds, baselineCooldown.TotalMilliseconds,
                gamePatternMultiplier, temperatureMultiplier, vramPressureMultiplier,
                confidenceMultiplier, stabilityMultiplier);

            return finalCooldown;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] 予測的クールダウン計算エラー");
            return TimeSpan.FromSeconds(5); // フェイルセーフ値
        }
    }

    /// <summary>
    /// クールダウン精度評価とフィードバック学習
    /// </summary>
    public async Task RecordCooldownEffectivenessAsync(
        TimeSpan appliedCooldown,
        bool wasEffective,
        string? gameProcessName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accuracyMeasurement = new PredictionAccuracyMeasurement(
                AppliedCooldown: appliedCooldown,
                WasEffective: wasEffective,
                GameProcessName: gameProcessName,
                Timestamp: DateTime.UtcNow
            );

            lock (_dataLock)
            {
                _accuracyHistory.Enqueue(accuracyMeasurement);
                
                // 履歴サイズ制限（最新100件）
                while (_accuracyHistory.Count > 100)
                    _accuracyHistory.Dequeue();
                
                // 予測精度を動的更新
                UpdatePredictionAccuracy();
            }

            // ゲーム固有の学習データ更新
            if (gameProcessName != null)
            {
                await UpdateGameCooldownLearningAsync(gameProcessName, appliedCooldown, wasEffective, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogTrace("📊 [PHASE3] クールダウン効果測定記録: {Cooldown}ms, 効果={Effective}, ゲーム={Game}",
                appliedCooldown.TotalMilliseconds, wasEffective, gameProcessName ?? "不明");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] クールダウン効果測定記録エラー");
        }
    }

    /// <summary>
    /// 現在の予測精度統計を取得
    /// </summary>
    public CooldownPredictionStatistics GetPredictionStatistics()
    {
        lock (_dataLock)
        {
            var totalMeasurements = _accuracyHistory.Count;
            var effectiveMeasurements = _accuracyHistory.Count(m => m.WasEffective);
            var averageEffectiveness = totalMeasurements > 0 ? (double)effectiveMeasurements / totalMeasurements : 0.0;
            
            var gameSpecificAccuracy = _cooldownHistory
                .Where(kvp => kvp.Value.TotalMeasurements > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.EffectiveMeasurements / (double)kvp.Value.TotalMeasurements
                );

            return new CooldownPredictionStatistics(
                CurrentPredictionAccuracy: _currentPredictionAccuracy,
                TotalMeasurements: totalMeasurements,
                AverageEffectiveness: averageEffectiveness,
                GameSpecificAccuracy: gameSpecificAccuracy,
                RecentTrend: CalculateAccuracyTrend()
            );
        }
    }

    private static TimeSpan CalculateBaselineCooldown(GpuVramMetrics gpuMetrics, SystemLoad systemLoad)
    {
        // システム負荷に基づく基本クールダウン
        var loadFactor = (systemLoad.CpuUsagePercent + systemLoad.MemoryUsagePercent + 
                         systemLoad.GpuUsagePercent + systemLoad.VramUsagePercent) / 4.0;

        // 非線形スケーリング（高負荷時により敏感に反応）
        var normalizedLoad = Math.Max(0, Math.Min(100, loadFactor));
        var scaledLoad = Math.Pow(normalizedLoad / 100.0, 1.3); // 1.3乗でカーブ調整
        
        return TimeSpan.FromMilliseconds(1000 + scaledLoad * 4000); // 1-5秒の範囲
    }

    private double CalculateGamePatternMultiplier(string gameProcessName, TimeSpan baselineCooldown)
    {
        var gamePattern = _gameLoadLearner.GetLearnedPattern(gameProcessName);
        
        if (gamePattern == null || gamePattern.LearningSessionCount < _settings.CurrentValue.MinLearningSessionCount)
            return 1.0; // 学習データ不足

        lock (_dataLock)
        {
            if (!_cooldownHistory.TryGetValue(gameProcessName, out var learningData))
                return 1.0;

            // ゲーム固有の平均効果的クールダウンと現在のベースラインを比較
            if (learningData.TotalMeasurements > 0)
            {
                var historicalAverage = learningData.AverageEffectiveCooldown;
                var ratio = baselineCooldown.TotalMilliseconds / historicalAverage.TotalMilliseconds;
                
                // 適度な調整範囲（0.5倍〜2.0倍）
                return Math.Max(0.5, Math.Min(2.0, ratio));
            }
        }

        // 負荷変動パターンによる調整
        var loadVariability = gamePattern.PeakLoad - gamePattern.AverageLoad;
        return loadVariability switch
        {
            < 20 => 0.8,  // 安定したゲーム：短縮
            < 40 => 1.0,  // 通常のゲーム：標準
            < 60 => 1.3,  // 不安定なゲーム：延長
            _ => 1.6      // 非常に不安定：大幅延長
        };
    }

    private static double CalculateTemperatureMultiplier(double temperatureCelsius, PredictiveControlSettings settings)
    {
        return temperatureCelsius switch
        {
            < 60 => 1.0, // 正常温度
            < 70 => 1.0 + settings.TemperatureAdjustmentMultiplier * 0.3,
            < 80 => 1.0 + settings.TemperatureAdjustmentMultiplier * 0.6,
            < 90 => 1.0 + settings.TemperatureAdjustmentMultiplier * 1.0,
            _ => 1.0 + settings.TemperatureAdjustmentMultiplier * 1.5 // 高温時は大幅延長
        };
    }

    private static double CalculateVramPressureMultiplier(VramPressureLevel pressureLevel, PredictiveControlSettings settings)
    {
        return pressureLevel switch
        {
            VramPressureLevel.Low => 1.0,
            VramPressureLevel.Moderate => 1.0 + settings.VramPressureAdjustmentMultiplier * 0.4,
            VramPressureLevel.High => 1.0 + settings.VramPressureAdjustmentMultiplier * 0.8,
            VramPressureLevel.Critical => 1.0 + settings.VramPressureAdjustmentMultiplier * 1.2,
            VramPressureLevel.Emergency => 1.0 + settings.VramPressureAdjustmentMultiplier * 1.6,
            _ => 1.0
        };
    }

    private static double CalculateConfidenceMultiplier(double predictionAccuracy, PredictiveControlSettings settings)
    {
        if (predictionAccuracy < settings.MinPredictionAccuracy)
        {
            // 予測精度が低い場合は保守的にクールダウンを延長
            var deficiency = settings.MinPredictionAccuracy - predictionAccuracy;
            return 1.0 + deficiency * 2.0; // 最大200%延長
        }
        
        // 予測精度が十分な場合は標準または短縮
        return Math.Max(0.8, 1.0 - (predictionAccuracy - settings.MinPredictionAccuracy) * 0.5);
    }

    private async Task<double> CalculateSystemStabilityMultiplierAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 過去の測定値から安定性を評価
            lock (_dataLock)
            {
                if (_recentMeasurements.Count < 3)
                    return 1.0; // データ不足

                var recentCooldowns = _recentMeasurements.TakeLast(5)
                    .Select(m => m.AppliedCooldown.TotalMilliseconds)
                    .ToArray();

                if (recentCooldowns.Length < 2)
                    return 1.0;

                // 変動係数（CV）による安定性評価
                var mean = recentCooldowns.Average();
                var variance = recentCooldowns.Select(x => Math.Pow(x - mean, 2)).Average();
                var standardDeviation = Math.Sqrt(variance);
                var coefficientOfVariation = mean > 0 ? standardDeviation / mean : 0;

                // CV値による調整（不安定な場合はクールダウンを延長）
                return coefficientOfVariation switch
                {
                    < 0.1 => 0.9,   // 非常に安定：短縮
                    < 0.2 => 1.0,   // 安定：標準
                    < 0.4 => 1.2,   // やや不安定：延長
                    _ => 1.4        // 不安定：大幅延長
                };
            }
        }
        catch
        {
            return 1.0; // エラー時はデフォルト
        }
    }

    private async Task<GpuVramMetrics> GetCurrentGpuMetricsAsync(CancellationToken cancellationToken)
    {
        var systemMetrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
        var vramCapacityInfo = await _vramDetector.GetVramCapacityInfoAsync(cancellationToken).ConfigureAwait(false);
        
        // ResourceMetricsをGpuVramMetricsに変換（動的VRAM容量使用）
        return new GpuVramMetrics(
            GpuUtilizationPercent: systemMetrics.GpuUsagePercent ?? 0.0,
            VramUsagePercent: vramCapacityInfo.UsagePercent,
            VramUsedMB: vramCapacityInfo.UsedCapacityMB,
            VramTotalMB: vramCapacityInfo.TotalCapacityMB,
            GpuTemperatureCelsius: systemMetrics.GpuTemperature ?? 0.0,
            PowerUsageWatts: 0.0,
            GpuClockMhz: 0,
            MemoryClockMhz: 0,
            IsOptimalForProcessing: vramCapacityInfo.UsagePercent < 80.0 && (systemMetrics.GpuUsagePercent ?? 0.0) < 80.0,
            MeasuredAt: DateTime.UtcNow
        );
    }

    private async Task<SystemLoad> GetCurrentSystemLoadAsync(CancellationToken cancellationToken)
    {
        var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
        var vramUsagePercent = await _vramDetector.CalculateVramUsagePercentAsync(cancellationToken).ConfigureAwait(false);
        
        return new SystemLoad(
            CpuUsagePercent: metrics.CpuUsagePercent,
            MemoryUsagePercent: metrics.MemoryUsagePercent,
            GpuUsagePercent: metrics.GpuUsagePercent ?? 0.0,
            VramUsagePercent: vramUsagePercent,
            ActiveProcessCount: metrics.ProcessCount,
            IsGamingActive: false, // TODO: 実際のゲーム検出ロジック
            MeasuredAt: DateTime.UtcNow
        );
    }

    private void RecordCooldownMeasurement(
        string? gameProcessName, 
        TimeSpan appliedCooldown, 
        GpuVramMetrics gpuMetrics, 
        SystemLoad systemLoad)
    {
        var measurement = new CooldownMeasurement(
            GameProcessName: gameProcessName,
            AppliedCooldown: appliedCooldown,
            GpuTemperature: gpuMetrics.GpuTemperatureCelsius,
            VramUsagePercent: gpuMetrics.VramUsagePercent,
            SystemLoadLevel: systemLoad.GetLoadLevel(),
            Timestamp: DateTime.UtcNow
        );

        lock (_dataLock)
        {
            _recentMeasurements.Enqueue(measurement);
            
            // 履歴サイズ制限（最新50件）
            while (_recentMeasurements.Count > 50)
                _recentMeasurements.Dequeue();
        }
    }

    private async Task UpdateGameCooldownLearningAsync(
        string gameProcessName, 
        TimeSpan appliedCooldown, 
        bool wasEffective,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // 非同期インターフェース対応

        lock (_dataLock)
        {
            if (!_cooldownHistory.TryGetValue(gameProcessName, out var learningData))
            {
                learningData = new CooldownLearningData();
                _cooldownHistory[gameProcessName] = learningData;
            }

            learningData.TotalMeasurements++;
            if (wasEffective)
            {
                learningData.EffectiveMeasurements++;
                learningData.TotalEffectiveCooldownMs += appliedCooldown.TotalMilliseconds;
                learningData.AverageEffectiveCooldown = TimeSpan.FromMilliseconds(
                    learningData.TotalEffectiveCooldownMs / learningData.EffectiveMeasurements);
            }
        }
    }

    private void UpdatePredictionAccuracy()
    {
        if (_accuracyHistory.Count < 10) return; // 最低10件必要

        var recentAccuracy = _accuracyHistory.TakeLast(20)
            .Count(m => m.WasEffective) / 20.0;

        // 徐々に更新（移動平均）
        _currentPredictionAccuracy = (_currentPredictionAccuracy * 0.8) + (recentAccuracy * 0.2);
    }

    private double CalculateAccuracyTrend()
    {
        if (_accuracyHistory.Count < 20) return 0.0;

        var recent = _accuracyHistory.TakeLast(10).Count(m => m.WasEffective) / 10.0;
        var older = _accuracyHistory.Skip(_accuracyHistory.Count - 20).Take(10).Count(m => m.WasEffective) / 10.0;

        return recent - older; // 正の値：向上傾向、負の値：悪化傾向
    }
}

/// <summary>
/// クールダウン学習データ
/// </summary>
internal sealed class CooldownLearningData
{
    public int TotalMeasurements { get; set; }
    public int EffectiveMeasurements { get; set; }
    public double TotalEffectiveCooldownMs { get; set; }
    public TimeSpan AverageEffectiveCooldown { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// クールダウン測定データ
/// </summary>
internal sealed record CooldownMeasurement(
    string? GameProcessName,
    TimeSpan AppliedCooldown,
    double GpuTemperature,
    double VramUsagePercent,
    SystemLoadLevel SystemLoadLevel,
    DateTime Timestamp
);

/// <summary>
/// 予測精度測定データ
/// </summary>
internal sealed record PredictionAccuracyMeasurement(
    TimeSpan AppliedCooldown,
    bool WasEffective,
    string? GameProcessName,
    DateTime Timestamp
);

/// <summary>
/// クールダウン予測統計情報
/// </summary>
public sealed record CooldownPredictionStatistics(
    double CurrentPredictionAccuracy,
    int TotalMeasurements,
    double AverageEffectiveness,
    Dictionary<string, double> GameSpecificAccuracy,
    double RecentTrend
);
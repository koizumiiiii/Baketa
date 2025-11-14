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
/// Phase 3: ゲーム負荷パターン学習システム
/// 特定ゲームの負荷パターンを学習し、予測的リソース制御を実現
/// </summary>
public sealed class GameLoadPatternLearner : IDisposable
{
    private readonly ILogger<GameLoadPatternLearner> _logger;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly IOptionsMonitor<PredictiveControlSettings> _settings;

    // ゲーム別学習データストレージ
    private readonly Dictionary<string, GameLearningSession> _activeSessions = new();
    private readonly Dictionary<string, GameLoadPattern> _learnedPatterns = new();
    private readonly object _dataLock = new();

    // パフォーマンス追跡
    private readonly Dictionary<string, List<LoadMeasurement>> _recentMeasurements = new();
    private readonly System.Threading.Timer _cleanupTimer;

    private bool _disposed;

    public GameLoadPatternLearner(
        ILogger<GameLoadPatternLearner> logger,
        IResourceMonitor resourceMonitor,
        IOptionsMonitor<PredictiveControlSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 定期クリーンアップタスク（1時間ごと）
        _cleanupTimer = new System.Threading.Timer(PerformCleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        _logger.LogInformation("🧠 [PHASE3] ゲーム負荷パターン学習システム初期化完了");
    }

    /// <summary>
    /// ゲームセッション開始
    /// </summary>
    public async Task StartGameSessionAsync(string gameProcessName, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        lock (_dataLock)
        {
            if (_activeSessions.ContainsKey(gameProcessName))
            {
                _logger.LogWarning("⚠️ [PHASE3] 既にアクティブなゲームセッション: {GameName}", gameProcessName);
                return;
            }

            var session = new GameLearningSession(gameProcessName, DateTime.UtcNow);
            _activeSessions[gameProcessName] = session;
            _recentMeasurements[gameProcessName] = [];
        }

        _logger.LogInformation("🎮 [PHASE3] ゲーム負荷学習開始: {GameName}", gameProcessName);
        await Task.CompletedTask; // 非同期インターフェース対応
    }

    /// <summary>
    /// ゲームセッション終了と学習データ統合
    /// </summary>
    public async Task EndGameSessionAsync(string gameProcessName, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        GameLearningSession? session = null;
        List<LoadMeasurement>? measurements = null;

        lock (_dataLock)
        {
            if (!_activeSessions.TryGetValue(gameProcessName, out session))
            {
                _logger.LogWarning("⚠️ [PHASE3] 存在しないゲームセッション終了要求: {GameName}", gameProcessName);
                return;
            }

            _activeSessions.Remove(gameProcessName);
            _recentMeasurements.TryGetValue(gameProcessName, out measurements);
            _recentMeasurements.Remove(gameProcessName);
        }

        if (session != null && measurements?.Count > 0)
        {
            var pattern = await AnalyzeAndLearnFromSession(session, measurements, cancellationToken).ConfigureAwait(false);

            lock (_dataLock)
            {
                _learnedPatterns[gameProcessName] = pattern;
            }

            _logger.LogInformation("🧠 [PHASE3] ゲーム負荷学習完了: {GameName}, 測定点数: {MeasurementCount}, 学習セッション数: {SessionCount}",
                gameProcessName, measurements.Count, pattern.LearningSessionCount);
        }
    }

    /// <summary>
    /// ゲーム実行中の負荷測定記録
    /// </summary>
    public async Task RecordGameLoadAsync(string gameProcessName, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        lock (_dataLock)
        {
            if (!_activeSessions.ContainsKey(gameProcessName))
                return; // セッションがアクティブでない
        }

        try
        {
            var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
            var measurement = CreateLoadMeasurement(metrics);

            lock (_dataLock)
            {
                if (_recentMeasurements.TryGetValue(gameProcessName, out var measurements))
                {
                    measurements.Add(measurement);

                    // メモリ効率のため最新1000件に制限
                    if (measurements.Count > 1000)
                    {
                        measurements.RemoveRange(0, 200); // 古い200件を削除
                    }
                }
            }

            if (_settings.CurrentValue.EnableGameLoadLearning && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("📊 [PHASE3] 負荷測定記録: {GameName}, 総合負荷: {CompositeLoad:F1}%, GPU: {GpuLoad:F1}%, VRAM: {VramLoad:F1}%",
                    gameProcessName, measurement.CompositeLoad, measurement.GpuUsage, measurement.VramUsage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] ゲーム負荷測定記録エラー: {GameName}", gameProcessName);
        }
    }

    /// <summary>
    /// 学習済みパターンから予測負荷を取得
    /// </summary>
    public GameLoadPattern? GetLearnedPattern(string gameProcessName)
    {
        if (_disposed) return null;

        lock (_dataLock)
        {
            return _learnedPatterns.TryGetValue(gameProcessName, out var pattern) ? pattern : null;
        }
    }

    /// <summary>
    /// 指定ゲームの予測負荷値を計算
    /// </summary>
    public double GetPredictedLoad(string gameProcessName, TimeSpan gameTime)
    {
        var pattern = GetLearnedPattern(gameProcessName);

        return pattern?.GetPredictedLoad(gameTime) ?? 50.0; // デフォルト50%負荷
    }

    /// <summary>
    /// 全学習済みパターンの統計情報を取得
    /// </summary>
    public GameLearningStatistics GetLearningStatistics()
    {
        lock (_dataLock)
        {
            var totalPatterns = _learnedPatterns.Count;
            var totalSessions = _learnedPatterns.Values.Sum(p => p.LearningSessionCount);
            var averageAccuracy = _learnedPatterns.Values.Count > 0
                ? _learnedPatterns.Values.Where(p => p.LearningSessionCount >= _settings.CurrentValue.MinLearningSessionCount)
                                         .DefaultIfEmpty()
                                         .Average(p => p?.AverageLoad ?? 0)
                : 0;

            return new GameLearningStatistics(
                TotalLearnedGames: totalPatterns,
                TotalLearningSession: totalSessions,
                AverageLoadAccuracy: averageAccuracy,
                ActiveSessions: _activeSessions.Count
            );
        }
    }

    private async Task<GameLoadPattern> AnalyzeAndLearnFromSession(
        GameLearningSession session,
        List<LoadMeasurement> measurements,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // 非同期インターフェース対応

        var settings = _settings.CurrentValue;
        var gameName = session.GameProcessName;
        var sessionDuration = DateTime.UtcNow - session.StartTime;

        // 既存パターンとの統合
        var existingPattern = GetLearnedPattern(gameName);
        var newSessionCount = (existingPattern?.LearningSessionCount ?? 0) + 1;

        // 負荷プロファイル生成（時間経過 → 負荷率のマップ）
        var loadProfile = GenerateLoadProfile(measurements, sessionDuration, settings);

        // 統計値計算
        var averageLoad = measurements.Average(m => m.CompositeLoad);
        var peakLoad = measurements.Max(m => m.CompositeLoad);
        var predictedPeakTime = FindPredictedPeakTime(measurements, sessionDuration);

        var newPattern = new GameLoadPattern(
            GameProcessName: gameName,
            LoadProfile: loadProfile,
            AverageLoad: averageLoad,
            PeakLoad: peakLoad,
            PredictedPeakTime: predictedPeakTime,
            LearningSessionCount: newSessionCount
        );

        _logger.LogDebug("📈 [PHASE3] セッション分析完了: {GameName}, 平均負荷: {AvgLoad:F1}%, ピーク負荷: {PeakLoad:F1}%, 予測ピーク時刻: {PeakTime}",
            gameName, averageLoad, peakLoad, predictedPeakTime);

        return newPattern;
    }

    private static Dictionary<TimeSpan, double> GenerateLoadProfile(
        List<LoadMeasurement> measurements,
        TimeSpan sessionDuration,
        PredictiveControlSettings settings)
    {
        var profile = new Dictionary<TimeSpan, double>();

        if (measurements.Count == 0) return profile;

        // セッションを時間帯に分割してプロファイル作成
        var timeSlots = Math.Max(1, Math.Min(60, (int)(sessionDuration.TotalMinutes / 2))); // 2分間隔、最大60スロット
        var slotDuration = sessionDuration.TotalMilliseconds / timeSlots;

        for (int i = 0; i < timeSlots; i++)
        {
            var slotStart = TimeSpan.FromMilliseconds(i * slotDuration);
            var slotEnd = TimeSpan.FromMilliseconds((i + 1) * slotDuration);

            var slotMeasurements = measurements.Where(m =>
            {
                var measurementTime = m.Timestamp - measurements.First().Timestamp;
                return measurementTime >= slotStart && measurementTime < slotEnd;
            }).ToList();

            if (slotMeasurements.Count > 0)
            {
                // 平滑化処理
                var smoothingWindowSize = settings.LoadSmoothingWindowSize;
                var smoothedLoad = ApplySmoothing(slotMeasurements.Select(m => m.CompositeLoad), smoothingWindowSize);
                profile[slotStart] = smoothedLoad;
            }
        }

        return profile;
    }

    private static double ApplySmoothing(IEnumerable<double> values, int windowSize)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0) return 0.0;

        if (valuesList.Count <= windowSize)
            return valuesList.Average();

        // 移動平均による平滑化
        var smoothedValues = new List<double>();
        for (int i = 0; i <= valuesList.Count - windowSize; i++)
        {
            var windowAverage = valuesList.Skip(i).Take(windowSize).Average();
            smoothedValues.Add(windowAverage);
        }

        return smoothedValues.Average();
    }

    private static TimeSpan FindPredictedPeakTime(List<LoadMeasurement> measurements, TimeSpan sessionDuration)
    {
        if (measurements.Count == 0) return TimeSpan.Zero;

        // ピーク負荷時刻を特定
        var peakMeasurement = measurements.OrderByDescending(m => m.CompositeLoad).First();
        var startTime = measurements.First().Timestamp;

        return peakMeasurement.Timestamp - startTime;
    }

    private static LoadMeasurement CreateLoadMeasurement(ResourceMetrics metrics)
    {
        var cpuLoad = metrics.CpuUsagePercent;
        var memoryLoad = metrics.MemoryUsagePercent;
        var gpuLoad = metrics.GpuUsagePercent ?? 0.0;
        var vramLoad = metrics.GpuMemoryUsageMB.HasValue
            ? Math.Min(100.0, (double)metrics.GpuMemoryUsageMB.Value / 8192.0 * 100.0) // 8GB仮定
            : 0.0;

        // 総合負荷スコア（重み付き平均）
        var compositeLoad = (cpuLoad * 0.3 + memoryLoad * 0.2 + gpuLoad * 0.25 + vramLoad * 0.25);

        return new LoadMeasurement(
            CpuUsage: cpuLoad,
            MemoryUsage: memoryLoad,
            GpuUsage: gpuLoad,
            VramUsage: vramLoad,
            CompositeLoad: compositeLoad,
            Timestamp: DateTime.UtcNow
        );
    }

    private void PerformCleanup(object? state)
    {
        if (_disposed) return;

        try
        {
            var settings = _settings.CurrentValue;
            var cutoffTime = DateTime.UtcNow - settings.LoadPatternRetentionPeriod;

            lock (_dataLock)
            {
                // 期限切れデータのクリーンアップ
                var expiredGames = _learnedPatterns.Keys.ToList();

                foreach (var gameName in expiredGames)
                {
                    // Note: 実装では永続化されたデータの最終更新時刻をチェックすべき
                    // ここでは簡略化のため、学習セッション数が十分でない古いデータを削除
                    if (_learnedPatterns[gameName].LearningSessionCount < settings.MinLearningSessionCount)
                    {
                        _learnedPatterns.Remove(gameName);
                        _logger.LogInformation("🧹 [PHASE3] 学習データクリーンアップ: {GameName} (学習不足)", gameName);
                    }
                }
            }

            _logger.LogDebug("🧹 [PHASE3] ゲーム負荷パターンデータクリーンアップ完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] クリーンアップエラー");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cleanupTimer?.Dispose();

        lock (_dataLock)
        {
            _activeSessions.Clear();
            _recentMeasurements.Clear();
            _learnedPatterns.Clear();
        }

        _logger.LogInformation("🔄 [PHASE3] ゲーム負荷パターン学習システム終了");
    }
}

/// <summary>
/// ゲーム学習セッション情報
/// </summary>
internal sealed record GameLearningSession(
    string GameProcessName,
    DateTime StartTime
);

/// <summary>
/// 負荷測定データ
/// </summary>
internal sealed record LoadMeasurement(
    double CpuUsage,
    double MemoryUsage,
    double GpuUsage,
    double VramUsage,
    double CompositeLoad,
    DateTime Timestamp
);

/// <summary>
/// ゲーム負荷学習統計情報
/// </summary>
public sealed record GameLearningStatistics(
    int TotalLearnedGames,
    int TotalLearningSession,
    double AverageLoadAccuracy,
    int ActiveSessions
);

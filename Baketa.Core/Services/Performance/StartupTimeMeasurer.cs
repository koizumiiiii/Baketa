using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Performance;

/// <summary>
/// アプリケーション起動時間を測定・分析するサービス
/// 2分間の起動時間ボトルネック特定に特化
/// </summary>
public sealed class StartupTimeMeasurer(ILogger<StartupTimeMeasurer> logger)
{
    private readonly ILogger<StartupTimeMeasurer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, Stopwatch> _activeTimers = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _completedTimings = new();
    private readonly Stopwatch _totalTimer = new();

    /// <summary>
    /// 全体の起動時間測定開始
    /// </summary>
    public void StartTotal()
    {
        _totalTimer.Start();
        _logger.LogInformation("🚀 [STARTUP] アプリケーション起動時間測定開始 - {Timestamp}", DateTime.Now);
    }

    /// <summary>
    /// 特定フェーズの測定開始
    /// </summary>
    /// <param name="phase">フェーズ名</param>
    public void StartPhase(string phase)
    {
        var timer = Stopwatch.StartNew();
        _activeTimers.TryAdd(phase, timer);

        _logger.LogInformation("⏱️ [STARTUP-PHASE] {Phase} 開始 - {Timestamp}", phase, DateTime.Now);
    }

    /// <summary>
    /// 特定フェーズの測定終了
    /// </summary>
    /// <param name="phase">フェーズ名</param>
    public void EndPhase(string phase)
    {
        if (_activeTimers.TryRemove(phase, out var timer))
        {
            timer.Stop();
            var elapsed = timer.Elapsed;
            _completedTimings.TryAdd(phase, elapsed);

            _logger.LogInformation("✅ [STARTUP-PHASE] {Phase} 完了 - 実行時間: {ElapsedMs}ms",
                phase, elapsed.TotalMilliseconds);

            // 10秒以上かかったフェーズを警告
            if (elapsed.TotalSeconds >= 10)
            {
                _logger.LogWarning("🐌 [STARTUP-SLOW] {Phase} が {ElapsedSec}秒かかりました - ボトルネック候補",
                    phase, elapsed.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// 全体の起動時間測定終了と結果出力
    /// </summary>
    public void EndTotal()
    {
        _totalTimer.Stop();
        var totalTime = _totalTimer.Elapsed;

        _logger.LogInformation("🏁 [STARTUP] アプリケーション起動完了 - 総時間: {TotalMs}ms ({TotalSec}秒)",
            totalTime.TotalMilliseconds, totalTime.TotalSeconds);

        // 詳細分析結果出力
        OutputDetailedAnalysis(totalTime);
    }

    /// <summary>
    /// 詳細な起動時間分析結果を出力
    /// </summary>
    private void OutputDetailedAnalysis(TimeSpan totalTime)
    {
        if (_completedTimings.IsEmpty)
        {
            _logger.LogWarning("⚠️ [STARTUP-ANALYSIS] フェーズ測定データがありません");
            return;
        }

        _logger.LogInformation("📊 [STARTUP-ANALYSIS] 起動時間詳細分析結果:");
        _logger.LogInformation("================================================");

        // フェーズ別時間を降順でソート
        var sortedPhases = _completedTimings
            .OrderByDescending(kvp => kvp.Value.TotalMilliseconds)
            .ToList();

        double totalMeasuredMs = sortedPhases.Sum(kvp => kvp.Value.TotalMilliseconds);

        foreach (var (phase, elapsed) in sortedPhases)
        {
            double percentage = (elapsed.TotalMilliseconds / totalTime.TotalMilliseconds) * 100;
            string status = elapsed.TotalSeconds >= 10 ? "🔴 SLOW" :
                           elapsed.TotalSeconds >= 5 ? "🟡 MEDIUM" : "🟢 FAST";

            _logger.LogInformation("  {Status} {Phase}: {ElapsedMs}ms ({Percentage:F1}%)",
                status, phase, elapsed.TotalMilliseconds, percentage);
        }

        _logger.LogInformation("================================================");

        // 未測定時間があるかチェック
        double unmeasuredMs = totalTime.TotalMilliseconds - totalMeasuredMs;
        if (unmeasuredMs > 1000) // 1秒以上の未測定時間
        {
            double unmeasuredPercentage = (unmeasuredMs / totalTime.TotalMilliseconds) * 100;
            _logger.LogWarning("⚠️ [STARTUP-ANALYSIS] 未測定時間: {UnmeasuredMs}ms ({UnmeasuredPercentage:F1}%) - 追加調査が必要",
                unmeasuredMs, unmeasuredPercentage);
        }

        // 最も時間のかかったフェーズを特定
        var slowestPhase = sortedPhases.FirstOrDefault();
        if (slowestPhase.Value.TotalSeconds >= 10)
        {
            _logger.LogError("🎯 [STARTUP-BOTTLENECK] 最大のボトルネック: {Phase} ({ElapsedSec:F1}秒)",
                slowestPhase.Key, slowestPhase.Value.TotalSeconds);
        }
    }

    /// <summary>
    /// 現在の測定状況を取得（デバッグ用）
    /// </summary>
    public Dictionary<string, TimeSpan> GetCurrentTimings()
    {
        return new Dictionary<string, TimeSpan>(_completedTimings);
    }
}

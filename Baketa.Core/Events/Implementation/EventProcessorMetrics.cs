using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Events.Implementation
{
    /// <summary>
    /// イベントプロセッサのパフォーマンスメトリクス
    /// </summary>
    // プライマリコンストラクターの使用を拒否（IDE0290）
    public class EventProcessorMetrics
    {
        private readonly ILogger<EventProcessorMetrics>? _logger;
        // コレクション初期化の簡素化を拒否（IDE0090）
        private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _processingTimes = new ConcurrentDictionary<string, ConcurrentQueue<double>>();
        private readonly ConcurrentDictionary<string, long> _invocationCounts = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, long> _errorCounts = new ConcurrentDictionary<string, long>();

        /// <summary>
        /// イベントプロセッサのパフォーマンスメトリクスを初期化します
        /// </summary>
        /// <param name="logger">ロガー（オプション）</param>
        public EventProcessorMetrics(ILogger<EventProcessorMetrics>? logger = null)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// プロセッサのパフォーマンス測定を開始
        /// </summary>
        /// <param name="processorType">プロセッサタイプ</param>
        /// <param name="eventType">イベントタイプ</param>
        /// <returns>測定用ストップウォッチ</returns>
        public Stopwatch StartMeasurement(Type processorType, Type eventType)
        {
            ArgumentNullException.ThrowIfNull(processorType);
            ArgumentNullException.ThrowIfNull(eventType);
            
            var key = GetKey(processorType, eventType);
            _invocationCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            
            return Stopwatch.StartNew();
        }
        
        /// <summary>
        /// プロセッサのパフォーマンス測定を終了
        /// </summary>
        /// <param name="stopwatch">測定用ストップウォッチ</param>
        /// <param name="processorType">プロセッサタイプ</param>
        /// <param name="eventType">イベントタイプ</param>
        /// <param name="isSuccess">処理成功フラグ</param>
        public void EndMeasurement(Stopwatch stopwatch, Type processorType, Type eventType, bool isSuccess)
        {
            ArgumentNullException.ThrowIfNull(stopwatch);
            ArgumentNullException.ThrowIfNull(processorType);
            ArgumentNullException.ThrowIfNull(eventType);
            
            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            var key = GetKey(processorType, eventType);
            
            // 処理時間の記録
            if (!_processingTimes.TryGetValue(key, out var times))
            {
                // コンカレントキューの初期化
                times = new ConcurrentQueue<double>();
                _processingTimes[key] = times;
            }
            
            // キューのサイズを制限（最新の100回分を保持）
            if (times.Count >= 100)
            {
                times.TryDequeue(out _);
            }
            
            times.Enqueue(elapsed);
            
            // エラー発生時はカウント
            if (!isSuccess)
            {
                _errorCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            }
            
            // 処理時間が長い場合は警告ログを出力
            if (elapsed > 100)
            {
                _logger?.LogWarning("プロセッサ {ProcessorType} のイベント {EventType} 処理に {ProcessingTime}ms かかりました",
                    processorType.Name, eventType.Name, elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        
        /// <summary>
        /// メトリクスレポートを作成
        /// </summary>
        /// <returns>レポート文字列</returns>
        public string GenerateReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("===== イベントプロセッサメトリクスレポート =====");
            report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "記録時刻: {0}", DateTime.Now));
            report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "監視中のプロセッサ数: {0}", _processingTimes.Count));
            report.AppendLine();
            
            foreach (var key in _processingTimes.Keys)
            {
                if (_processingTimes.TryGetValue(key, out var times) && !times.IsEmpty)
                {
                    var timesList = times.ToList();
                    var avgTime = timesList.Average();
                    var maxTime = timesList.Max();
                    var minTime = timesList.Min();
                    var p95Time = timesList.OrderBy(t => t).ElementAt((int)(timesList.Count * 0.95));
                    
                    _invocationCounts.TryGetValue(key, out var invocations);
                    _errorCounts.TryGetValue(key, out var errors);
                    var successRate = invocations > 0 ? 100 * (invocations - errors) / (double)invocations : 0;
                    
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "プロセッサ: {0}", key));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  呼び出し回数: {0}", invocations));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  エラー回数: {0}", errors));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  成功率: {0}%", successRate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  平均処理時間: {0}ms", avgTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  最大処理時間: {0}ms", maxTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  最小処理時間: {0}ms", minTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  95パーセンタイル: {0}ms", p95Time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    report.AppendLine();
                }
            }
            
            report.AppendLine("============================================");
            return report.ToString();
        }
        
        /// <summary>
        /// メトリクスを取得
        /// </summary>
        /// <returns>プロセッサごとのメトリクス</returns>
        public IReadOnlyDictionary<string, ProcessorMetric> GetMetrics()
        {
            // ディクショナリの初期化
            var metrics = new Dictionary<string, ProcessorMetric>();
            
            foreach (var key in _processingTimes.Keys)
            {
                if (_processingTimes.TryGetValue(key, out var times) && !times.IsEmpty)
                {
                    var timesList = times.ToList();
                    
                    _invocationCounts.TryGetValue(key, out var invocations);
                    _errorCounts.TryGetValue(key, out var errors);
                    
                    metrics[key] = new ProcessorMetric
                    {
                        ProcessorKey = key,
                        InvocationCount = invocations,
                        ErrorCount = errors,
                        AverageProcessingTime = timesList.Average(),
                        MaxProcessingTime = timesList.Max(),
                        MinProcessingTime = timesList.Min(),
                        P95ProcessingTime = timesList.OrderBy(t => t).ElementAt((int)(timesList.Count * 0.95))
                    };
                }
            }
            
            return metrics;
        }
        
        /// <summary>
        /// プロセッサとイベントタイプからキーを生成
        /// </summary>
        /// <param name="processorType">プロセッサタイプ</param>
        /// <param name="eventType">イベントタイプ</param>
        /// <returns>キー文字列</returns>
        private static string GetKey(Type processorType, Type eventType)
        {
            // processorType と eventType は呼び出し元でチェック済み
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}_{1}", processorType.Name, eventType.Name);
        }
    }
    
    /// <summary>
    /// プロセッサのメトリクス情報
    /// </summary>
    public class ProcessorMetric
    {
        /// <summary>
        /// プロセッサキー
        /// </summary>
        public string ProcessorKey { get; set; } = string.Empty;
        
        /// <summary>
        /// 呼び出し回数
        /// </summary>
        public long InvocationCount { get; set; }
        
        /// <summary>
        /// エラー回数
        /// </summary>
        public long ErrorCount { get; set; }
        
        /// <summary>
        /// 平均処理時間（ミリ秒）
        /// </summary>
        public double AverageProcessingTime { get; set; }
        
        /// <summary>
        /// 最大処理時間（ミリ秒）
        /// </summary>
        public double MaxProcessingTime { get; set; }
        
        /// <summary>
        /// 最小処理時間（ミリ秒）
        /// </summary>
        public double MinProcessingTime { get; set; }
        
        /// <summary>
        /// 95パーセンタイル処理時間（ミリ秒）
        /// </summary>
        public double P95ProcessingTime { get; set; }
        
        /// <summary>
        /// 成功率（％）
        /// </summary>
        public double SuccessRate => InvocationCount > 0 ? 100 * (InvocationCount - ErrorCount) / (double)InvocationCount : 0;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.ResourceManagement;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// Phase 3: ヒステリシス制御による動的並列度調整コントローラー
/// 上下限しきい値、フラッピング防止、安定性確認を統合
/// </summary>
public sealed class HysteresisParallelismController : IDisposable
{
    private readonly ILogger<HysteresisParallelismController> _logger;
    private readonly HysteresisControlSettings _settings;
    
    private int _currentParallelism;
    private DateTime _lastAdjustmentTime = DateTime.MinValue;
    private readonly Queue<MeasurementPoint> _measurementHistory = new();
    private readonly object _stateLock = new();
    private bool _disposed;
    
    /// <summary>ヒステリシス状態変更イベント</summary>
    public event EventHandler<HysteresisStateChangedEventArgs>? HysteresisStateChanged;
    
    public HysteresisParallelismController(
        ILogger<HysteresisParallelismController> logger,
        HysteresisControlSettings settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        if (!_settings.IsValid())
        {
            throw new ArgumentException("Invalid hysteresis control settings", nameof(settings));
        }
        
        _currentParallelism = Math.Max(_settings.MinParallelism, 2); // デフォルト並列度
        
        _logger.LogInformation("🎯 HysteresisParallelismController初期化完了 - 初期並列度: {Parallelism}, GPU上限: {UpperThreshold}%, GPU下限: {LowerThreshold}%",
            _currentParallelism, _settings.GpuUpperThresholdPercent, _settings.GpuLowerThresholdPercent);
    }
    
    /// <summary>
    /// GPU/VRAMメトリクスに基づくヒステリシス制御実行
    /// </summary>
    public async Task<int> AdjustParallelismAsync(GpuVramMetrics gpuMetrics, SystemLoad systemLoad, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HysteresisParallelismController));
        }
        
        lock (_stateLock)
        {
            // 測定履歴に追加
            var measurement = new MeasurementPoint(
                gpuMetrics.GpuUtilizationPercent,
                gpuMetrics.VramUsagePercent,
                gpuMetrics.GpuTemperatureCelsius,
                systemLoad.IsGamingActive,
                DateTime.UtcNow
            );
            
            AddMeasurement(measurement);
            
            // フラッピング防止：最小間隔チェック
            if (DateTime.UtcNow - _lastAdjustmentTime < _settings.MinAdjustmentInterval)
            {
                _logger.LogDebug("⏱️ ヒステリシス制御: 最小間隔未経過のためスキップ - 残り{Remaining}秒", 
                    (_settings.MinAdjustmentInterval - (DateTime.UtcNow - _lastAdjustmentTime)).TotalSeconds);
                return _currentParallelism;
            }
            
            // 緊急制限チェック
            if (gpuMetrics.GpuTemperatureCelsius >= 90.0)
            {
                return ApplyEmergencyLimiting(gpuMetrics.GpuTemperatureCelsius);
            }
            
            // ゲーミングモード制限
            if (systemLoad.IsGamingActive && _currentParallelism > _settings.GamingModeMaxParallelism)
            {
                return ApplyGamingModeLimit();
            }
            
            // ヒステリシス制御判定
            var adjustmentDecision = EvaluateAdjustmentDecision(gpuMetrics, systemLoad);
            
            if (adjustmentDecision != ParallelismAdjustment.NoChange)
            {
                return ApplyAdjustment(adjustmentDecision, gpuMetrics, systemLoad);
            }
            
            return _currentParallelism;
        }
    }
    
    /// <summary>現在の並列度を取得</summary>
    public int GetCurrentParallelism()
    {
        lock (_stateLock)
        {
            return _currentParallelism;
        }
    }
    
    /// <summary>ヒステリシス制御状態の詳細情報を取得</summary>
    public HysteresisControlState GetControlState()
    {
        lock (_stateLock)
        {
            var recentMeasurements = _measurementHistory.TakeLast(5).ToArray();
            var avgGpuUsage = recentMeasurements.Length > 0 ? recentMeasurements.Average(m => m.GpuUsagePercent) : 0;
            var avgVramUsage = recentMeasurements.Length > 0 ? recentMeasurements.Average(m => m.VramUsagePercent) : 0;
            
            return new HysteresisControlState(
                _currentParallelism,
                avgGpuUsage,
                avgVramUsage,
                _settings.GpuUpperThresholdPercent,
                _settings.GpuLowerThresholdPercent,
                DateTime.UtcNow - _lastAdjustmentTime,
                recentMeasurements.Length >= _settings.StabilityRequiredMeasurements
            );
        }
    }
    
    private void AddMeasurement(MeasurementPoint measurement)
    {
        _measurementHistory.Enqueue(measurement);
        
        // 履歴サイズ制限（最新20件）
        while (_measurementHistory.Count > 20)
        {
            _measurementHistory.Dequeue();
        }
    }
    
    private ParallelismAdjustment EvaluateAdjustmentDecision(GpuVramMetrics gpuMetrics, SystemLoad systemLoad)
    {
        // 安定性確認：必要な測定回数が揃っているかチェック
        if (_measurementHistory.Count < _settings.StabilityRequiredMeasurements)
        {
            return ParallelismAdjustment.NoChange;
        }
        
        var recentMeasurements = _measurementHistory.TakeLast(_settings.StabilityRequiredMeasurements).ToArray();
        var avgGpuUsage = recentMeasurements.Average(m => m.GpuUsagePercent);
        var avgVramUsage = recentMeasurements.Average(m => m.VramUsagePercent);
        
        // 上限チェック：負荷が高すぎる場合は並列度を下げる
        if (avgGpuUsage > _settings.GpuUpperThresholdPercent || avgVramUsage > _settings.VramUpperThresholdPercent)
        {
            if (_currentParallelism > _settings.MinParallelism)
            {
                _logger.LogDebug("📉 ヒステリシス制御: 負荷上限超過による並列度削減判定 - GPU: {GpuUsage:F1}% (上限: {UpperThreshold}%), VRAM: {VramUsage:F1}% (上限: {VramUpperThreshold}%)",
                    avgGpuUsage, _settings.GpuUpperThresholdPercent, avgVramUsage, _settings.VramUpperThresholdPercent);
                return ParallelismAdjustment.Decrease;
            }
        }
        
        // 下限チェック：負荷が低い場合は並列度を上げる
        else if (avgGpuUsage < _settings.GpuLowerThresholdPercent && avgVramUsage < _settings.VramLowerThresholdPercent)
        {
            if (_currentParallelism < _settings.MaxParallelism)
            {
                // すべての測定値が下限を下回っている場合のみ増加（安定性重視）
                bool allBelowThreshold = recentMeasurements.All(m => 
                    m.GpuUsagePercent < _settings.GpuLowerThresholdPercent && 
                    m.VramUsagePercent < _settings.VramLowerThresholdPercent);
                    
                if (allBelowThreshold)
                {
                    _logger.LogDebug("📈 ヒステリシス制御: 負荷下限未満による並列度増加判定 - GPU: {GpuUsage:F1}% (下限: {LowerThreshold}%), VRAM: {VramUsage:F1}% (下限: {VramLowerThreshold}%)",
                        avgGpuUsage, _settings.GpuLowerThresholdPercent, avgVramUsage, _settings.VramLowerThresholdPercent);
                    return ParallelismAdjustment.Increase;
                }
            }
        }
        
        return ParallelismAdjustment.NoChange;
    }
    
    private int ApplyAdjustment(ParallelismAdjustment adjustment, GpuVramMetrics gpuMetrics, SystemLoad systemLoad)
    {
        var previousParallelism = _currentParallelism;
        
        switch (adjustment)
        {
            case ParallelismAdjustment.Increase:
                _currentParallelism = Math.Min(_currentParallelism + _settings.AdjustmentStep, _settings.MaxParallelism);
                break;
                
            case ParallelismAdjustment.Decrease:
                _currentParallelism = Math.Max(_currentParallelism - _settings.AdjustmentStep, _settings.MinParallelism);
                break;
        }
        
        _lastAdjustmentTime = DateTime.UtcNow;
        
        _logger.LogInformation("🎯 ヒステリシス制御: 並列度調整実行 - {Previous} → {New} ({Adjustment}) | GPU: {GpuUsage:F1}%, VRAM: {VramUsage:F1}%, 温度: {Temperature:F1}°C",
            previousParallelism, _currentParallelism, adjustment, 
            gpuMetrics.GpuUtilizationPercent, gpuMetrics.VramUsagePercent, gpuMetrics.GpuTemperatureCelsius);
        
        // イベント発行
        HysteresisStateChanged?.Invoke(this, new HysteresisStateChangedEventArgs
        {
            PreviousParallelism = previousParallelism,
            NewParallelism = _currentParallelism,
            IsIncreasing = adjustment == ParallelismAdjustment.Increase,
            CurrentLoad = (gpuMetrics.GpuUtilizationPercent + gpuMetrics.VramUsagePercent) / 2,
            UpperThreshold = _settings.GpuUpperThresholdPercent,
            LowerThreshold = _settings.GpuLowerThresholdPercent
        });
        
        return _currentParallelism;
    }
    
    private int ApplyEmergencyLimiting(double temperature)
    {
        if (_currentParallelism > _settings.EmergencyParallelismLimit)
        {
            var previousParallelism = _currentParallelism;
            _currentParallelism = _settings.EmergencyParallelismLimit;
            _lastAdjustmentTime = DateTime.UtcNow;
            
            _logger.LogWarning("🚨 緊急制限発動: GPU温度 {Temperature:F1}°C により並列度を緊急制限 - {Previous} → {Emergency}",
                temperature, previousParallelism, _currentParallelism);
            
            HysteresisStateChanged?.Invoke(this, new HysteresisStateChangedEventArgs
            {
                PreviousParallelism = previousParallelism,
                NewParallelism = _currentParallelism,
                IsIncreasing = false,
                CurrentLoad = 100.0, // 緊急状態
                UpperThreshold = _settings.GpuUpperThresholdPercent,
                LowerThreshold = _settings.GpuLowerThresholdPercent
            });
        }
        
        return _currentParallelism;
    }
    
    private int ApplyGamingModeLimit()
    {
        var previousParallelism = _currentParallelism;
        _currentParallelism = _settings.GamingModeMaxParallelism;
        _lastAdjustmentTime = DateTime.UtcNow;
        
        _logger.LogInformation("🎮 ゲーミングモード制限: 並列度をゲーム用制限値に調整 - {Previous} → {GamingLimit}",
            previousParallelism, _currentParallelism);
        
        return _currentParallelism;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_stateLock)
            {
                _measurementHistory.Clear();
                _disposed = true;
            }
            
            _logger.LogDebug("HysteresisParallelismController正常終了");
        }
    }
}

/// <summary>測定ポイントデータ</summary>
internal sealed record MeasurementPoint(
    double GpuUsagePercent,
    double VramUsagePercent,
    double GpuTemperatureCelsius,
    bool IsGamingActive,
    DateTime MeasuredAt
);

/// <summary>並列度調整の判定結果</summary>
internal enum ParallelismAdjustment
{
    NoChange,   // 変更なし
    Increase,   // 増加
    Decrease    // 減少
}

/// <summary>
/// ヒステリシス制御状態の詳細情報
/// </summary>
public sealed record HysteresisControlState(
    int CurrentParallelism,
    double AverageGpuUsage,
    double AverageVramUsage,
    double UpperThreshold,
    double LowerThreshold,
    TimeSpan TimeSinceLastAdjustment,
    bool IsStable
);
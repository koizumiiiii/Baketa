using Baketa.Core.Events.Diagnostics;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 診断情報収集サービスの抽象化
/// パフォーマンス影響を最小化した非同期診断データ収集
/// </summary>
public interface IDiagnosticCollectionService
{
    /// <summary>
    /// 診断イベントを非同期で収集
    /// メイン処理をブロックしないようバックグラウンドで処理
    /// </summary>
    Task CollectDiagnosticAsync(PipelineDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 現在蓄積されている診断データを即座にレポートとして生成
    /// </summary>
    Task<string> GenerateReportAsync(string reportType = "diagnostic", CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 診断データ収集を開始
    /// </summary>
    Task StartCollectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 診断データ収集を停止し、蓄積データをフラッシュ
    /// </summary>
    Task StopCollectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 診断収集サービスの動作状態
    /// </summary>
    bool IsCollecting { get; }
}
namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// GPU環境チェック・セットアップサービスのインターフェース
/// Issue #193: Python GPU環境の自動セットアップ
/// </summary>
public interface IGpuEnvironmentService
{
    /// <summary>
    /// GPU環境セットアップ進捗イベント
    /// </summary>
    event EventHandler<GpuSetupProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// NVIDIA GPUが利用可能かチェック（nvidia-smiベース）
    /// </summary>
    /// <returns>NVIDIA GPUが検出された場合true</returns>
    Task<bool> IsNvidiaGpuAvailableAsync();

    /// <summary>
    /// Python環境でCUDAが利用可能かチェック（torch.cuda.is_available()ベース）
    /// </summary>
    /// <returns>CUDAが利用可能な場合true</returns>
    Task<bool> IsCudaAvailableInPythonAsync();

    /// <summary>
    /// GPU環境が既にセットアップ済みかチェック（.gpu_okフラグ）
    /// </summary>
    /// <returns>セットアップ済みの場合true</returns>
    bool IsGpuEnvironmentSetup();

    /// <summary>
    /// GPU版パッケージをインストール
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>インストール成功の場合true</returns>
    Task<GpuSetupResult> InstallGpuPackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GPU環境セットアップ完了をマーク（.gpu_okフラグ作成）
    /// </summary>
    void MarkGpuEnvironmentSetup();

    /// <summary>
    /// GPU環境の完全チェックと必要に応じた自動セットアップ
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>セットアップ結果</returns>
    Task<GpuSetupResult> EnsureGpuEnvironmentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// GPU環境セットアップ結果
/// </summary>
public enum GpuSetupResult
{
    /// <summary>GPU環境が正常に利用可能</summary>
    Success,

    /// <summary>既にセットアップ済み</summary>
    AlreadySetup,

    /// <summary>NVIDIA GPU未検出（CPUモードで続行）</summary>
    NoNvidiaGpu,

    /// <summary>セットアップをスキップ</summary>
    Skipped,

    /// <summary>インストール失敗（CPUモードで続行）</summary>
    InstallationFailed,

    /// <summary>配布版のためスキップ</summary>
    SkippedDistribution
}

/// <summary>
/// GPU環境セットアップ進捗情報
/// </summary>
public class GpuSetupProgressEventArgs : EventArgs
{
    /// <summary>現在のステップ</summary>
    public required string Step { get; init; }

    /// <summary>メッセージ</summary>
    public required string Message { get; init; }

    /// <summary>進捗率（0-100）</summary>
    public int Progress { get; init; }

    /// <summary>完了フラグ</summary>
    public bool IsCompleted { get; init; }

    /// <summary>エラーメッセージ（エラー時のみ）</summary>
    public string? ErrorMessage { get; init; }
}

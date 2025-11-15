using Baketa.Core.Abstractions.GPU;

namespace Baketa.Core.Abstractions.Diagnostics;

/// <summary>
/// PaddleOCR診断システムインターフェース
/// Gemini推奨: 起動時全依存関係チェック + 初期化プロセス診断
/// Sprint 1: 基盤復旧診断システム
/// </summary>
public interface IPaddleOcrDiagnostics
{
    /// <summary>
    /// 包括的診断実行（全チェック項目）
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>詳細診断レポート</returns>
    Task<DiagnosticReport> RunFullDiagnosticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 依存関係整合性確認
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>依存関係が正常か</returns>
    Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// モデルファイル整合性検証
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>モデルファイルが正常か</returns>
    Task<bool> ValidateModelFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GPU環境互換性チェック
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>GPU互換性レポート</returns>
    Task<GpuCompatibilityReport> CheckGpuCompatibilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// PaddleOCR初期化プロセス診断（段階別）
    /// </summary>
    /// <param name="useCpuOnly">CPUモード強制（GPU問題切り分け用）</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>初期化診断結果</returns>
    Task<InitializationDiagnosticResult> DiagnoseInitializationAsync(bool useCpuOnly = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// 診断レポート
/// </summary>
public class DiagnosticReport
{
    /// <summary>
    /// 全体的な健全性スコア（0-1）
    /// </summary>
    public double OverallHealthScore { get; init; }

    /// <summary>
    /// 依存関係チェック結果
    /// </summary>
    public DependencyCheckResult Dependencies { get; init; } = new();

    /// <summary>
    /// モデルファイル検証結果
    /// </summary>
    public ModelValidationResult ModelFiles { get; init; } = new();

    /// <summary>
    /// GPU互換性結果
    /// </summary>
    public GpuCompatibilityReport GpuCompatibility { get; init; } = new();

    /// <summary>
    /// 初期化診断結果
    /// </summary>
    public InitializationDiagnosticResult Initialization { get; init; } = new();

    /// <summary>
    /// 検出された問題一覧
    /// </summary>
    public IReadOnlyList<DiagnosticIssue> DetectedIssues { get; init; } = [];

    /// <summary>
    /// 推奨アクション
    /// </summary>
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];

    /// <summary>
    /// 診断実行時刻
    /// </summary>
    public DateTime DiagnosedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 依存関係チェック結果
/// </summary>
public class DependencyCheckResult
{
    /// <summary>
    /// チェック成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// PaddleOCR DLL存在確認
    /// </summary>
    public bool PaddleOcrDllExists { get; init; }

    /// <summary>
    /// OpenCV DLL存在確認
    /// </summary>
    public bool OpenCvDllExists { get; init; }

    /// <summary>
    /// CUDA DLL存在確認（GPU使用時）
    /// </summary>
    public bool CudaDllExists { get; init; }

    /// <summary>
    /// 依存関係詳細情報
    /// </summary>
    public Dictionary<string, DependencyInfo> Dependencies { get; init; } = [];

    /// <summary>
    /// 検出された依存関係問題
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>
/// 依存関係情報
/// </summary>
public class DependencyInfo
{
    /// <summary>
    /// 依存関係名
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// ファイルパス
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// 存在確認結果
    /// </summary>
    public bool Exists { get; init; }

    /// <summary>
    /// ファイルバージョン
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// ファイルサイズ
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime? LastModified { get; init; }
}

/// <summary>
/// モデルファイル検証結果
/// </summary>
public class ModelValidationResult
{
    /// <summary>
    /// 検証成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// モデルキャッシュディレクトリ存在確認
    /// </summary>
    public bool ModelCacheDirectoryExists { get; init; }

    /// <summary>
    /// 検出器モデル整合性
    /// </summary>
    public ModelFileInfo? DetectorModel { get; init; }

    /// <summary>
    /// 認識器モデル整合性
    /// </summary>
    public ModelFileInfo? RecognitionModel { get; init; }

    /// <summary>
    /// 分類器モデル整合性
    /// </summary>
    public ModelFileInfo? ClassificationModel { get; init; }

    /// <summary>
    /// 検出されたモデル問題
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>
/// モデルファイル情報
/// </summary>
public class ModelFileInfo
{
    /// <summary>
    /// モデル名
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// ファイルパス
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// 存在確認結果
    /// </summary>
    public bool Exists { get; init; }

    /// <summary>
    /// ファイルサイズ
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// ハッシュ値（整合性確認用）
    /// </summary>
    public string? FileHash { get; init; }

    /// <summary>
    /// 破損チェック結果
    /// </summary>
    public bool IsCorrupted { get; init; }
}

/// <summary>
/// GPU互換性レポート
/// </summary>
public class GpuCompatibilityReport
{
    /// <summary>
    /// GPU互換性チェック成功
    /// </summary>
    public bool IsCompatible { get; init; }

    /// <summary>
    /// CUDA利用可能性
    /// </summary>
    public bool CudaAvailable { get; init; }

    /// <summary>
    /// CUDAバージョン
    /// </summary>
    public string? CudaVersion { get; init; }

    /// <summary>
    /// cuDNNバージョン
    /// </summary>
    public string? CudnnVersion { get; init; }

    /// <summary>
    /// GPU情報
    /// </summary>
    public GpuEnvironmentInfo? GpuInfo { get; init; }

    /// <summary>
    /// VRAM利用可能量（MB）
    /// </summary>
    public long AvailableVramMB { get; init; }

    /// <summary>
    /// ドライババージョン
    /// </summary>
    public string? DriverVersion { get; init; }

    /// <summary>
    /// GPU互換性問題
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>
/// 初期化診断結果
/// </summary>
public class InitializationDiagnosticResult
{
    /// <summary>
    /// 初期化成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// CPUモード初期化結果
    /// </summary>
    public bool CpuModeSuccess { get; init; }

    /// <summary>
    /// GPUモード初期化結果（テスト時のみ）
    /// </summary>
    public bool GpuModeSuccess { get; init; }

    /// <summary>
    /// 初期化ステップ詳細
    /// </summary>
    public IReadOnlyList<InitializationStep> InitializationSteps { get; init; } = [];

    /// <summary>
    /// 初期化時間
    /// </summary>
    public TimeSpan InitializationTime { get; init; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 例外詳細
    /// </summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// 初期化ステップ
/// </summary>
public class InitializationStep
{
    /// <summary>
    /// ステップ名
    /// </summary>
    public string StepName { get; init; } = string.Empty;

    /// <summary>
    /// 成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 詳細情報
    /// </summary>
    public Dictionary<string, object> Details { get; init; } = [];
}

/// <summary>
/// 診断問題
/// </summary>
public class DiagnosticIssue
{
    /// <summary>
    /// 問題の重要度
    /// </summary>
    public DiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// 問題カテゴリ
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// 問題説明
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 推奨解決策
    /// </summary>
    public string RecommendedSolution { get; init; } = string.Empty;
}

/// <summary>
/// 診断重要度
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// 情報
    /// </summary>
    Info,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// エラー
    /// </summary>
    Error,

    /// <summary>
    /// クリティカル
    /// </summary>
    Critical
}

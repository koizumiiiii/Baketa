using System.IO;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCRモデルの種類
/// </summary>
public enum OcrModelType
{
    /// <summary>
    /// テキスト検出モデル
    /// </summary>
    Detection,

    /// <summary>
    /// テキスト認識モデル
    /// </summary>
    Recognition,

    /// <summary>
    /// 方向分類モデル（将来拡張用）
    /// </summary>
    Classification
}

/// <summary>
/// OCRモデル情報
/// </summary>
public class OcrModelInfo
{
    /// <summary>
    /// モデルID
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// モデル名
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// モデル種類
    /// </summary>
    public OcrModelType Type { get; }

    /// <summary>
    /// モデルファイル名
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// ダウンロードURL
    /// </summary>
    public Uri DownloadUrl { get; }

    /// <summary>
    /// モデルサイズ（バイト）
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// モデルハッシュ（検証用）
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// 関連する言語コード（nullの場合は言語非依存）
    /// </summary>
    public string? LanguageCode { get; }

    /// <summary>
    /// モデル説明
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// モデルバージョン
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// 更新日時
    /// </summary>
    public DateTime UpdatedAt { get; }

    /// <summary>
    /// モデルが必須かどうか
    /// </summary>
    public bool IsRequired { get; }

    public OcrModelInfo(
        string id,
        string name,
        OcrModelType type,
        string fileName,
        Uri downloadUrl,
        long fileSize,
        string hash,
        string? languageCode = null,
        string description = "",
        string version = "1.0",
        DateTime? createdAt = null,
        DateTime? updatedAt = null,
        bool isRequired = false)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(downloadUrl);
        ArgumentNullException.ThrowIfNull(hash);

        Id = id;
        Name = name;
        Type = type;
        FileName = fileName;
        DownloadUrl = downloadUrl;
        FileSize = Math.Max(0, fileSize);
        Hash = hash;
        LanguageCode = languageCode;
        Description = description ?? string.Empty;
        Version = version ?? "1.0";
        CreatedAt = createdAt ?? DateTime.UtcNow;
        UpdatedAt = updatedAt ?? DateTime.UtcNow;
        IsRequired = isRequired;
    }

    /// <summary>
    /// モデル情報の妥当性をチェック
    /// </summary>
    /// <returns>妥当性チェック結果</returns>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return false;

        if (string.IsNullOrWhiteSpace(Name))
            return false;

        if (string.IsNullOrWhiteSpace(FileName))
            return false;

        if (DownloadUrl == null)
            return false;

        if (!DownloadUrl.IsAbsoluteUri)
            return false;

        // 許可されたURLスキームのみ有効とする（セキュリティ向上）
        if (!IsAllowedUrlScheme(DownloadUrl.Scheme))
            return false;

        if (FileSize <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(Hash))
            return false;

        return true;
    }

    /// <summary>
    /// 許可されたURLスキームかどうかをチェック
    /// </summary>
    /// <param name="scheme">URLスキーム</param>
    /// <returns>許可されている場合はtrue</returns>
    private static bool IsAllowedUrlScheme(string scheme)
    {
        return scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 完全なモデルファイルパスを取得
    /// </summary>
    /// <param name="baseDirectory">ベースディレクトリ</param>
    /// <returns>完全なファイルパス</returns>
    public string GetFullPath(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("ベースディレクトリが無効です", nameof(baseDirectory));

        return Path.Combine(baseDirectory, FileName);
    }
}

/// <summary>
/// モデルダウンロード状態
/// </summary>
public enum ModelDownloadStatus
{
    /// <summary>
    /// 待機中
    /// </summary>
    Pending,

    /// <summary>
    /// ダウンロード中
    /// </summary>
    Downloading,

    /// <summary>
    /// 検証中
    /// </summary>
    Validating,

    /// <summary>
    /// 完了
    /// </summary>
    Completed,

    /// <summary>
    /// エラー
    /// </summary>
    Error,

    /// <summary>
    /// キャンセル済み
    /// </summary>
    Cancelled,

    /// <summary>
    /// 再試行待ち
    /// </summary>
    Retrying
}

/// <summary>
/// モデルダウンロード進捗情報
/// </summary>
public class ModelDownloadProgress
{
    /// <summary>
    /// 対象モデル情報
    /// </summary>
    public OcrModelInfo ModelInfo { get; }

    /// <summary>
    /// ダウンロード状態
    /// </summary>
    public ModelDownloadStatus Status { get; }

    /// <summary>
    /// 進捗率（0.0～1.0）
    /// </summary>
    public double Progress { get; }

    /// <summary>
    /// 現在のアクション説明
    /// </summary>
    public string StatusMessage { get; }

    /// <summary>
    /// エラー情報（エラー時のみ）
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// ダウンロード済みバイト数
    /// </summary>
    public long DownloadedBytes { get; }

    /// <summary>
    /// 総バイト数
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// ダウンロード速度（バイト/秒）
    /// </summary>
    public double DownloadSpeedBps { get; }

    /// <summary>
    /// 残り時間の推定
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; }

    /// <summary>
    /// 再試行回数
    /// </summary>
    public int RetryCount { get; }

    public ModelDownloadProgress(
        OcrModelInfo modelInfo,
        ModelDownloadStatus status,
        double progress,
        string statusMessage,
        string? errorMessage = null,
        long downloadedBytes = 0,
        long totalBytes = 0,
        double downloadSpeedBps = 0,
        TimeSpan? estimatedTimeRemaining = null,
        int retryCount = 0)
    {
        ArgumentNullException.ThrowIfNull(modelInfo);

        ModelInfo = modelInfo;
        Status = status;
        Progress = Math.Clamp(progress, 0.0, 1.0);
        StatusMessage = statusMessage ?? string.Empty;
        ErrorMessage = errorMessage;
        DownloadedBytes = Math.Max(0, downloadedBytes);
        TotalBytes = Math.Max(0, totalBytes);
        DownloadSpeedBps = Math.Max(0, downloadSpeedBps);
        EstimatedTimeRemaining = estimatedTimeRemaining;
        RetryCount = Math.Max(0, retryCount);
    }

    /// <summary>
    /// ダウンロード速度を人間が読める形式で取得
    /// </summary>
    /// <returns>フォーマットされた速度文字列</returns>
    public string GetFormattedSpeed()
    {
        return FormatBytes(DownloadSpeedBps) + "/s";
    }

    /// <summary>
    /// ダウンロード済みサイズを人間が読める形式で取得
    /// </summary>
    /// <returns>フォーマットされたサイズ文字列</returns>
    public string GetFormattedProgress()
    {
        return $"{FormatBytes(DownloadedBytes)}/{FormatBytes(TotalBytes)}";
    }

    private static string FormatBytes(double bytes)
    {
        string[] suffix = ["B", "KB", "MB", "GB", "TB"];
        int i;
        double dblBytes = bytes;

        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblBytes = bytes / 1024.0;
        }

        return $"{dblBytes:0.##} {suffix[i]}";
    }
}

/// <summary>
/// モデル管理の例外
/// </summary>
public class ModelManagementException : Exception
{
    public ModelManagementException() { }

    public ModelManagementException(string message) : base(message) { }

    public ModelManagementException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// モデル検証の結果
/// </summary>
public class ModelValidationResult
{
    /// <summary>
    /// 検証成功フラグ
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// エラーメッセージ（検証失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// ファイルサイズが一致するか
    /// </summary>
    public bool FileSizeMatches { get; init; }

    /// <summary>
    /// ハッシュが一致するか
    /// </summary>
    public bool HashMatches { get; init; }

    /// <summary>
    /// ファイルが存在するか
    /// </summary>
    public bool FileExists { get; init; }

    /// <summary>
    /// 実際のファイルサイズ
    /// </summary>
    public long ActualFileSize { get; init; }

    /// <summary>
    /// 計算されたハッシュ
    /// </summary>
    public string? ActualHash { get; init; }

    public static ModelValidationResult Success() => new()
    {
        IsValid = true,
        FileSizeMatches = true,
        HashMatches = true,
        FileExists = true
    };

    public static ModelValidationResult Failure(string errorMessage) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// OCRモデル管理インターフェース
/// </summary>
public interface IOcrModelManager
{
    /// <summary>
    /// 利用可能なすべてのモデル情報を取得
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>モデル情報のリスト</returns>
    Task<IReadOnlyList<OcrModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 言語コードに対応するモデルを取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>モデル情報のリスト</returns>
    Task<IReadOnlyList<OcrModelInfo>> GetModelsForLanguageAsync(string languageCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// モデルが既にダウンロード済みかを確認
    /// </summary>
    /// <param name="modelInfo">モデル情報</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ダウンロード済みの場合はtrue</returns>
    Task<bool> IsModelDownloadedAsync(OcrModelInfo modelInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// モデルを非同期でダウンロード
    /// </summary>
    /// <param name="modelInfo">モデル情報</param>
    /// <param name="progressCallback">進捗通知コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ダウンロードが成功した場合はtrue</returns>
    Task<bool> DownloadModelAsync(
        OcrModelInfo modelInfo,
        IProgress<ModelDownloadProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数モデルを一括ダウンロード
    /// </summary>
    /// <param name="modelInfos">モデル情報のリスト</param>
    /// <param name="progressCallback">進捗通知コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>すべてのダウンロードが成功した場合はtrue</returns>
    Task<bool> DownloadModelsAsync(
        IEnumerable<OcrModelInfo> modelInfos,
        IProgress<ModelDownloadProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ダウンロード済みモデルのリストを取得
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ダウンロード済みモデル情報のリスト</returns>
    Task<IReadOnlyList<OcrModelInfo>> GetDownloadedModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// モデルを削除
    /// </summary>
    /// <param name="modelInfo">モデル情報</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>削除が成功した場合はtrue</returns>
    Task<bool> DeleteModelAsync(OcrModelInfo modelInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定言語に必要なすべてのモデルがダウンロード済みかを確認
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>すべて揃っている場合はtrue</returns>
    Task<bool> IsLanguageCompleteAsync(string languageCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// モデルファイルの整合性を検証
    /// </summary>
    /// <param name="modelInfo">モデル情報</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検証結果</returns>
    Task<ModelValidationResult> ValidateModelAsync(OcrModelInfo modelInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// モデルのメタデータを更新
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新が成功した場合はtrue</returns>
    Task<bool> RefreshModelMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用されていない古いモデルファイルをクリーンアップ
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>クリーンアップされたファイル数</returns>
    Task<int> CleanupUnusedModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// モデル管理の統計情報を取得
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>統計情報</returns>
    Task<ModelManagementStats> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// モデル管理の統計情報
/// </summary>
public class ModelManagementStats
{
    /// <summary>
    /// 総モデル数
    /// </summary>
    public int TotalModels { get; init; }

    /// <summary>
    /// ダウンロード済みモデル数
    /// </summary>
    public int DownloadedModels { get; init; }

    /// <summary>
    /// 総ダウンロードサイズ（バイト）
    /// </summary>
    public long TotalDownloadSize { get; init; }

    /// <summary>
    /// 使用ディスク容量（バイト）
    /// </summary>
    public long UsedDiskSpace { get; init; }

    /// <summary>
    /// 利用可能な言語数
    /// </summary>
    public int AvailableLanguages { get; init; }

    /// <summary>
    /// 完了した言語数（すべてのモデルがダウンロード済み）
    /// </summary>
    public int CompletedLanguages { get; init; }

    /// <summary>
    /// 最後のメタデータ更新日時
    /// </summary>
    public DateTime LastMetadataUpdate { get; init; }

    /// <summary>
    /// 最後のクリーンアップ日時
    /// </summary>
    public DateTime LastCleanup { get; init; }
}

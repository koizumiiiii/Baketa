using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// キャッシュ管理レベル（優先度）
/// </summary>
public enum CacheManagementLevel
{
    /// <summary>基本 - 必要最小限の管理</summary>
    Basic = 0,

    /// <summary>標準 - 通常の自動管理</summary>
    Standard = 1,

    /// <summary>積極的 - 容量最適化重視</summary>
    Aggressive = 2
}

/// <summary>
/// キャッシュ健全性レポート
/// </summary>
public sealed record CacheHealthReport
{
    /// <summary>キャッシュが健全か</summary>
    public bool IsHealthy { get; init; }

    /// <summary>総容量 (バイト)</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>利用可能容量 (バイト)</summary>
    public long AvailableSpaceBytes { get; init; }

    /// <summary>容量利用率 (0.0-1.0)</summary>
    public double SpaceUtilization { get; init; }

    /// <summary>破損ファイル数</summary>
    public int CorruptedFileCount { get; init; }

    /// <summary>古いファイル数</summary>
    public int ObsoleteFileCount { get; init; }

    /// <summary>推奨アクション</summary>
    public string[] RecommendedActions { get; init; } = Array.Empty<string>();

    /// <summary>詳細メッセージ</summary>
    public string DetailMessage { get; init; } = string.Empty;

    /// <summary>
    /// 正常な状態のレポートを作成
    /// </summary>
    public static CacheHealthReport CreateHealthy(long totalSize, long availableSpace)
    {
        return new CacheHealthReport
        {
            IsHealthy = true,
            TotalSizeBytes = totalSize,
            AvailableSpaceBytes = availableSpace,
            SpaceUtilization = totalSize > 0 ? 1.0 - (double)availableSpace / (totalSize + availableSpace) : 0.0,
            CorruptedFileCount = 0,
            ObsoleteFileCount = 0,
            DetailMessage = "キャッシュ状態は正常です"
        };
    }

    /// <summary>
    /// 異常な状態のレポートを作成
    /// </summary>
    public static CacheHealthReport CreateUnhealthy(string detailMessage, string[] recommendedActions,
        int corruptedFiles = 0, int obsoleteFiles = 0)
    {
        return new CacheHealthReport
        {
            IsHealthy = false,
            CorruptedFileCount = corruptedFiles,
            ObsoleteFileCount = obsoleteFiles,
            RecommendedActions = recommendedActions,
            DetailMessage = detailMessage
        };
    }
}

/// <summary>
/// 高度なキャッシュ管理サービス
/// ModelCacheManagerを基盤とした容量監視・自動クリーンアップ・破損検出機能を提供
/// </summary>
public sealed class CacheManagementService
{
    private readonly ILogger<CacheManagementService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ModelCacheManager _modelCacheManager;
    private readonly CacheManagementLevel _managementLevel;

    // 設定値
    private readonly long _maxCacheSizeBytes;
    private readonly int _maxRetentionDays;
    private readonly double _cleanupThresholdRatio;

    public CacheManagementService(
        ILogger<CacheManagementService> logger,
        IConfiguration configuration,
        ModelCacheManager modelCacheManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _modelCacheManager = modelCacheManager ?? throw new ArgumentNullException(nameof(modelCacheManager));

        // 設定読み込み
        _managementLevel = GetManagementLevel();
        _maxCacheSizeBytes = GetMaxCacheSizeBytes();
        _maxRetentionDays = _configuration.GetValue("Translation:NLLB200:ModelCache:MaxCacheRetentionDays", 30);
        _cleanupThresholdRatio = _configuration.GetValue("Translation:NLLB200:ModelCache:CleanupThresholdRatio", 0.8);

        _logger.LogInformation("🗂️ CacheManagementService初期化: Level={Level}, MaxSize={MaxSizeMB:F1}MB, Retention={RetentionDays}日",
            _managementLevel, _maxCacheSizeBytes / 1024.0 / 1024.0, _maxRetentionDays);
    }

    /// <summary>
    /// キャッシュの健全性チェックと包括レポート生成
    /// </summary>
    public async Task<CacheHealthReport> PerformHealthCheckAsync()
    {
        try
        {
            _logger.LogDebug("🔍 キャッシュ健全性チェック開始");

            var cacheInfo = await _modelCacheManager.GetCacheInfoAsync();

            if (!cacheInfo.IsModelCached)
            {
                _logger.LogDebug("ℹ️ モデル未キャッシュ（初回実行前）");
                return CacheHealthReport.CreateHealthy(0, 0);
            }

            // ディスク容量チェック
            var driveInfo = new DriveInfo(Path.GetPathRoot(cacheInfo.CacheDirectory)!);
            var availableSpace = driveInfo.AvailableFreeSpace;
            var totalSpace = driveInfo.TotalSize;
            var utilizationRatio = (double)(totalSpace - availableSpace) / totalSpace;

            // 破損ファイルチェック
            var corruptedCount = await CheckCorruptedFilesAsync(cacheInfo.ModelPath);

            // 古いファイルチェック
            var obsoleteCount = await CheckObsoleteFilesAsync(cacheInfo.CacheDirectory);

            // 健全性判定
            var isHealthy = corruptedCount == 0 &&
                          obsoleteCount < 5 &&
                          utilizationRatio < 0.95 &&
                          availableSpace > _maxCacheSizeBytes * 2;

            if (isHealthy)
            {
                _logger.LogDebug("✅ キャッシュ健全性良好: 容量利用率={Utilization:P1}", utilizationRatio);
                return CacheHealthReport.CreateHealthy(cacheInfo.CacheSize, availableSpace);
            }

            // 問題検出時の推奨アクション生成
            var actions = GenerateRecommendedActions(corruptedCount, obsoleteCount, utilizationRatio, availableSpace);
            var detailMessage = $"キャッシュに問題を検出: 破損ファイル={corruptedCount}, 古いファイル={obsoleteCount}, 容量利用率={utilizationRatio:P1}";

            _logger.LogWarning("⚠️ {DetailMessage}", detailMessage);
            return CacheHealthReport.CreateUnhealthy(detailMessage, actions, corruptedCount, obsoleteCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュ健全性チェック失敗");
            return CacheHealthReport.CreateUnhealthy(
                $"健全性チェックエラー: {ex.Message}",
                new[] { "システム管理者に連絡してください" });
        }
    }

    /// <summary>
    /// 自動キャッシュクリーンアップ実行
    /// </summary>
    public async Task<bool> PerformAutomaticCleanupAsync()
    {
        try
        {
            _logger.LogInformation("🧹 自動キャッシュクリーンアップ開始");

            if (_managementLevel == CacheManagementLevel.Basic)
            {
                _logger.LogDebug("ℹ️ Basic管理レベル - 自動クリーンアップスキップ");
                return true;
            }

            var healthReport = await PerformHealthCheckAsync();

            // クリーンアップ実行条件判定
            var shouldCleanup = healthReport.SpaceUtilization > _cleanupThresholdRatio ||
                              healthReport.CorruptedFileCount > 0 ||
                              (_managementLevel == CacheManagementLevel.Aggressive && healthReport.ObsoleteFileCount > 2);

            if (!shouldCleanup)
            {
                _logger.LogDebug("ℹ️ クリーンアップ不要 - 条件未達成");
                return true;
            }

            var cleanupSuccess = true;

            // 破損ファイルの修復または削除
            if (healthReport.CorruptedFileCount > 0)
            {
                _logger.LogInformation("🔧 破損ファイル修復実行: {Count}件", healthReport.CorruptedFileCount);
                cleanupSuccess &= await RepairCorruptedFilesAsync();
            }

            // 容量超過時のキャッシュクリア
            if (healthReport.SpaceUtilization > 0.9)
            {
                _logger.LogWarning("💾 容量逼迫によるキャッシュクリア実行: {Utilization:P1}", healthReport.SpaceUtilization);
                cleanupSuccess &= await _modelCacheManager.CleanupCacheAsync();
            }

            _logger.LogInformation(cleanupSuccess ?
                "✅ 自動クリーンアップ完了" :
                "⚠️ 自動クリーンアップ部分的成功");

            return cleanupSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 自動キャッシュクリーンアップ失敗");
            return false;
        }
    }

    /// <summary>
    /// キャッシュ容量監視と警告
    /// </summary>
    public async Task<bool> MonitorCacheSpaceAsync()
    {
        try
        {
            var cacheInfo = await _modelCacheManager.GetCacheInfoAsync();

            if (!cacheInfo.IsModelCached)
                return true; // モデル未キャッシュ時は問題なし

            var cacheDirectory = cacheInfo.CacheDirectory;
            var driveInfo = new DriveInfo(Path.GetPathRoot(cacheDirectory)!);

            var availableSpaceGB = driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            var totalSpaceGB = driveInfo.TotalSize / 1024.0 / 1024.0 / 1024.0;
            var utilizationRatio = (totalSpaceGB - availableSpaceGB) / totalSpaceGB;

            // 警告レベル判定
            if (utilizationRatio > 0.95)
            {
                _logger.LogError("🚨 ディスク容量危険: {Available:F1}GB残り ({Utilization:P1}使用)",
                    availableSpaceGB, utilizationRatio);
                return false;
            }
            else if (utilizationRatio > 0.85)
            {
                _logger.LogWarning("⚠️ ディスク容量警告: {Available:F1}GB残り ({Utilization:P1}使用)",
                    availableSpaceGB, utilizationRatio);
            }
            else if (utilizationRatio > 0.7)
            {
                _logger.LogInformation("ℹ️ ディスク容量注意: {Available:F1}GB残り ({Utilization:P1}使用)",
                    availableSpaceGB, utilizationRatio);
            }
            else
            {
                _logger.LogDebug("💾 ディスク容量正常: {Available:F1}GB残り ({Utilization:P1}使用)",
                    availableSpaceGB, utilizationRatio);
            }

            return utilizationRatio < 0.9;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュ容量監視エラー");
            return false;
        }
    }

    /// <summary>
    /// 管理レベルを設定から取得
    /// </summary>
    private CacheManagementLevel GetManagementLevel()
    {
        var levelString = _configuration.GetValue("Translation:NLLB200:ModelCache:ManagementLevel", "Standard");
        return Enum.TryParse<CacheManagementLevel>(levelString, ignoreCase: true, out var level)
            ? level
            : CacheManagementLevel.Standard;
    }

    /// <summary>
    /// 最大キャッシュサイズを設定から取得（バイト単位）
    /// </summary>
    private long GetMaxCacheSizeBytes()
    {
        var maxSizeMB = _configuration.GetValue("Translation:NLLB200:ModelCache:EstimatedModelSizeMB", 2400);
        var maxSizeMultiplier = _configuration.GetValue("Translation:NLLB200:ModelCache:MaxSizeMultiplier", 2.0);
        return (long)(maxSizeMB * maxSizeMultiplier * 1024 * 1024);
    }

    /// <summary>
    /// 破損ファイル数をチェック
    /// </summary>
    private async Task<int> CheckCorruptedFilesAsync(string modelPath)
    {
        try
        {
            if (!Directory.Exists(modelPath))
                return 0;

            var requiredFiles = new[] { "config.json", "pytorch_model.bin", "tokenizer.json", "tokenizer_config.json" };
            var corruptedCount = 0;

            foreach (var fileName in requiredFiles)
            {
                var filePath = Path.Combine(modelPath, fileName);
                if (!File.Exists(filePath))
                {
                    corruptedCount++;
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    corruptedCount++;
                    _logger.LogWarning("🔍 空ファイル検出: {FilePath}", filePath);
                }
            }

            return corruptedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 破損ファイルチェック失敗");
            return 0;
        }
    }

    /// <summary>
    /// 古いファイル数をチェック
    /// </summary>
    private async Task<int> CheckObsoleteFilesAsync(string cacheDirectory)
    {
        try
        {
            if (!Directory.Exists(cacheDirectory))
                return 0;

            var cutoffDate = DateTime.UtcNow.AddDays(-_maxRetentionDays);
            var obsoleteFiles = Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    var fileInfo = new FileInfo(file);
                    return fileInfo.LastWriteTimeUtc < cutoffDate;
                })
                .ToList();

            if (obsoleteFiles.Count > 0)
            {
                _logger.LogDebug("📅 古いファイル検出: {Count}件 (保持期間: {RetentionDays}日)",
                    obsoleteFiles.Count, _maxRetentionDays);
            }

            return obsoleteFiles.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 古いファイルチェック失敗");
            return 0;
        }
    }

    /// <summary>
    /// 推奨アクションを生成
    /// </summary>
    private static string[] GenerateRecommendedActions(int corruptedCount, int obsoleteCount,
        double utilizationRatio, long availableSpace)
    {
        var actions = new List<string>();

        if (corruptedCount > 0)
            actions.Add($"破損ファイル({corruptedCount}件)の修復またはキャッシュ再生成");

        if (obsoleteCount > 10)
            actions.Add($"古いファイル({obsoleteCount}件)のクリーンアップ");

        if (utilizationRatio > 0.95)
            actions.Add("ディスク容量の確保（不要ファイルの削除）");
        else if (utilizationRatio > 0.85)
            actions.Add("ディスク容量の監視強化");

        if (availableSpace < 1024 * 1024 * 1024) // 1GB未満
            actions.Add("緊急: ディスク容量不足（1GB以下）");

        if (actions.Count == 0)
            actions.Add("定期監視継続");

        return [.. actions];
    }

    /// <summary>
    /// 破損ファイルの修復
    /// </summary>
    private async Task<bool> RepairCorruptedFilesAsync()
    {
        try
        {
            _logger.LogInformation("🔧 破損ファイル修復実行 - キャッシュ再生成");

            // 破損したキャッシュを削除して再生成を促す
            var cleanupSuccess = await _modelCacheManager.CleanupCacheAsync();

            if (cleanupSuccess)
            {
                _logger.LogInformation("✅ 破損キャッシュ削除完了 - 次回起動時に自動再生成されます");
                return true;
            }

            _logger.LogWarning("⚠️ 破損キャッシュ削除部分的成功");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 破損ファイル修復失敗");
            return false;
        }
    }
}

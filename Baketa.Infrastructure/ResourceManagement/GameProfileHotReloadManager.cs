using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// Phase 3: ゲーム別プロファイルホットリロードシステム
/// A/Bテスト機能とリアルタイム設定更新による動的最適化
/// </summary>
public sealed class GameProfileHotReloadManager : IDisposable
{
    private readonly ILogger<GameProfileHotReloadManager> _logger;
    private readonly IOptionsMonitor<HysteresisControlSettings> _hysteresisSettings;
    private readonly IOptionsMonitor<PredictiveControlSettings> _predictiveSettings;

    // プロファイル管理
    private readonly Dictionary<string, GameProfile> _gameProfiles = new();
    private readonly Dictionary<string, string> _activeVariants = new(); // Game -> Variant
    private readonly object _profileLock = new();

    // A/Bテスト管理
    private readonly Dictionary<string, AbTestConfiguration> _abTestConfigs = new();
    private readonly Dictionary<string, AbTestMetrics> _abTestMetrics = new();

    // ファイルシステム監視
    private FileSystemWatcher? _profileWatcher;
    private readonly string _profilesDirectory;
    private readonly System.Threading.Timer _performanceEvaluationTimer;

    // ホットリロード状態
    private readonly bool _hotReloadEnabled = true;
    private DateTime _lastReloadTime = DateTime.UtcNow;

    // 非同期初期化制御
    private readonly TaskCompletionSource<bool> _initializationComplete = new();
    private bool _isInitializationStarted;

    private readonly ILoggerFactory _loggerFactory;

    public GameProfileHotReloadManager(
        ILogger<GameProfileHotReloadManager> logger,
        ILoggerFactory loggerFactory,
        IOptionsMonitor<HysteresisControlSettings> hysteresisSettings,
        IOptionsMonitor<PredictiveControlSettings> predictiveSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _hysteresisSettings = hysteresisSettings ?? throw new ArgumentNullException(nameof(hysteresisSettings));
        _predictiveSettings = predictiveSettings ?? throw new ArgumentNullException(nameof(predictiveSettings));

        // プロファイル保存ディレクトリの設定
        _profilesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Baketa", "GameProfiles");

        Directory.CreateDirectory(_profilesDirectory);

        // パフォーマンス評価タイマー（5分ごと）
        _performanceEvaluationTimer = new System.Threading.Timer(EvaluatePerformanceAndAdjust, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        _logger.LogInformation("🎮 [PHASE3] ゲーム別プロファイルホットリロードシステム初期化完了");

        // 安全な非同期初期化の開始
        StartSafeInitializationAsync();
    }

    /// <summary>
    /// 非同期初期化の安全な開始
    /// </summary>
    private void StartSafeInitializationAsync()
    {
        if (_isInitializationStarted) return;

        _isInitializationStarted = true;

        // 安全な非同期初期化（例外処理付き）
        Task.Run(async () =>
        {
            try
            {
                await InitializeProfilesAsync().ConfigureAwait(false);
                _initializationComplete.SetResult(true);
                _logger.LogDebug("🔧 [PHASE3] 非同期初期化完了");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [PHASE3] 非同期初期化エラー");
                _initializationComplete.SetException(ex);
            }
        });
    }

    /// <summary>
    /// 初期化完了の待機
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            combinedCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30秒タイムアウト

            await _initializationComplete.Task.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("⚠️ [PHASE3] 初期化タイムアウト、デフォルト動作で続行");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "⚠️ [PHASE3] 初期化待機エラー、デフォルト動作で続行");
        }
    }

    /// <summary>
    /// ゲームプロファイルの動的取得
    /// </summary>
    public async Task<GameProfile> GetGameProfileAsync(string gameProcessName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        lock (_profileLock)
        {
            if (_gameProfiles.TryGetValue(gameProcessName, out var profile))
            {
                return profile;
            }

            // プロファイルが存在しない場合はデフォルト作成
            var defaultProfile = CreateDefaultGameProfile(gameProcessName);
            _gameProfiles[gameProcessName] = defaultProfile;

            _logger.LogInformation("🆕 [PHASE3] デフォルトゲームプロファイル作成: {GameName}", gameProcessName);

            // 非同期でファイルに保存
            Task.Run(async () => await SaveGameProfileAsync(defaultProfile).ConfigureAwait(false));

            return defaultProfile;
        }
    }

    /// <summary>
    /// ゲームプロファイルの動的取得（同期版 - 後方互換性のため）
    /// </summary>
    public GameProfile GetGameProfile(string gameProcessName)
    {
        return GetGameProfileAsync(gameProcessName).GetAwaiter().GetResult();
    }

    /// <summary>
    /// A/Bテスト設定の適用と管理
    /// </summary>
    public async Task<string> ApplyAbTestVariantAsync(string gameProcessName, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_profileLock)
            {
                // 既にアクティブなバリアントがある場合はそれを返す
                if (_activeVariants.TryGetValue(gameProcessName, out var activeVariant))
                {
                    return activeVariant;
                }
            }

            // A/Bテスト設定の確認
            if (!_abTestConfigs.TryGetValue(gameProcessName, out var abConfig))
            {
                // デフォルトA/Bテスト設定作成
                abConfig = CreateDefaultAbTestConfiguration(gameProcessName);
                _abTestConfigs[gameProcessName] = abConfig;
            }

            // バリアント選択ロジック
            var selectedVariant = SelectOptimalVariant(gameProcessName, abConfig);

            lock (_profileLock)
            {
                _activeVariants[gameProcessName] = selectedVariant;
            }

            // バリアント適用とメトリクス記録開始
            await ApplyVariantConfigurationAsync(gameProcessName, selectedVariant, cancellationToken).ConfigureAwait(false);
            InitializeAbTestMetrics(gameProcessName, selectedVariant);

            _logger.LogInformation("🔬 [PHASE3] A/Bテストバリアント適用: {GameName} → {Variant}",
                gameProcessName, selectedVariant);

            return selectedVariant;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] A/Bテストバリアント適用エラー: {GameName}", gameProcessName);
            return "Default";
        }
    }

    /// <summary>
    /// パフォーマンスメトリクスの記録
    /// </summary>
    public void RecordPerformanceMetrics(string gameProcessName, PerformanceMetrics metrics)
    {
        try
        {
            var variant = _activeVariants.TryGetValue(gameProcessName, out var v) ? v : "Default";

            if (_abTestMetrics.TryGetValue($"{gameProcessName}_{variant}", out var abMetrics))
            {
                lock (abMetrics)
                {
                    abMetrics.TotalMeasurements++;
                    abMetrics.TotalCooldownTime += metrics.CooldownTime;
                    abMetrics.TotalProcessingTime += metrics.ProcessingTime;
                    abMetrics.SuccessfulOperations += metrics.WasSuccessful ? 1 : 0;

                    // GPU温度とVRAM使用率の追跡
                    abMetrics.AverageGpuTemperature = (abMetrics.AverageGpuTemperature * (abMetrics.TotalMeasurements - 1) +
                        metrics.GpuTemperature) / abMetrics.TotalMeasurements;
                    abMetrics.AverageVramUsage = (abMetrics.AverageVramUsage * (abMetrics.TotalMeasurements - 1) +
                        metrics.VramUsagePercent) / abMetrics.TotalMeasurements;

                    abMetrics.LastUpdateTime = DateTime.UtcNow;
                }

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("📊 [PHASE3] パフォーマンスメトリクス記録: {GameName}_{Variant}, 成功率: {SuccessRate:P2}",
                        gameProcessName, variant, (double)abMetrics.SuccessfulOperations / abMetrics.TotalMeasurements);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] パフォーマンスメトリクス記録エラー: {GameName}", gameProcessName);
        }
    }

    /// <summary>
    /// プロファイルの手動リロード
    /// </summary>
    public async Task ReloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("🔄 [PHASE3] ゲームプロファイル手動リロード開始");

            var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");
            var reloadedCount = 0;

            foreach (var filePath in profileFiles)
            {
                try
                {
                    var profile = await LoadGameProfileFromFileAsync(filePath, cancellationToken).ConfigureAwait(false);
                    if (profile != null)
                    {
                        lock (_profileLock)
                        {
                            _gameProfiles[profile.GameProcessName] = profile;
                        }
                        reloadedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [PHASE3] プロファイルファイル読み込み失敗: {FilePath}", filePath);
                }
            }

            _lastReloadTime = DateTime.UtcNow;
            _logger.LogInformation("✅ [PHASE3] ゲームプロファイルリロード完了: {ReloadedCount}/{TotalCount}",
                reloadedCount, profileFiles.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] ゲームプロファイルリロードエラー");
            throw;
        }
    }

    /// <summary>
    /// A/Bテスト結果の統計情報取得（統計的有意性検定付き）
    /// </summary>
    public AbTestSummary GetAbTestSummary(string gameProcessName)
    {
        var variants = _abTestMetrics
            .Where(kvp => kvp.Key.StartsWith($"{gameProcessName}_", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key.Substring($"{gameProcessName}_".Length), kvp => kvp.Value);

        if (!variants.Any())
        {
            return new AbTestSummary(gameProcessName, [], "Default", DateTime.UtcNow, null);
        }

        var variantResults = variants.Select(kvp => new VariantResult(
            VariantName: kvp.Key,
            TotalMeasurements: kvp.Value.TotalMeasurements,
            SuccessRate: kvp.Value.TotalMeasurements > 0 ? (double)kvp.Value.SuccessfulOperations / kvp.Value.TotalMeasurements : 0,
            AverageCooldownTime: kvp.Value.TotalMeasurements > 0 ? kvp.Value.TotalCooldownTime / kvp.Value.TotalMeasurements : TimeSpan.Zero,
            AverageProcessingTime: kvp.Value.TotalMeasurements > 0 ? kvp.Value.TotalProcessingTime / kvp.Value.TotalMeasurements : TimeSpan.Zero,
            AverageGpuTemperature: kvp.Value.AverageGpuTemperature,
            AverageVramUsage: kvp.Value.AverageVramUsage
        )).ToList();

        // 統計的分析実行（2つ以上のバリアントがある場合）
        StatisticalTestResult? statisticalResult = null;
        if (variantResults.Count >= 2)
        {
            var statisticalAnalyzer = new StatisticalAnalyzer(_loggerFactory.CreateLogger<StatisticalAnalyzer>());

            // 最も有望な2つのバリアントを比較
            var topTwoVariants = variantResults
                .Where(v => v.TotalMeasurements >= 10)
                .OrderByDescending(v => v.SuccessRate)
                .ThenBy(v => v.AverageCooldownTime)
                .Take(2)
                .ToList();

            if (topTwoVariants.Count == 2)
            {
                statisticalResult = statisticalAnalyzer.CompareVariants(topTwoVariants[0], topTwoVariants[1]);
                _logger.LogInformation("📊 [STATS] 統計検定結果 - {Game}: {TestType}, p={PValue:F6}, 有意差={IsSignificant}, 効果量={EffectSize:F3}({EffectCategory}), 推奨={Recommendation}",
                    gameProcessName, statisticalResult.TestType, statisticalResult.PValue,
                    statisticalResult.IsSignificant, statisticalResult.EffectSize, statisticalResult.EffectSizeCategory,
                    statisticalResult.Recommendation);
            }
        }

        // 統計的有意性を考慮した最適バリアント決定
        var optimalVariant = DetermineOptimalVariantWithStatistics(variantResults, statisticalResult);

        return new AbTestSummary(gameProcessName, variantResults, optimalVariant, DateTime.UtcNow, statisticalResult);
    }

    /// <summary>
    /// 統計的有意性を考慮した最適バリアント決定
    /// </summary>
    private static string DetermineOptimalVariantWithStatistics(
        List<VariantResult> variantResults,
        StatisticalTestResult? statisticalResult)
    {
        var validVariants = variantResults.Where(v => v.TotalMeasurements >= 10).ToList();
        if (!validVariants.Any())
        {
            return "Default";
        }

        // 統計的有意差がある場合、それを優先
        if (statisticalResult?.IsSignificant == true &&
            statisticalResult.EffectSizeCategory >= EffectSizeCategory.Small)
        {
            // より高い成功率を持つバリアントを選択
            return validVariants
                .OrderByDescending(v => v.SuccessRate)
                .ThenBy(v => v.AverageCooldownTime)
                .First()
                .VariantName;
        }

        // 統計的有意差がない場合、保守的にDefault選択またはより安全なバリアント選択
        if (statisticalResult != null && !statisticalResult.IsSignificant)
        {
            var defaultVariant = validVariants.FirstOrDefault(v => v.VariantName == "Default");
            if (defaultVariant != null)
            {
                return "Default";
            }
        }

        // フォールバック：従来ロジック
        return validVariants
            .OrderByDescending(v => v.SuccessRate)
            .ThenBy(v => v.AverageCooldownTime)
            .First()
            .VariantName;
    }

    private async Task InitializeProfilesAsync()
    {
        try
        {
            // ファイルシステム監視の開始
            InitializeFileSystemWatcher();

            // 既存プロファイルの読み込み
            await ReloadProfilesAsync().ConfigureAwait(false);

            _logger.LogInformation("🔧 [PHASE3] プロファイルシステム初期化完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] プロファイルシステム初期化エラー");
        }
    }

    private void InitializeFileSystemWatcher()
    {
        try
        {
            _profileWatcher = new FileSystemWatcher(_profilesDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = _hotReloadEnabled
            };

            _profileWatcher.Changed += OnProfileFileChanged;
            _profileWatcher.Created += OnProfileFileChanged;
            _profileWatcher.Deleted += OnProfileFileDeleted;

            _logger.LogDebug("👁️ [PHASE3] ファイルシステム監視開始: {Directory}", _profilesDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] ファイルシステム監視初期化エラー");
        }
    }

    private async void OnProfileFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_hotReloadEnabled) return;

        try
        {
            // ファイルロックを避けるため少し待機
            await Task.Delay(500).ConfigureAwait(false);

            var profile = await LoadGameProfileFromFileAsync(e.FullPath).ConfigureAwait(false);
            if (profile != null)
            {
                lock (_profileLock)
                {
                    _gameProfiles[profile.GameProcessName] = profile;
                }

                _logger.LogInformation("🔄 [PHASE3] プロファイル自動リロード: {GameName} ({FilePath})",
                    profile.GameProcessName, e.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] プロファイル自動リロードエラー: {FilePath}", e.FullPath);
        }
    }

    private void OnProfileFileDeleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            var gameName = Path.GetFileNameWithoutExtension(e.Name);

            lock (_profileLock)
            {
                if (_gameProfiles.Remove(gameName))
                {
                    _logger.LogInformation("🗑️ [PHASE3] プロファイル削除検出: {GameName}", gameName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] プロファイル削除処理エラー: {FilePath}", e.FullPath);
        }
    }

    private static GameProfile CreateDefaultGameProfile(string gameProcessName)
    {
        return new GameProfile(
            GameProcessName: gameProcessName,
            HysteresisSettings: HysteresisControlSettings.Default,
            PredictiveSettings: PredictiveControlSettings.Default,
            CustomThresholds: new Dictionary<string, double>
            {
                ["CpuHighThreshold"] = 80.0,
                ["MemoryHighThreshold"] = 85.0,
                ["GpuHighThreshold"] = 80.0,
                ["VramHighThreshold"] = 75.0
            },
            IsEnabled: true,
            Priority: GameProfilePriority.Normal,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );
    }

    private static AbTestConfiguration CreateDefaultAbTestConfiguration(string gameProcessName)
    {
        return new AbTestConfiguration(
            GameProcessName: gameProcessName,
            Variants: ["Conservative", "Default", "Aggressive"],
            TrafficSplit: [0.3, 0.4, 0.3], // Conservative 30%, Default 40%, Aggressive 30%
            MinSampleSize: 20,
            EvaluationInterval: TimeSpan.FromMinutes(10),
            StatisticalSignificanceThreshold: 0.05,
            IsEnabled: true
        );
    }

    private string SelectOptimalVariant(string gameProcessName, AbTestConfiguration abConfig)
    {
        if (!abConfig.IsEnabled || !abConfig.Variants.Any())
            return "Default";

        // 既存のパフォーマンスデータに基づく最適バリアント選択
        var bestVariant = abConfig.Variants
            .Select(variant =>
            {
                var metricsKey = $"{gameProcessName}_{variant}";
                if (_abTestMetrics.TryGetValue(metricsKey, out var metrics) && metrics.TotalMeasurements >= abConfig.MinSampleSize)
                {
                    var successRate = (double)metrics.SuccessfulOperations / metrics.TotalMeasurements;
                    return new { Variant = variant, SuccessRate = successRate };
                }
                return new { Variant = variant, SuccessRate = 0.0 };
            })
            .OrderByDescending(v => v.SuccessRate)
            .First();

        // 十分なサンプルがない場合はランダム選択（トラフィック分割に基づく）
        if (bestVariant.SuccessRate == 0.0)
        {
            var random = new Random();
            var randomValue = random.NextDouble();
            var cumulative = 0.0;

            for (int i = 0; i < abConfig.Variants.Count && i < abConfig.TrafficSplit.Count; i++)
            {
                cumulative += abConfig.TrafficSplit[i];
                if (randomValue <= cumulative)
                    return abConfig.Variants[i];
            }
        }

        return bestVariant.Variant;
    }

    private async Task ApplyVariantConfigurationAsync(string gameProcessName, string variant, CancellationToken cancellationToken)
    {
        try
        {
            // バリアント固有の設定適用ロジック
            var profile = GetGameProfile(gameProcessName);

            // バリアントに基づく設定調整
            var adjustedSettings = variant switch
            {
                "Conservative" => HysteresisControlSettings.Conservative,
                "Aggressive" => HysteresisControlSettings.Aggressive,
                "Rtx4070Optimized" => HysteresisControlSettings.Rtx4070Optimized,
                _ => HysteresisControlSettings.Default
            };

            // プロファイルの更新（必要に応じて）
            var updatedProfile = profile with { HysteresisSettings = adjustedSettings };

            lock (_profileLock)
            {
                _gameProfiles[gameProcessName] = updatedProfile;
            }

            await Task.CompletedTask; // 非同期インターフェース対応
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] バリアント設定適用エラー: {GameName}_{Variant}", gameProcessName, variant);
        }
    }

    private void InitializeAbTestMetrics(string gameProcessName, string variant)
    {
        var key = $"{gameProcessName}_{variant}";
        if (!_abTestMetrics.ContainsKey(key))
        {
            _abTestMetrics[key] = new AbTestMetrics();
        }
    }

    private async Task<GameProfile?> LoadGameProfileFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            return JsonSerializer.Deserialize<GameProfile>(jsonContent, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] プロファイルファイル読み込みエラー: {FilePath}", filePath);
            return null;
        }
    }

    private async Task SaveGameProfileAsync(GameProfile profile)
    {
        try
        {
            var filePath = Path.Combine(_profilesDirectory, $"{profile.GameProcessName}.json");

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var jsonContent = JsonSerializer.Serialize(profile, options);
            await File.WriteAllTextAsync(filePath, jsonContent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] プロファイルファイル保存エラー: {GameName}", profile.GameProcessName);
        }
    }

    private async void EvaluatePerformanceAndAdjust(object? state)
    {
        try
        {
            // 定期的なパフォーマンス評価と自動調整
            var gameNames = _activeVariants.Keys.ToList();

            foreach (var gameName in gameNames)
            {
                var summary = GetAbTestSummary(gameName);

                // 統計的有意性チェックと最適バリアント切り替え
                if (summary.VariantResults.Count > 1 && ShouldSwitchVariant(summary))
                {
                    var newVariant = summary.OptimalVariant;
                    if (newVariant != _activeVariants.GetValueOrDefault(gameName))
                    {
                        await ApplyAbTestVariantAsync(gameName).ConfigureAwait(false);
                        _logger.LogInformation("📊 [PHASE3] パフォーマンスに基づくバリアント自動切替: {GameName} → {NewVariant}",
                            gameName, newVariant);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] パフォーマンス評価・調整エラー");
        }
    }

    private static bool ShouldSwitchVariant(AbTestSummary summary)
    {
        // 統計的有意性の簡単なチェック
        var bestVariant = summary.VariantResults
            .Where(v => v.TotalMeasurements >= 20)
            .OrderByDescending(v => v.SuccessRate)
            .FirstOrDefault();

        if (bestVariant == null) return false;

        var currentOptimal = summary.VariantResults.FirstOrDefault(v => v.VariantName == summary.OptimalVariant);

        return bestVariant != currentOptimal && bestVariant.SuccessRate > (currentOptimal?.SuccessRate ?? 0) + 0.05; // 5%以上の改善
    }

    public void Dispose()
    {
        _profileWatcher?.Dispose();
        _performanceEvaluationTimer?.Dispose();

        // プロファイルの最終保存
        Task.Run(async () =>
        {
            foreach (var profile in _gameProfiles.Values)
            {
                await SaveGameProfileAsync(profile).ConfigureAwait(false);
            }
        });

        _logger.LogInformation("🔄 [PHASE3] ゲーム別プロファイルホットリロードシステム正常終了");
    }
}

/// <summary>
/// ゲームプロファイル設定
/// </summary>
public sealed record GameProfile(
    string GameProcessName,
    HysteresisControlSettings HysteresisSettings,
    PredictiveControlSettings PredictiveSettings,
    Dictionary<string, double> CustomThresholds,
    bool IsEnabled,
    GameProfilePriority Priority,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// ゲームプロファイル優先度
/// </summary>
public enum GameProfilePriority
{
    Low,
    Normal,
    High,
    Critical
}

/// <summary>
/// A/Bテスト設定
/// </summary>
internal sealed record AbTestConfiguration(
    string GameProcessName,
    List<string> Variants,
    List<double> TrafficSplit,
    int MinSampleSize,
    TimeSpan EvaluationInterval,
    double StatisticalSignificanceThreshold,
    bool IsEnabled
);

/// <summary>
/// A/Bテストメトリクス
/// </summary>
internal sealed class AbTestMetrics
{
    public int TotalMeasurements { get; set; }
    public int SuccessfulOperations { get; set; }
    public TimeSpan TotalCooldownTime { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public double AverageGpuTemperature { get; set; }
    public double AverageVramUsage { get; set; }
    public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// パフォーマンスメトリクス
/// </summary>
public sealed record PerformanceMetrics(
    TimeSpan CooldownTime,
    TimeSpan ProcessingTime,
    double GpuTemperature,
    double VramUsagePercent,
    bool WasSuccessful
);

/// <summary>
/// A/Bテスト結果サマリー
/// </summary>
public sealed record AbTestSummary(
    string GameProcessName,
    List<VariantResult> VariantResults,
    string OptimalVariant,
    DateTime GeneratedAt,
    StatisticalTestResult? StatisticalResult = null
);

/// <summary>
/// バリアント結果
/// </summary>
public sealed record VariantResult(
    string VariantName,
    int TotalMeasurements,
    double SuccessRate,
    TimeSpan AverageCooldownTime,
    TimeSpan AverageProcessingTime,
    double AverageGpuTemperature,
    double AverageVramUsage
);

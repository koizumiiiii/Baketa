using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi.SeedProfiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Roi;

/// <summary>
/// ROI管理サービスの実装
/// </summary>
/// <remarks>
/// ROI領域の管理、学習データの統合、動的閾値の提供を担当します。
/// </remarks>
public sealed class RoiManager : IRoiManager, IDisposable
{
    private readonly ILogger<RoiManager> _logger;
    private readonly IRoiLearningEngine _learningEngine;
    private readonly IRoiProfileService? _profileService;
    private readonly RoiManagerSettings _settings;
    private readonly object _lock = new();

    private RoiProfile? _currentProfile;
    private readonly List<NormalizedRect> _exclusionZones = [];
    private readonly System.Threading.Timer? _decayTimer;
    private readonly System.Threading.Timer? _autoSaveTimer;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RoiManager(
        ILogger<RoiManager> logger,
        IRoiLearningEngine learningEngine,
        IOptions<RoiManagerSettings> settings,
        IRoiProfileService? profileService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _learningEngine = learningEngine ?? throw new ArgumentNullException(nameof(learningEngine));
        _profileService = profileService;
        _settings = settings?.Value ?? RoiManagerSettings.CreateDefault();

        // 減衰タイマーを設定
        if (_settings.Enabled && _settings.DecayIntervalSeconds > 0)
        {
            _decayTimer = new System.Threading.Timer(
                OnDecayTimerElapsed,
                null,
                TimeSpan.FromSeconds(_settings.DecayIntervalSeconds),
                TimeSpan.FromSeconds(_settings.DecayIntervalSeconds));
        }

        // 自動保存タイマーを設定
        if (_settings.Enabled && _profileService != null && _settings.AutoSaveIntervalSeconds > 0)
        {
            _autoSaveTimer = new System.Threading.Timer(
                OnAutoSaveTimerElapsed,
                null,
                TimeSpan.FromSeconds(_settings.AutoSaveIntervalSeconds),
                TimeSpan.FromSeconds(_settings.AutoSaveIntervalSeconds));
        }

        _logger.LogInformation(
            "RoiManager initialized: Enabled={Enabled}, AutoLearning={AutoLearning}",
            _settings.Enabled, _settings.AutoLearningEnabled);
    }

    /// <inheritdoc />
    public RoiProfile? CurrentProfile
    {
        get
        {
            lock (_lock)
            {
                return _currentProfile;
            }
        }
    }

    /// <inheritdoc />
    public bool IsEnabled => _settings.Enabled;

    /// <inheritdoc />
    public event EventHandler<RoiProfileChangedEventArgs>? ProfileChanged;

    /// <inheritdoc />
    public async Task<RoiProfile?> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        if (_profileService == null)
        {
            _logger.LogWarning("Profile service not available, cannot load profile");
            return null;
        }

        try
        {
            var profile = await _profileService.LoadProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (profile != null)
            {
                SetCurrentProfile(profile, RoiProfileChangeType.Loaded);
            }

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profile: {ProfileId}", profileId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        RoiProfile? profileToSave;
        lock (_lock)
        {
            if (_currentProfile == null)
            {
                _logger.LogDebug("No current profile to save");
                return;
            }

            // 現在の学習データでプロファイルを更新
            profileToSave = UpdateProfileWithLearningData(_currentProfile);
            _currentProfile = profileToSave;
        }

        if (_profileService != null)
        {
            try
            {
                await _profileService.SaveProfileAsync(profileToSave, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Saved profile: {ProfileId}", profileToSave.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save profile: {ProfileId}", profileToSave.Id);
            }
        }
    }

    /// <inheritdoc />
    public string ComputeProfileId(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }

        // パスを正規化
        var normalizedPath = executablePath.ToLowerInvariant().Replace('/', '\\');

        // SHA256ハッシュを計算
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public async Task<RoiProfile> GetOrCreateProfileAsync(
        string executablePath,
        string windowTitle,
        CancellationToken cancellationToken = default)
    {
        var profileId = ComputeProfileId(executablePath);

        // 既存プロファイルを検索
        if (_profileService != null)
        {
            var existing = await _profileService.LoadProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                SetCurrentProfile(existing, RoiProfileChangeType.Loaded);
                return existing;
            }
        }

        // 新規プロファイルを作成
        var name = !string.IsNullOrWhiteSpace(windowTitle)
            ? windowTitle
            : System.IO.Path.GetFileNameWithoutExtension(executablePath);

        RoiProfile newProfile;

        // [Issue #369] シードプロファイル適用
        if (_settings.EnableSeedProfile)
        {
            newProfile = SeedProfileProvider.CreateSeededProfile(
                profileId,
                name ?? "Unknown",
                executablePath,
                windowTitle,
                _settings);

            _logger.LogInformation(
                "Seed profile applied: Name={Name}, Regions={RegionCount}, HeatmapCells={HeatmapCells}",
                name,
                newProfile.Regions.Count,
                newProfile.HeatmapData?.Values.Count(v => v > 0) ?? 0);
        }
        else
        {
            // シード無効時は空プロファイルを作成（従来動作）
            newProfile = RoiProfile.Create(
                profileId,
                name ?? "Unknown",
                executablePath,
                windowTitle);

            _logger.LogDebug(
                "Seed profile disabled, creating empty profile: Name={Name}",
                name);
        }

        SetCurrentProfile(newProfile, RoiProfileChangeType.Created);

        // 新規プロファイルを保存
        if (_profileService != null)
        {
            try
            {
                await _profileService.SaveProfileAsync(newProfile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save new profile: {ProfileId}", profileId);
            }
        }

        return newProfile;
    }

    /// <inheritdoc />
    public RoiRegion? GetRegionAt(NormalizedRect normalizedBounds)
    {
        lock (_lock)
        {
            if (_currentProfile == null)
            {
                return null;
            }

            return _currentProfile.FindOverlappingRegions(normalizedBounds, minIoU: 0.3f).FirstOrDefault();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RoiRegion> GetAllRegions()
    {
        lock (_lock)
        {
            return _currentProfile?.Regions ?? [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RoiRegion> GetHighConfidenceRegions(float minConfidence = 0.7f)
    {
        lock (_lock)
        {
            if (_currentProfile?.Regions is not { Count: > 0 } regions)
            {
                return [];
            }

            // [Issue #324] 高信頼度領域のみをフィルタリング
            // - ConfidenceLevel == High または ConfidenceScore >= minConfidence
            // - 検出回数が一定以上（安定した領域）
            var highConfidenceRegions = regions
                .Where(r => r.ConfidenceLevel == RoiConfidenceLevel.High ||
                           r.ConfidenceScore >= minConfidence)
                .Where(r => r.DetectionCount >= _settings.MinDetectionCountForHighConfidence)
                .ToList();

            _logger.LogDebug(
                "[Issue #324] GetHighConfidenceRegions: {Count}/{Total} regions (minConfidence={MinConf})",
                highConfidenceRegions.Count, regions.Count, minConfidence);

            return highConfidenceRegions;
        }
    }

    /// <inheritdoc />
    public bool IsLearningComplete
    {
        get
        {
            lock (_lock)
            {
                if (_currentProfile == null)
                {
                    return false;
                }

                // [Issue #324] 学習完了条件:
                // 1. 高信頼度領域が1つ以上存在
                // 2. 学習セッション数が閾値以上
                var highConfidenceCount = GetHighConfidenceRegions().Count;
                var learningComplete = highConfidenceCount >= _settings.MinHighConfidenceRegionsForComplete &&
                                      _currentProfile.TotalLearningSessionCount >= _settings.MinLearningSessionsForComplete;

                return learningComplete;
            }
        }
    }

    // [Issue #293] 動的閾値ログのサンプリングカウンター
    private int _thresholdLogCounter = 0;
    private const int ThresholdLogSampleRate = 16; // 16セルに1回ログ出力

    /// <inheritdoc />
    public float GetThresholdAt(float normalizedX, float normalizedY, float defaultThreshold)
    {
        if (!_settings.Enabled || !_settings.EnableDynamicThreshold)
        {
            return defaultThreshold;
        }

        var heatmapValue = _learningEngine.GetHeatmapValueAt(normalizedX, normalizedY);

        // ヒートマップ値に基づいて閾値を調整
        float multiplier;
        string zone;
        if (heatmapValue >= _settings.HighConfidenceThreshold)
        {
            // 高ヒートマップ領域: より厳しい閾値
            multiplier = _settings.HighHeatmapThresholdMultiplier;
            zone = "HIGH";
        }
        else if (heatmapValue >= _settings.MinConfidenceForRegion)
        {
            // 中ヒートマップ領域: 線形補間
            var ratio = (heatmapValue - _settings.MinConfidenceForRegion) /
                       (_settings.HighConfidenceThreshold - _settings.MinConfidenceForRegion);
            multiplier = _settings.LowHeatmapThresholdMultiplier +
                        ratio * (_settings.HighHeatmapThresholdMultiplier - _settings.LowHeatmapThresholdMultiplier);
            zone = "MID";
        }
        else
        {
            // 低ヒートマップ領域: より緩い閾値
            multiplier = _settings.LowHeatmapThresholdMultiplier;
            zone = "LOW";
        }

        var adjustedThreshold = Math.Clamp(defaultThreshold * multiplier, 0.0f, 1.0f);

        // [Issue #293] サンプリングログ: 16セルに1回、または閾値が調整された場合にログ出力
        _thresholdLogCounter++;
        if (_thresholdLogCounter >= ThresholdLogSampleRate)
        {
            _thresholdLogCounter = 0;
            _logger.LogDebug(
                "[Issue #293] 動的閾値: Pos=({X:F2},{Y:F2}), Heatmap={Heatmap:F2}, Zone={Zone}, Threshold={Default:F3}→{Adjusted:F3} (×{Mult:F2})",
                normalizedX, normalizedY, heatmapValue, zone, defaultThreshold, adjustedThreshold, multiplier);
        }

        return adjustedThreshold;
    }

    /// <inheritdoc />
    public float GetHeatmapValueAt(float normalizedX, float normalizedY)
    {
        if (!_settings.Enabled)
        {
            return 0.0f;
        }

        // 除外ゾーンチェック
        if (IsInExclusionZone(normalizedX, normalizedY))
        {
            return 0.0f;
        }

        return _learningEngine.GetHeatmapValueAt(normalizedX, normalizedY);
    }

    /// <inheritdoc />
    public void ReportTextDetection(NormalizedRect normalizedBounds, float confidence)
    {
        if (!_settings.Enabled || !_settings.AutoLearningEnabled)
        {
            return;
        }

        // 除外ゾーンチェック
        if (IsInExclusionZone(normalizedBounds.CenterX, normalizedBounds.CenterY))
        {
            return;
        }

        _learningEngine.RecordDetection(normalizedBounds, confidence);
    }

    /// <inheritdoc />
    public void ReportTextDetections(
        IEnumerable<(NormalizedRect bounds, float confidence)> detections,
        IReadOnlyList<NormalizedRect>? changedRegions = null)
    {
        if (!_settings.Enabled || !_settings.AutoLearningEnabled)
        {
            _logger.LogDebug(
                "[Issue #293] ReportTextDetections skipped: Enabled={Enabled}, AutoLearningEnabled={AutoLearning}",
                _settings.Enabled, _settings.AutoLearningEnabled);
            return;
        }

        // プロファイルがない場合は記録しない（ReportTextDetectionsAsyncを使用すべき）
        lock (_lock)
        {
            if (_currentProfile == null)
            {
                _logger.LogDebug("[Issue #293] ReportTextDetections: プロファイルがないためスキップ（ReportTextDetectionsAsyncを使用してください）");
                return;
            }
        }

        // 除外ゾーン内の検出を除外
        var filteredDetections = detections
            .Where(d => !IsInExclusionZone(d.bounds.CenterX, d.bounds.CenterY))
            .ToList();

        // [Issue #354] 変化領域との照合
        if (changedRegions != null && changedRegions.Count > 0)
        {
            var originalCount = filteredDetections.Count;
            filteredDetections = filteredDetections
                .Where(d => IsInChangedRegion(d.bounds, changedRegions))
                .ToList();

            _logger.LogDebug(
                "[Issue #354] 変化領域フィルタ適用: {Original}個 → {Filtered}個 (除外: {Excluded}個)",
                originalCount, filteredDetections.Count, originalCount - filteredDetections.Count);
        }

        if (filteredDetections.Count > 0)
        {
            _learningEngine.RecordDetections(filteredDetections);
            _logger.LogInformation(
                "[Issue #293] ROI学習記録完了: {Count}個の検出をヒートマップに記録",
                filteredDetections.Count);
        }
    }

    /// <summary>
    /// [Issue #354] 検出領域が変化領域と重なるかをチェック
    /// </summary>
    private static bool IsInChangedRegion(NormalizedRect bounds, IReadOnlyList<NormalizedRect> changedRegions)
    {
        foreach (var changedRegion in changedRegions)
        {
            // 矩形の交差判定（AABB交差）
            if (bounds.X < changedRegion.Right &&
                bounds.Right > changedRegion.X &&
                bounds.Y < changedRegion.Bottom &&
                bounds.Bottom > changedRegion.Y)
            {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public void ReportTextDetectionsWithWeight(
        IEnumerable<(NormalizedRect bounds, float confidence, int weight)> detections,
        IReadOnlyList<NormalizedRect>? changedRegions = null)
    {
        if (!_settings.Enabled || !_settings.AutoLearningEnabled)
        {
            _logger.LogDebug(
                "[Issue #354] ReportTextDetectionsWithWeight skipped: Enabled={Enabled}, AutoLearningEnabled={AutoLearning}",
                _settings.Enabled, _settings.AutoLearningEnabled);
            return;
        }

        // プロファイルがない場合は記録しない
        lock (_lock)
        {
            if (_currentProfile == null)
            {
                _logger.LogDebug("[Issue #354] ReportTextDetectionsWithWeight: プロファイルがないためスキップ");
                return;
            }
        }

        // 除外ゾーン内の検出を除外
        var filteredDetections = detections
            .Where(d => !IsInExclusionZone(d.bounds.CenterX, d.bounds.CenterY))
            .ToList();

        // 変化領域との照合
        if (changedRegions != null && changedRegions.Count > 0)
        {
            var originalCount = filteredDetections.Count;
            filteredDetections = filteredDetections
                .Where(d => IsInChangedRegion(d.bounds, changedRegions))
                .ToList();

            _logger.LogDebug(
                "[Issue #354] 変化領域フィルタ適用（重み付き）: {Original}個 → {Filtered}個 (除外: {Excluded}個)",
                originalCount, filteredDetections.Count, originalCount - filteredDetections.Count);
        }

        if (filteredDetections.Count > 0)
        {
            _learningEngine.RecordDetectionsWithWeight(filteredDetections);
            var avgWeight = filteredDetections.Average(d => d.weight);
            _logger.LogInformation(
                "[Issue #354] ROI学習記録完了（重み付き）: {Count}個の検出をヒートマップに記録 (平均weight={AvgWeight:F1})",
                filteredDetections.Count, avgWeight);
        }
    }

    /// <inheritdoc />
    public async Task ReportTextDetectionsAsync(
        IEnumerable<(NormalizedRect bounds, float confidence)> detections,
        IntPtr windowHandle,
        string windowTitle,
        string executablePath,
        IReadOnlyList<NormalizedRect>? changedRegions = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || !_settings.AutoLearningEnabled)
        {
            _logger.LogDebug(
                "[Issue #293] ReportTextDetectionsAsync skipped: Enabled={Enabled}, AutoLearningEnabled={AutoLearning}",
                _settings.Enabled, _settings.AutoLearningEnabled);
            return;
        }

        // ウィンドウ情報からプロファイルを取得または作成
        var profile = await GetOrCreateProfileAsync(executablePath, windowTitle, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[Issue #293] ROI学習: Profile={ProfileName} (Id={ProfileId}), Window='{WindowTitle}', Exe={ExePath}",
            profile.Name, profile.Id, windowTitle, executablePath);

        // 除外ゾーン内の検出を除外
        var detectionsList = detections.ToList();
        var filteredDetections = detectionsList
            .Where(d => !IsInExclusionZone(d.bounds.CenterX, d.bounds.CenterY))
            .ToList();

        // [Issue #354] 変化領域との照合
        if (changedRegions != null && changedRegions.Count > 0)
        {
            var originalCount = filteredDetections.Count;
            filteredDetections = filteredDetections
                .Where(d => IsInChangedRegion(d.bounds, changedRegions))
                .ToList();

            _logger.LogDebug(
                "[Issue #354] 変化領域フィルタ適用: {Original}個 → {Filtered}個 (除外: {Excluded}個)",
                originalCount, filteredDetections.Count, originalCount - filteredDetections.Count);
        }

        if (filteredDetections.Count > 0)
        {
            _learningEngine.RecordDetections(filteredDetections);
            _logger.LogInformation(
                "[Issue #293] ROI学習記録完了: {Count}個の検出をヒートマップに記録 (Profile={ProfileName})",
                filteredDetections.Count, profile.Name);
        }

        // 学習後にプロファイルを保存（学習データを永続化）
        await SaveCurrentProfileAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ReportMiss(NormalizedRect normalizedBounds)
    {
        if (!_settings.Enabled || !_settings.AutoLearningEnabled)
        {
            return;
        }

        if (!_settings.EnableNegativeReinforcement)
        {
            _logger.LogDebug("[Issue #354] ReportMiss skipped: EnableNegativeReinforcement=false");
            return;
        }

        // 既に除外ゾーン内の場合はスキップ
        if (IsInExclusionZone(normalizedBounds.CenterX, normalizedBounds.CenterY))
        {
            return;
        }

        // 学習エンジンにmissを記録し、自動除外候補を取得
        var exclusionCandidates = _learningEngine.RecordMiss(normalizedBounds);

        // 自動除外が有効な場合、候補を除外ゾーンに登録
        if (_settings.EnableAutoExclusionZone && exclusionCandidates.Count > 0)
        {
            foreach (var candidate in exclusionCandidates)
            {
                AddExclusionZone(candidate);
                _logger.LogInformation(
                    "[Issue #354] 自動除外ゾーン登録: {Bounds}",
                    candidate);
            }
        }
    }

    /// <inheritdoc />
    public bool IsInExclusionZone(float normalizedX, float normalizedY)
    {
        lock (_lock)
        {
            // プロファイルの除外ゾーンをチェック
            if (_currentProfile?.IsInExclusionZone(normalizedX, normalizedY) == true)
            {
                return true;
            }

            // 一時的な除外ゾーンをチェック
            foreach (var zone in _exclusionZones)
            {
                if (normalizedX >= zone.X && normalizedX <= zone.Right &&
                    normalizedY >= zone.Y && normalizedY <= zone.Bottom)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc />
    public void AddExclusionZone(NormalizedRect zone)
    {
        if (!zone.IsValid())
        {
            _logger.LogWarning("Invalid exclusion zone: {Zone}", zone);
            return;
        }

        lock (_lock)
        {
            _exclusionZones.Add(zone);
            _logger.LogDebug("Added exclusion zone: {Zone}", zone);
        }
    }

    /// <inheritdoc />
    public bool RemoveExclusionZone(NormalizedRect zone)
    {
        lock (_lock)
        {
            var removed = _exclusionZones.Remove(zone);
            if (removed)
            {
                _logger.LogDebug("Removed exclusion zone: {Zone}", zone);
            }

            return removed;
        }
    }

    /// <inheritdoc />
    public void ResetLearningData(bool preserveExclusionZones = true)
    {
        lock (_lock)
        {
            _learningEngine.Reset();

            if (!preserveExclusionZones)
            {
                _exclusionZones.Clear();
            }

            if (_currentProfile != null)
            {
                _currentProfile = _currentProfile with
                {
                    Regions = [],
                    HeatmapData = null,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            _logger.LogInformation(
                "Reset learning data: PreserveExclusionZones={Preserve}",
                preserveExclusionZones);
        }
    }

    /// <summary>
    /// 現在のプロファイルを設定
    /// </summary>
    /// <remarks>
    /// [Hotfix #293] スレッドセーフティ: イベント発火をロック外に移動し、
    /// UIスレッドとのデッドロックを防止。イベント引数はロック内で準備し、
    /// 発火はロック解放後に行う。
    /// </remarks>
    private void SetCurrentProfile(RoiProfile profile, RoiProfileChangeType changeType)
    {
        RoiProfileChangedEventArgs? eventArgs = null;

        lock (_lock)
        {
            var oldProfile = _currentProfile;
            _currentProfile = profile;

            // ヒートマップをインポート
            if (profile.HeatmapData != null)
            {
                _learningEngine.ImportHeatmap(profile.HeatmapData);
            }
            else
            {
                _learningEngine.Reset();
            }

            // 除外ゾーンを設定
            _exclusionZones.Clear();
            foreach (var zone in profile.ExclusionZones)
            {
                _exclusionZones.Add(zone);
            }

            // イベント引数をロック内で準備（スレッドセーフティ確保）
            eventArgs = new RoiProfileChangedEventArgs
            {
                OldProfile = oldProfile,
                NewProfile = profile,
                ChangeType = changeType
            };

            _logger.LogInformation(
                "Profile changed: {ChangeType}, Id={ProfileId}, Name={ProfileName}",
                changeType, profile.Id, profile.Name);
        }

        // [Hotfix #293] イベントをロック外で発火（デッドロック防止）
        // イベントハンドラが他のRoiManagerメソッドを呼び出してもデッドロックしない
        ProfileChanged?.Invoke(this, eventArgs);
    }

    /// <summary>
    /// 学習データでプロファイルを更新
    /// </summary>
    private RoiProfile UpdateProfileWithLearningData(RoiProfile profile)
    {
        var heatmap = _learningEngine.ExportHeatmap();
        var regions = _learningEngine.GenerateRegions(
            _settings.MinConfidenceForRegion,
            _settings.MinConfidenceForRegion);

        return profile with
        {
            Regions = regions,
            HeatmapData = heatmap,
            ExclusionZones = [.. _exclusionZones],
            UpdatedAt = DateTime.UtcNow,
            TotalLearningSessionCount = profile.TotalLearningSessionCount + 1
        };
    }

    /// <summary>
    /// 減衰タイマーのコールバック
    /// </summary>
    private void OnDecayTimerElapsed(object? state)
    {
        try
        {
            _learningEngine.ApplyDecay();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying decay");
        }
    }

    /// <summary>
    /// 自動保存タイマーのコールバック
    /// </summary>
    private async void OnAutoSaveTimerElapsed(object? state)
    {
        try
        {
            await SaveCurrentProfileAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-saving profile");
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        _decayTimer?.Dispose();
        _autoSaveTimer?.Dispose();
    }
}

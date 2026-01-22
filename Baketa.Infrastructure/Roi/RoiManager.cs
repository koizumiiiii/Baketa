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

        var newProfile = RoiProfile.Create(
            profileId,
            name ?? "Unknown",
            executablePath,
            windowTitle);

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
    public float GetThresholdAt(float normalizedX, float normalizedY, float defaultThreshold)
    {
        if (!_settings.Enabled || !_settings.EnableDynamicThreshold)
        {
            return defaultThreshold;
        }

        var heatmapValue = _learningEngine.GetHeatmapValueAt(normalizedX, normalizedY);

        // ヒートマップ値に基づいて閾値を調整
        float multiplier;
        if (heatmapValue >= _settings.HighConfidenceThreshold)
        {
            // 高ヒートマップ領域: より厳しい閾値
            multiplier = _settings.HighHeatmapThresholdMultiplier;
        }
        else if (heatmapValue >= _settings.MinConfidenceForRegion)
        {
            // 中ヒートマップ領域: 線形補間
            var ratio = (heatmapValue - _settings.MinConfidenceForRegion) /
                       (_settings.HighConfidenceThreshold - _settings.MinConfidenceForRegion);
            multiplier = _settings.LowHeatmapThresholdMultiplier +
                        ratio * (_settings.HighHeatmapThresholdMultiplier - _settings.LowHeatmapThresholdMultiplier);
        }
        else
        {
            // 低ヒートマップ領域: より緩い閾値
            multiplier = _settings.LowHeatmapThresholdMultiplier;
        }

        return Math.Clamp(defaultThreshold * multiplier, 0.0f, 1.0f);
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
    public void ReportTextDetections(IEnumerable<(NormalizedRect bounds, float confidence)> detections)
    {
        if (!_settings.Enabled || !_settings.AutoLearningEnabled)
        {
            _logger.LogDebug(
                "[Issue #293] ReportTextDetections skipped: Enabled={Enabled}, AutoLearningEnabled={AutoLearning}",
                _settings.Enabled, _settings.AutoLearningEnabled);
            return;
        }

        // [Issue #293] プロファイルがない場合は自動作成（デフォルトプロファイル）
        EnsureDefaultProfileExists();

        // 除外ゾーン内の検出を除外
        var filteredDetections = detections
            .Where(d => !IsInExclusionZone(d.bounds.CenterX, d.bounds.CenterY))
            .ToList();

        if (filteredDetections.Count > 0)
        {
            _learningEngine.RecordDetections(filteredDetections);
            _logger.LogInformation(
                "[Issue #293] ROI学習記録完了: {Count}個の検出をヒートマップに記録",
                filteredDetections.Count);
        }
    }

    /// <summary>
    /// [Issue #293] デフォルトプロファイルが存在しない場合に作成
    /// </summary>
    private void EnsureDefaultProfileExists()
    {
        lock (_lock)
        {
            if (_currentProfile != null)
            {
                return;
            }
        }

        // デフォルトプロファイルを作成
        var defaultProfileId = "default";
        var defaultProfile = RoiProfile.Create(
            defaultProfileId,
            "Default Profile",
            string.Empty,
            string.Empty);

        SetCurrentProfile(defaultProfile, RoiProfileChangeType.Created);
        _logger.LogInformation("[Issue #293] デフォルトROIプロファイルを作成しました");

        // 非同期で保存（fire-and-forget）
        if (_profileService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _profileService.SaveProfileAsync(defaultProfile).ConfigureAwait(false);
                    _logger.LogDebug("[Issue #293] デフォルトROIプロファイルを保存しました");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Issue #293] デフォルトROIプロファイルの保存に失敗");
                }
            });
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

            // イベントをロック内で発火（一貫性保証）
            ProfileChanged?.Invoke(this, eventArgs);
        }
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

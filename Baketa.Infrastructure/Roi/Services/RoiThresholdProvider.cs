using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Roi.Services;

/// <summary>
/// [Issue #293] ROIベース動的閾値プロバイダー実装
/// </summary>
/// <remarks>
/// IRoiManagerのヒートマップデータを使用して、画像変化検知の閾値を動的に調整します。
/// </remarks>
public sealed class RoiThresholdProvider : IRoiThresholdProvider
{
    /// <summary>
    /// [Issue #293] 高優先度判定に使用する基準閾値
    /// </summary>
    private const float ReferenceThreshold = 0.5f;

    private readonly ILogger<RoiThresholdProvider> _logger;
    private readonly IRoiManager _roiManager;
    private readonly RoiManagerSettings _settings;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RoiThresholdProvider(
        ILogger<RoiThresholdProvider> logger,
        IRoiManager roiManager,
        IOptions<RoiManagerSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _roiManager = roiManager ?? throw new ArgumentNullException(nameof(roiManager));
        _settings = settings?.Value ?? RoiManagerSettings.CreateDefault();

        _logger.LogInformation(
            "[Issue #293] RoiThresholdProvider initialized: Enabled={Enabled}, EnableDynamicThreshold={EnableDynamic}",
            _roiManager.IsEnabled, _settings.EnableDynamicThreshold);
    }

    /// <inheritdoc />
    public bool IsEnabled => _roiManager.IsEnabled && _settings.EnableDynamicThreshold;

    /// <inheritdoc />
    public float GetThresholdAt(float normalizedX, float normalizedY, float defaultThreshold)
    {
        if (!IsEnabled)
        {
            return defaultThreshold;
        }

        return _roiManager.GetThresholdAt(normalizedX, normalizedY, defaultThreshold);
    }

    /// <inheritdoc />
    public float GetThresholdForCell(int row, int column, int totalRows, int totalColumns, float defaultThreshold)
    {
        if (!IsEnabled || totalRows <= 0 || totalColumns <= 0)
        {
            return defaultThreshold;
        }

        // グリッドセルの中心座標を正規化座標に変換
        var normalizedX = (column + 0.5f) / totalColumns;
        var normalizedY = (row + 0.5f) / totalRows;

        return GetThresholdAt(normalizedX, normalizedY, defaultThreshold);
    }

    /// <inheritdoc />
    public bool IsHighPriorityRegion(float normalizedX, float normalizedY)
    {
        if (!IsEnabled)
        {
            return false;
        }

        // 除外ゾーンにある場合は低優先度
        if (_roiManager.IsInExclusionZone(normalizedX, normalizedY))
        {
            return false;
        }

        // [Issue #293] 高ヒートマップ値（HighConfidenceThreshold以上）なら高優先度
        var heatmapValue = _roiManager.GetHeatmapValueAt(normalizedX, normalizedY);
        return heatmapValue >= _settings.HighConfidenceThreshold;
    }

    /// <inheritdoc />
    public float GetHeatmapValueAt(float normalizedX, float normalizedY)
    {
        if (!IsEnabled)
        {
            return 0.0f;
        }

        // [Issue #293] IRoiManager経由で直接ヒートマップ値を取得
        // 複雑な逆算ロジックを削除し、責務を明確化
        return _roiManager.GetHeatmapValueAt(normalizedX, normalizedY);
    }
}

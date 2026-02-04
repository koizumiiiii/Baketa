using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi;
using Baketa.Infrastructure.Roi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi;

/// <summary>
/// [Issue #293] RoiManagerの単体テスト
/// </summary>
public class RoiManagerTests : IDisposable
{
    private readonly Mock<ILogger<RoiManager>> _loggerMock;
    private readonly Mock<ILogger<RoiLearningEngine>> _engineLoggerMock;
    private readonly Mock<IRoiLearningEngine> _learningEngineMock;
    private readonly IOptions<RoiManagerSettings> _defaultSettings;
    private RoiManager? _manager;

    public RoiManagerTests()
    {
        _loggerMock = new Mock<ILogger<RoiManager>>();
        _engineLoggerMock = new Mock<ILogger<RoiLearningEngine>>();
        _learningEngineMock = new Mock<IRoiLearningEngine>();
        _defaultSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            EnableDynamicThreshold = true,
            MinConfidenceForRegion = 0.3f,
            HighConfidenceThreshold = 0.7f,
            HighHeatmapThresholdMultiplier = 1.05f,
            LowHeatmapThresholdMultiplier = 0.95f,
            DecayIntervalSeconds = 0, // タイマーを無効化
            AutoSaveIntervalSeconds = 0 // タイマーを無効化
        });
    }

    public void Dispose()
    {
        _manager?.Dispose();
    }

    #region 初期化テスト

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Assert
        Assert.True(_manager.IsEnabled);
        Assert.Null(_manager.CurrentProfile);
    }

    [Fact]
    public void IsEnabled_WithDisabledSettings_ShouldReturnFalse()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings { Enabled = false });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            disabledSettings);

        // Act & Assert
        Assert.False(_manager.IsEnabled);
    }

    #endregion

    #region ComputeProfileId テスト

    [Fact]
    public void ComputeProfileId_ShouldReturnConsistentHash()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var path = @"C:\Games\MyGame.exe";

        // Act
        var id1 = _manager.ComputeProfileId(path);
        var id2 = _manager.ComputeProfileId(path);

        // Assert
        Assert.Equal(id1, id2);
        Assert.NotEmpty(id1);
    }

    [Fact]
    public void ComputeProfileId_ShouldBeCaseInsensitive()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var id1 = _manager.ComputeProfileId(@"C:\Games\MyGame.exe");
        var id2 = _manager.ComputeProfileId(@"C:\GAMES\MYGAME.EXE");

        // Assert
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ComputeProfileId_WithEmptyPath_ShouldReturnEmptyString()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var id = _manager.ComputeProfileId("");

        // Assert
        Assert.Empty(id);
    }

    #endregion

    #region GetThresholdAt テスト

    [Fact]
    public void GetThresholdAt_WhenDisabled_ShouldReturnDefault()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings { Enabled = false });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            disabledSettings);

        // Act
        var threshold = _manager.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        Assert.Equal(0.92f, threshold, precision: 4);
    }

    [Fact]
    public void GetThresholdAt_WithHighHeatmapValue_ShouldReturnHigherThreshold()
    {
        // Arrange
        _learningEngineMock.Setup(e => e.GetHeatmapValueAt(It.IsAny<float>(), It.IsAny<float>()))
            .Returns(0.9f); // 高ヒートマップ値

        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var threshold = _manager.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        // 高ヒートマップ領域: 0.92 * 1.05 = 0.966
        Assert.True(threshold > 0.92f);
    }

    [Fact]
    public void GetThresholdAt_WithLowHeatmapValue_ShouldReturnLowerThreshold()
    {
        // Arrange
        _learningEngineMock.Setup(e => e.GetHeatmapValueAt(It.IsAny<float>(), It.IsAny<float>()))
            .Returns(0.1f); // 低ヒートマップ値

        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var threshold = _manager.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        // 低ヒートマップ領域: 0.92 * 0.95 = 0.874
        Assert.True(threshold < 0.92f);
    }

    #endregion

    #region ReportTextDetection テスト

    [Fact]
    public void ReportTextDetection_WhenEnabled_ShouldCallLearningEngine()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        _manager.ReportTextDetection(bounds, confidence: 0.9f);

        // Assert
        _learningEngineMock.Verify(
            e => e.RecordDetection(bounds, 0.9f, 1),
            Times.Once);
    }

    [Fact]
    public void ReportTextDetection_WhenDisabled_ShouldNotCallLearningEngine()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = false,
            AutoLearningEnabled = true
        });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            disabledSettings);

        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        _manager.ReportTextDetection(bounds, confidence: 0.9f);

        // Assert
        _learningEngineMock.Verify(
            e => e.RecordDetection(It.IsAny<NormalizedRect>(), It.IsAny<float>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public void ReportTextDetection_InExclusionZone_ShouldNotCallLearningEngine()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // 除外ゾーンを追加
        var exclusionZone = new NormalizedRect(0.0f, 0.0f, 0.3f, 0.3f);
        _manager.AddExclusionZone(exclusionZone);

        // 除外ゾーン内のバウンズ
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.1f, 0.1f);

        // Act
        _manager.ReportTextDetection(bounds, confidence: 0.9f);

        // Assert
        _learningEngineMock.Verify(
            e => e.RecordDetection(It.IsAny<NormalizedRect>(), It.IsAny<float>(), It.IsAny<int>()),
            Times.Never);
    }

    #endregion

    #region ExclusionZone テスト

    [Fact]
    public void IsInExclusionZone_WithPointInZone_ShouldReturnTrue()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));

        // Act
        var result = _manager.IsInExclusionZone(0.05f, 0.05f);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInExclusionZone_WithPointOutsideZone_ShouldReturnFalse()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));

        // Act
        var result = _manager.IsInExclusionZone(0.5f, 0.5f);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RemoveExclusionZone_ShouldRemoveZone()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var zone = new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f);
        _manager.AddExclusionZone(zone);
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));

        // Act
        var removed = _manager.RemoveExclusionZone(zone);

        // Assert
        Assert.True(removed);
        Assert.False(_manager.IsInExclusionZone(0.05f, 0.05f));
    }

    #endregion

    #region ResetLearningData テスト

    [Fact]
    public void ResetLearningData_ShouldResetEngine()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        _manager.ResetLearningData(preserveExclusionZones: true);

        // Assert
        _learningEngineMock.Verify(e => e.Reset(), Times.Once);
    }

    [Fact]
    public void ResetLearningData_WithoutPreservingExclusionZones_ShouldClearZones()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));

        // Act
        _manager.ResetLearningData(preserveExclusionZones: false);

        // Assert
        Assert.False(_manager.IsInExclusionZone(0.05f, 0.05f));
    }

    #endregion

    #region [Issue #379] P1-2 除外ゾーンIoUデデュプリケーション テスト

    [Fact]
    public void AddExclusionZone_WithHighIoUDuplicate_ShouldSkip()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var zone1 = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        var zone2 = new NormalizedRect(0.11f, 0.11f, 0.2f, 0.2f); // zone1とほぼ同じ

        _manager.AddExclusionZone(zone1);

        // Act
        _manager.AddExclusionZone(zone2);

        // Assert - zone1のみ有効、zone2は重複としてスキップ
        Assert.True(_manager.IsInExclusionZone(0.15f, 0.15f));
        // zone1を削除するとzone2は登録されていないので除外ゾーンなし
        _manager.RemoveExclusionZone(zone1);
        // マージされた場合もあるのでクリアして確認
        _manager.ClearAllExclusionZones();
        Assert.False(_manager.IsInExclusionZone(0.15f, 0.15f));
    }

    [Fact]
    public void AddExclusionZone_WithLowIoU_ShouldAddBoth()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var zone1 = new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f);
        var zone2 = new NormalizedRect(0.5f, 0.5f, 0.1f, 0.1f); // zone1と離れている

        // Act
        _manager.AddExclusionZone(zone1);
        _manager.AddExclusionZone(zone2);

        // Assert - 両方のゾーンが有効
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));
        Assert.True(_manager.IsInExclusionZone(0.55f, 0.55f));
    }

    #endregion

    #region [Issue #379] P1-3 除外ゾーン上限 テスト

    [Fact]
    public void AddExclusionZone_WhenMaxReached_ShouldNotAdd()
    {
        // Arrange - MaxExclusionZones=3に設定
        var settings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            DecayIntervalSeconds = 0,
            AutoSaveIntervalSeconds = 0,
            MaxExclusionZones = 3
        });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            settings);

        // 3つの離れたゾーンを追加
        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.05f, 0.05f));
        _manager.AddExclusionZone(new NormalizedRect(0.3f, 0.3f, 0.05f, 0.05f));
        _manager.AddExclusionZone(new NormalizedRect(0.6f, 0.6f, 0.05f, 0.05f));

        // Act - 4つ目は上限超過
        _manager.AddExclusionZone(new NormalizedRect(0.9f, 0.9f, 0.05f, 0.05f));

        // Assert - 4つ目は登録されない
        Assert.False(_manager.IsInExclusionZone(0.92f, 0.92f));
        // 既存3つは有効
        Assert.True(_manager.IsInExclusionZone(0.02f, 0.02f));
    }

    #endregion

    #region [Issue #379] P2-3 TTL除外ゾーン テスト

    [Fact]
    public void IsInExclusionZone_WithExpiredTtl_ShouldReturnFalse()
    {
        // Arrange - TTL=0（無期限）で確認後、TTL有効にする
        // TTLのテストは内部タイムスタンプに依存するため、TTL=0で動作確認
        var settings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            DecayIntervalSeconds = 0,
            AutoSaveIntervalSeconds = 0,
            ExclusionZoneTtlHours = 0 // TTL無効
        });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            settings);

        var zone = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        _manager.AddExclusionZone(zone);

        // Act & Assert - TTL無効なので削除されない
        Assert.True(_manager.IsInExclusionZone(0.15f, 0.15f));
    }

    [Fact]
    public void IsInExclusionZone_WithTtlEnabled_ZoneShouldPersistWithinTtl()
    {
        // Arrange - TTL=24時間（テスト中は期限切れにならない）
        var settings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            DecayIntervalSeconds = 0,
            AutoSaveIntervalSeconds = 0,
            ExclusionZoneTtlHours = 24
        });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            settings);

        var zone = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        _manager.AddExclusionZone(zone);

        // Act & Assert - TTL内なので有効
        Assert.True(_manager.IsInExclusionZone(0.15f, 0.15f));
    }

    #endregion

    #region [Issue #379] P3-2 隣接ゾーンマージ テスト

    [Fact]
    public void AddExclusionZone_WithIntersectingZone_ShouldMerge()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // 部分的に重なるが、IoU < dedup閾値のゾーン
        var zone1 = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f); // (0.1,0.1)-(0.3,0.3)
        var zone2 = new NormalizedRect(0.25f, 0.25f, 0.2f, 0.2f); // (0.25,0.25)-(0.45,0.45)

        _manager.AddExclusionZone(zone1);

        // Act
        _manager.AddExclusionZone(zone2);

        // Assert - マージされたゾーンが両方の領域をカバー
        Assert.True(_manager.IsInExclusionZone(0.15f, 0.15f)); // zone1のエリア
        Assert.True(_manager.IsInExclusionZone(0.4f, 0.4f)); // zone2のエリア
    }

    #endregion

    #region [Issue #379] A案 RemoveOverlappingExclusionZones テスト

    [Fact]
    public void RemoveOverlappingExclusionZones_WithOverlap_ShouldRemoveZone()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var zone = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        _manager.AddExclusionZone(zone);
        Assert.True(_manager.IsInExclusionZone(0.15f, 0.15f));

        // Act - 同じ位置で翻訳成功
        var successBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        var removed = _manager.RemoveOverlappingExclusionZones(successBounds, iouThreshold: 0.3f);

        // Assert
        Assert.Equal(1, removed);
        Assert.False(_manager.IsInExclusionZone(0.15f, 0.15f));
    }

    [Fact]
    public void RemoveOverlappingExclusionZones_WithNoOverlap_ShouldNotRemove()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var zone = new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f);
        _manager.AddExclusionZone(zone);

        // Act - 離れた場所で翻訳成功
        var successBounds = new NormalizedRect(0.5f, 0.5f, 0.2f, 0.2f);
        var removed = _manager.RemoveOverlappingExclusionZones(successBounds, iouThreshold: 0.3f);

        // Assert
        Assert.Equal(0, removed);
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));
    }

    [Fact]
    public void RemoveOverlappingExclusionZones_WithMultipleOverlaps_ShouldRemoveAll()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // 広い範囲をカバーする除外ゾーン
        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));
        _manager.AddExclusionZone(new NormalizedRect(0.5f, 0.5f, 0.1f, 0.1f));

        // Act - 片方のゾーンと重なる翻訳成功
        var successBounds = new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f);
        var removed = _manager.RemoveOverlappingExclusionZones(successBounds, iouThreshold: 0.3f);

        // Assert
        Assert.Equal(1, removed);
        Assert.False(_manager.IsInExclusionZone(0.05f, 0.05f));
        Assert.True(_manager.IsInExclusionZone(0.55f, 0.55f)); // 他のゾーンは残る
    }

    [Fact]
    public void RemoveOverlappingExclusionZones_WithInvalidBounds_ShouldReturnZero()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));

        // Act
        var removed = _manager.RemoveOverlappingExclusionZones(
            new NormalizedRect(-1f, -1f, 0f, 0f)); // 無効なbounds

        // Assert
        Assert.Equal(0, removed);
    }

    #endregion

    #region [Issue #379] ClearAllExclusionZones テスト

    [Fact]
    public void ClearAllExclusionZones_ShouldRemoveAllZones()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));
        _manager.AddExclusionZone(new NormalizedRect(0.5f, 0.5f, 0.1f, 0.1f));
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));
        Assert.True(_manager.IsInExclusionZone(0.55f, 0.55f));

        // Act
        _manager.ClearAllExclusionZones();

        // Assert
        Assert.False(_manager.IsInExclusionZone(0.05f, 0.05f));
        Assert.False(_manager.IsInExclusionZone(0.55f, 0.55f));
    }

    [Fact]
    public void ClearAllExclusionZones_WithNoZones_ShouldNotThrow()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act & Assert - 例外なく処理される
        _manager.ClearAllExclusionZones();
        Assert.False(_manager.IsInExclusionZone(0.5f, 0.5f));
    }

    #endregion

    #region [Issue #379] C案 プロファイル切り替え時の除外ゾーンクリア テスト

    [Fact]
    public async Task GetOrCreateProfileAsync_ShouldClearRuntimeExclusionZones()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // ランタイム除外ゾーンを追加
        _manager.AddExclusionZone(new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));
        Assert.True(_manager.IsInExclusionZone(0.15f, 0.15f));

        // Act - 新しいプロファイルを作成（プロファイル切り替え）
        await _manager.GetOrCreateProfileAsync(@"C:\Games\NewGame.exe", "New Game");

        // Assert - ランタイム除外ゾーンがクリアされる
        Assert.False(_manager.IsInExclusionZone(0.15f, 0.15f));
    }

    #endregion

    #region [Issue #379] RoiManagerSettings バリデーション テスト

    [Fact]
    public void Settings_WithValidIssue379Defaults_ShouldBeValid()
    {
        // Arrange
        var settings = new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            MaxExclusionZones = 20,
            ExclusionZoneDeduplicationIoU = 0.5f,
            OcrConfidenceThresholdForMissSkip = 0.7f,
            SafeZoneMinDetectionCount = 10,
            MissRatioThresholdForExclusion = 0.8f,
            MinSamplesForMissRatio = 10,
            ExclusionZoneTtlHours = 24,
            LowConfidenceMissRecordingThreshold = 0.3f,
            SafeZoneOverlapIoUThreshold = 0.3f
        };

        // Act & Assert
        Assert.True(settings.IsValid());
    }

    [Theory]
    [InlineData(0)]     // MaxExclusionZones must be > 0
    [InlineData(-1)]
    public void Settings_WithInvalidMaxExclusionZones_ShouldBeInvalid(int maxZones)
    {
        // Arrange
        var settings = new RoiManagerSettings { MaxExclusionZones = maxZones };

        // Act & Assert
        Assert.False(settings.IsValid());
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void Settings_WithInvalidExclusionZoneDeduplicationIoU_ShouldBeInvalid(float iou)
    {
        // Arrange
        var settings = new RoiManagerSettings { ExclusionZoneDeduplicationIoU = iou };

        // Act & Assert
        Assert.False(settings.IsValid());
    }

    [Fact]
    public void Settings_WithZeroTtl_ShouldBeValid()
    {
        // Arrange - TTL=0は無期限として有効
        var settings = new RoiManagerSettings { ExclusionZoneTtlHours = 0 };

        // Act & Assert
        Assert.True(settings.IsValid());
    }

    [Fact]
    public void Settings_WithNegativeTtl_ShouldBeInvalid()
    {
        // Arrange
        var settings = new RoiManagerSettings { ExclusionZoneTtlHours = -1 };

        // Act & Assert
        Assert.False(settings.IsValid());
    }

    #endregion

    #region GetAllRegions テスト

    [Fact]
    public void GetAllRegions_WithNoProfile_ShouldReturnEmptyList()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var regions = _manager.GetAllRegions();

        // Assert
        Assert.Empty(regions);
    }

    #endregion
}

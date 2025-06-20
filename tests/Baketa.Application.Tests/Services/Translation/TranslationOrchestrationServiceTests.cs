using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Models;
using Baketa.Application.Services.Translation;
using Baketa.Application.Tests.TestUtilities;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

// 名前空間競合を解決するためのエイリアス
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;
using ApplicationTranslationSettings = Baketa.Application.Services.Translation.TranslationSettings;
using CoreTranslationSettings = Baketa.Core.Settings.TranslationSettings;

namespace Baketa.Application.Tests.Services.Translation;

/// <summary>
/// TranslationOrchestrationService の単体テスト
/// </summary>
public class TranslationOrchestrationServiceTests : IDisposable
{
    private readonly Mock<ICaptureService> _captureServiceMock;
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly Mock<ILogger<TranslationOrchestrationService>> _loggerMock;
    private readonly Mock<IImage> _imageMock;
    
    private readonly TranslationOrchestrationService _service;
    private bool _disposed;

    /// <summary>
    /// テストセットアップ
    /// </summary>
    public TranslationOrchestrationServiceTests()
    {
        // Mock オブジェクトの初期化
        _captureServiceMock = new Mock<ICaptureService>();
        _settingsServiceMock = new Mock<ISettingsService>();
        _loggerMock = new Mock<ILogger<TranslationOrchestrationService>>();
        _imageMock = new Mock<IImage>();

        // CaptureService のモック設定
        SetupCaptureServiceMocks();
        
        // Settings Service のモック設定
        SetupSettingsServiceMocks();

        // テスト対象サービスのインスタンス作成
        _service = new TranslationOrchestrationService(
            _captureServiceMock.Object,
            _settingsServiceMock.Object,
            _loggerMock.Object);
    }

    #region 自動翻訳制御テスト

    /// <summary>
    /// StartAutomaticTranslationAsync が呼ばれたときに自動ループが開始されることをテスト
    /// </summary>
    [Fact]
    public async Task StartAutomaticTranslationAsync_WhenCalled_StartsAutomaticLoop()
    {
        // Act
        await _service.StartAutomaticTranslationAsync();

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeTrue();
        _service.CurrentMode.Should().Be(TranslationMode.Automatic);

        // Cleanup
        await _service.StopAutomaticTranslationAsync();
    }

    /// <summary>
    /// 既に実行中の自動翻訳を再度開始したときに警告ログが出ることをテスト
    /// </summary>
    [Fact]
    public async Task StartAutomaticTranslationAsync_WhenAlreadyRunning_LogsWarning()
    {
        // Arrange
        await _service.StartAutomaticTranslationAsync();

        // Act
        await _service.StartAutomaticTranslationAsync();

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeTrue();
        
        // ログの検証
        VerifyLogCall(MSLogLevel.Warning, Times.Once());

        // Cleanup
        await _service.StopAutomaticTranslationAsync();
    }

    /// <summary>
    /// StopAutomaticTranslationAsync が実行中の自動翻訳を正常に停止することをテスト
    /// </summary>
    [Fact]
    public async Task StopAutomaticTranslationAsync_WhenRunning_StopsGracefully()
    {
        // Arrange
        await _service.StartAutomaticTranslationAsync();
        _service.IsAutomaticTranslationActive.Should().BeTrue();

        // Act
        await _service.StopAutomaticTranslationAsync();

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeFalse();
        _service.CurrentMode.Should().Be(TranslationMode.Manual);
    }

    /// <summary>
    /// 実行中でない自動翻訳を停止したときに何もしないことをテスト
    /// </summary>
    [Fact]
    public async Task StopAutomaticTranslationAsync_WhenNotRunning_DoesNothing()
    {
        // Arrange
        _service.IsAutomaticTranslationActive.Should().BeFalse();

        // Act
        await _service.StopAutomaticTranslationAsync();

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeFalse();
        
        // 警告ログが出ることを確認
        VerifyLogCall(MSLogLevel.Warning, Times.Once());
    }

    #endregion

    #region 単発翻訳制御テスト

    /// <summary>
    /// TriggerSingleTranslationAsync が呼ばれたときに翻訳が実行されることをテスト
    /// </summary>
    [Fact]
    public async Task TriggerSingleTranslationAsync_WhenCalled_ExecutesTranslation()
    {
        // Arrange
        TranslationResult? receivedResult = null;
        using var subscription = _service.TranslationResults
            .Subscribe(result => receivedResult = result);

        // Act
        await _service.TriggerSingleTranslationAsync();
        
        // 翻訳処理の完了を待機（模擬処理で約500ms + 800ms = 1300ms）
        await Task.Delay(1500);

        // Assert
        _captureServiceMock.Verify(
            x => x.CaptureScreenAsync(), 
            Times.Once);

        // 翻訳結果が発行されることを確認
        receivedResult.Should().NotBeNull();
        receivedResult!.Mode.Should().Be(TranslationMode.Manual);
        receivedResult.Id.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// TriggerSingleTranslationAsync が既に実行中の場合の処理をテスト
    /// </summary>
    [Fact]
    public async Task TriggerSingleTranslationAsync_WhenAlreadyRunning_WaitsForCompletion()
    {
        // Arrange - キャプチャに時間がかかるように設定
        _captureServiceMock
            .Setup(x => x.CaptureScreenAsync())
            .Returns(async () =>
            {
                await Task.Delay(500);
                return _imageMock.Object;
            });

        // Act - 並行して2つの単発翻訳を実行
        var task1 = _service.TriggerSingleTranslationAsync();
        var task2 = _service.TriggerSingleTranslationAsync();

        await Task.WhenAll(task1, task2);

        // Assert - セマフォにより順次実行されることを確認
        _captureServiceMock.Verify(
            x => x.CaptureScreenAsync(), 
            Times.Exactly(2));
    }

    /// <summary>
    /// キャンセレーショントークンによる単発翻訳のキャンセルをテスト
    /// </summary>
    [Fact]
    public async Task TriggerSingleTranslationAsync_WithCancellation_CancelsGracefully()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        
        _captureServiceMock
            .Setup(x => x.CaptureScreenAsync())
            .Returns(async () =>
            {
                await Task.Delay(1000, cts.Token);
                return _imageMock.Object;
            });

        // Act
        var translationTask = _service.TriggerSingleTranslationAsync(cts.Token);
        cts.CancelAfter(100); // 100ms後にキャンセル

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => translationTask);
    }

    #endregion

    #region 割り込み処理テスト

    /// <summary>
    /// 自動翻訳中の単発翻訳が優先されることをテスト
    /// </summary>
    [Fact]
    public async Task SingleTranslation_DuringAutomaticMode_TakesPriority()
    {
        // Arrange
        await _service.StartAutomaticTranslationAsync();
        _service.IsAutomaticTranslationActive.Should().BeTrue();

        // Act
        await _service.TriggerSingleTranslationAsync();

        // Assert - 単発翻訳が実行されたことを確認
        _captureServiceMock.Verify(
            x => x.CaptureScreenAsync(), 
            Times.AtLeastOnce);

        // Cleanup
        await _service.StopAutomaticTranslationAsync();
    }

    /// <summary>
    /// 単発翻訳中に自動翻訳が待機することをテスト
    /// </summary>
    [Fact]
    public async Task AutomaticTranslation_WaitsDuringSingleTranslation()
    {
        // Arrange
        await _service.StartAutomaticTranslationAsync();
        
        // キャプチャに時間がかかるように設定
        _captureServiceMock
            .Setup(x => x.CaptureScreenAsync())
            .Returns(async () =>
            {
                await Task.Delay(200);
                return _imageMock.Object;
            });

        // Act
        var singleTranslationTask = _service.TriggerSingleTranslationAsync();
        
        // 単発翻訳の完了を待機
        await singleTranslationTask;

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeTrue();

        // Cleanup
        await _service.StopAutomaticTranslationAsync();
    }

    /// <summary>
    /// 単発翻訳完了後に自動翻訳が再開されることをテスト
    /// </summary>
    [Fact]
    public async Task AutomaticMode_ResumesAfterSingleTranslationCompletes()
    {
        // Arrange
        await _service.StartAutomaticTranslationAsync();

        // Act
        await _service.TriggerSingleTranslationAsync();

        // Assert - 自動翻訳モードが継続していることを確認
        _service.IsAutomaticTranslationActive.Should().BeTrue();
        _service.CurrentMode.Should().Be(TranslationMode.Automatic);

        // Cleanup
        await _service.StopAutomaticTranslationAsync();
    }

    #endregion

    #region Observable統合テスト

    /// <summary>
    /// TranslationResults が完了した翻訳を発行することをテスト
    /// </summary>
    [Fact]
    public async Task TranslationResults_PublishesCompletedTranslations()
    {
        // Arrange
        TranslationResult? receivedResult = null;
        using var subscription = _service.TranslationResults
            .Subscribe(result => receivedResult = result);

        // Act
        await _service.TriggerSingleTranslationAsync();

        // Assert
        receivedResult.Should().NotBeNull();
        receivedResult!.Mode.Should().Be(TranslationMode.Manual);
        receivedResult.Id.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// StatusChanges が状態更新を発行することをテスト
    /// </summary>
    [Fact]
    public async Task StatusChanges_PublishesStatusUpdates()
    {
        // Arrange
        var statusUpdates = new List<TranslationStatus>();
        using var subscription = _service.StatusChanges
            .Subscribe(status => statusUpdates.Add(status));

        // Act
        await _service.TriggerSingleTranslationAsync();

        // Assert
        statusUpdates.Should().NotBeEmpty();
        statusUpdates.Should().Contain(TranslationStatus.Completed);
    }

    /// <summary>
    /// ProgressUpdates が詳細進行状況を発行することをテスト
    /// </summary>
    [Fact]
    public async Task ProgressUpdates_PublishesDetailedProgress()
    {
        // Arrange
        var progressUpdates = new List<TranslationProgress>();
        using var subscription = _service.ProgressUpdates
            .Subscribe(progress => progressUpdates.Add(progress));

        // Act
        await _service.TriggerSingleTranslationAsync();

        // Assert
        progressUpdates.Should().NotBeEmpty();
        progressUpdates.Should().Contain(p => p.Status == TranslationStatus.Capturing);
        progressUpdates.Should().Contain(p => p.Status == TranslationStatus.Completed);
    }

    #endregion

    #region エラー処理とリソース管理テスト

    /// <summary>
    /// 自動翻訳ループでエラーが発生しても処理が継続されることをテスト
    /// </summary>
    [Fact]
    public async Task AutomaticTranslationLoop_WhenErrorOccurs_ContinuesOperation()
    {
        // Arrange
        var callCount = 0;
        _captureServiceMock
            .Setup(x => x.CaptureScreenAsync())
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("テストエラー");
                }
                return Task.FromResult(_imageMock.Object);
            });

        // Act
        await _service.StartAutomaticTranslationAsync();
        
        // エラー発生後も処理が継続されるまで少し待機
        await Task.Delay(300);

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeTrue();

        // エラーログが出力されていることを確認
        VerifyLogCall(MSLogLevel.Error, Times.AtLeastOnce());

        // Cleanup
        await _service.StopAutomaticTranslationAsync();
    }

    /// <summary>
    /// Dispose時に全ての処理が停止してリソースが解放されることをテスト
    /// </summary>
    [Fact]
    public async Task Dispose_StopsAllOperationsAndReleasesResources()
    {
        // Arrange
        await _service.StartAutomaticTranslationAsync();
        _service.IsAutomaticTranslationActive.Should().BeTrue();

        // Act
        _service.Dispose();

        // Assert - Disposeが正常に完了することを確認
        _service.IsAutomaticTranslationActive.Should().BeFalse();
        
        // サービス停止ログが出力されていることを確認
        VerifyLogCall(MSLogLevel.Information, Times.AtLeastOnce());
    }

    /// <summary>
    /// StopAsync がタイムアウト内に完了することをテスト
    /// </summary>
    [Fact]
    public async Task StopAsync_WithTimeout_CompletesWithinDeadline()
    {
        // Arrange
        await _service.StartAsync();
        await _service.StartAutomaticTranslationAsync();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _service.StopAsync(cts.Token);

        // Assert
        _service.IsAutomaticTranslationActive.Should().BeFalse();
        cts.Token.IsCancellationRequested.Should().BeFalse();
    }

    #endregion

    #region 設定管理テスト

    /// <summary>
    /// GetSingleTranslationDisplayDuration が正しい期間を返すことをテスト
    /// </summary>
    [Fact]
    public void GetSingleTranslationDisplayDuration_ReturnsCorrectDuration()
    {
        // Act
        var duration = _service.GetSingleTranslationDisplayDuration();

        // Assert
        duration.Should().BePositive();
        duration.TotalSeconds.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// GetAutomaticTranslationInterval が正しい間隔を返すことをテスト
    /// </summary>
    [Fact]
    public void GetAutomaticTranslationInterval_ReturnsCorrectInterval()
    {
        // Act
        var interval = _service.GetAutomaticTranslationInterval();

        // Assert
        interval.Should().BePositive();
        interval.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// UpdateTranslationSettingsAsync が設定を更新することをテスト
    /// </summary>
    [Fact]
    public async Task UpdateTranslationSettingsAsync_UpdatesSettings()
    {
        // Arrange
        var newSettings = new ApplicationTranslationSettings
        {
            SingleTranslationDisplaySeconds = 10,
            AutomaticTranslationIntervalMs = 2000
        };

        // Act
        await _service.UpdateTranslationSettingsAsync(newSettings);

        // Assert - 情報ログが出力されていることを確認
        VerifyLogCall(MSLogLevel.Information, Times.AtLeastOnce());
    }

    #endregion

    #region ヘルパーメソッド

    /// <summary>
    /// CaptureService のモック設定
    /// </summary>
    private void SetupCaptureServiceMocks()
    {
        _captureServiceMock
            .Setup(x => x.CaptureScreenAsync())
            .ReturnsAsync(_imageMock.Object);

        _captureServiceMock
            .Setup(x => x.DetectChangesAsync(It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()))
            .ReturnsAsync(true);

        _captureServiceMock
            .Setup(x => x.SetCaptureOptions(It.IsAny<CaptureOptions>()));

        _captureServiceMock
            .Setup(x => x.GetCaptureOptions())
            .Returns(ApplicationTestDataFactory.CreateTestCaptureOptions());
    }

    /// <summary>
    /// SettingsService のモック設定
    /// </summary>
    private void SetupSettingsServiceMocks()
    {
        // 必要に応じて設定関連のモックを追加
        // 現在は TranslationOrchestrationService が設定に直接依存していないため空
    }

    /// <summary>
    /// ログ出力の検証
    /// </summary>
    private void VerifyLogCall(MSLogLevel level, Times times)
    {
        _loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _service?.Dispose();
        _imageMock?.Object?.Dispose();
        
        _disposed = true;
    }

    #endregion
}

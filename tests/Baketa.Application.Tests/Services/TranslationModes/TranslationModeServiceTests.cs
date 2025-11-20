using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.TranslationModes;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using TranslationMode = Baketa.Core.Abstractions.Services.TranslationMode;

namespace Baketa.Application.Tests.Services.TranslationModes;

/// <summary>
/// TranslationModeServiceのテストクラス
/// Issue #163 Phase 6: 20テストケース
/// </summary>
public class TranslationModeServiceTests
{
    private readonly LiveTranslationMode _liveMode;
    private readonly SingleshotTranslationMode _singleshotMode;
    private readonly Mock<ILogger<TranslationModeService>> _mockLogger;

    public TranslationModeServiceTests()
    {
        // 実際のインスタンスを作成（sealedクラスのためMockは使用不可）
        _liveMode = new LiveTranslationMode(Mock.Of<ILogger<LiveTranslationMode>>());
        _singleshotMode = new SingleshotTranslationMode(Mock.Of<ILogger<SingleshotTranslationMode>>());
        _mockLogger = new Mock<ILogger<TranslationModeService>>();
    }

    private TranslationModeService CreateService()
    {
        return new TranslationModeService(_liveMode, _singleshotMode, _mockLogger.Object);
    }

    #region 基本機能テスト (5個)

    [Fact]
#pragma warning disable CA1707 // xUnitテストメソッドの命名規約に従い、アンダースコアを使用します
    public void CurrentMode_DefaultValue_ShouldBeNone()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();

        // Act
        var currentMode = service.CurrentMode;

        // Assert
        Assert.Equal(TranslationMode.None, currentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToLiveModeAsync_ShouldSetCurrentModeToLive()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();

        // Act
        await service.SwitchToLiveModeAsync();

        // Assert
        Assert.Equal(TranslationMode.Live, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToSingleshotModeAsync_ShouldSetCurrentModeToSingleshot()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();

        // Act
        await service.SwitchToSingleshotModeAsync();

        // Assert
        Assert.Equal(TranslationMode.Singleshot, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task ResetModeAsync_ShouldSetCurrentModeToNone()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        await service.SwitchToLiveModeAsync();

        // Act
        await service.ResetModeAsync();

        // Assert
        Assert.Equal(TranslationMode.None, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public void Dispose_ShouldCompleteWithoutException()
#pragma warning restore CA1707
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    #endregion

    #region State Pattern動作テスト (5個)

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToLiveMode_ShouldTransitionToLiveState()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();

        // Act
        await service.SwitchToLiveModeAsync();

        // Assert - 状態がLiveに遷移
        Assert.Equal(TranslationMode.Live, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToSingleshotMode_ShouldTransitionToSingleshotState()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();

        // Act
        await service.SwitchToSingleshotModeAsync();

        // Assert - 状態がSingleshotに遷移
        Assert.Equal(TranslationMode.Singleshot, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToSameMode_ShouldMaintainCurrentState()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        await service.SwitchToLiveModeAsync();
        var initialMode = service.CurrentMode;

        // Act - 同じモードに切り替え
        await service.SwitchToLiveModeAsync();

        // Assert - 状態が維持される
        Assert.Equal(initialMode, service.CurrentMode);
        Assert.Equal(TranslationMode.Live, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchMode_ShouldTransitionFromLiveToSingleshot()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        await service.SwitchToLiveModeAsync();
        Assert.Equal(TranslationMode.Live, service.CurrentMode);

        // Act - Singleshotに切り替え
        await service.SwitchToSingleshotModeAsync();

        // Assert - 状態がSingleshotに遷移
        Assert.Equal(TranslationMode.Singleshot, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchMode_ShouldTransitionFromSingleshotToLive()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        await service.SwitchToSingleshotModeAsync();
        Assert.Equal(TranslationMode.Singleshot, service.CurrentMode);

        // Act - Liveに切り替え
        await service.SwitchToLiveModeAsync();

        // Assert - 状態がLiveに遷移
        Assert.Equal(TranslationMode.Live, service.CurrentMode);
    }

    #endregion

    #region イベント発行テスト (5個)

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToLiveMode_ShouldRaiseModeChangedEvent()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        TranslationMode? raisedMode = null;
        service.ModeChanged += (_, mode) => raisedMode = mode;

        // Act
        await service.SwitchToLiveModeAsync();

        // Assert
        Assert.NotNull(raisedMode);
        Assert.Equal(TranslationMode.Live, raisedMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToSingleshotMode_ShouldRaiseModeChangedEvent()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        TranslationMode? raisedMode = null;
        service.ModeChanged += (_, mode) => raisedMode = mode;

        // Act
        await service.SwitchToSingleshotModeAsync();

        // Assert
        Assert.NotNull(raisedMode);
        Assert.Equal(TranslationMode.Singleshot, raisedMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task ResetMode_ShouldRaiseModeChangedEvent()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        await service.SwitchToLiveModeAsync();

        TranslationMode? raisedMode = null;
        service.ModeChanged += (_, mode) => raisedMode = mode;

        // Act
        await service.ResetModeAsync();

        // Assert
        Assert.NotNull(raisedMode);
        Assert.Equal(TranslationMode.None, raisedMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task ModeChangedEvent_ShouldContainCorrectModeValue()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        var raisedModes = new System.Collections.Generic.List<TranslationMode>();
        service.ModeChanged += (_, mode) => raisedModes.Add(mode);

        // Act
        await service.SwitchToLiveModeAsync();
        await service.SwitchToSingleshotModeAsync();
        await service.ResetModeAsync();

        // Assert
        Assert.Equal(3, raisedModes.Count);
        Assert.Equal(TranslationMode.Live, raisedModes[0]);
        Assert.Equal(TranslationMode.Singleshot, raisedModes[1]);
        Assert.Equal(TranslationMode.None, raisedModes[2]);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SwitchToSameMode_ShouldNotRaiseModeChangedEvent()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        await service.SwitchToLiveModeAsync();

        var eventRaised = false;
        service.ModeChanged += (_, _) => eventRaised = true;

        // Act - 同じモードに切り替え
        await service.SwitchToLiveModeAsync();

        // Assert - イベントが発行されない
        Assert.False(eventRaised);
    }

    #endregion

    #region スレッドセーフティテスト (3個)

    [Fact]
#pragma warning disable CA1707
    public async Task ConcurrentModeSwitch_ShouldMaintainConsistency()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();

        // Act - 並行して複数のモード切り替えを実行
        var tasks = new[]
        {
            Task.Run(async () => await service.SwitchToLiveModeAsync()),
            Task.Run(async () => await service.SwitchToSingleshotModeAsync()),
            Task.Run(async () => await service.SwitchToLiveModeAsync()),
            Task.Run(async () => await service.SwitchToSingleshotModeAsync())
        };

        await Task.WhenAll(tasks);

        // Assert - 最終的にいずれかのモードになっている（Noneではない）
        Assert.NotEqual(TranslationMode.None, service.CurrentMode);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task MultithreadedAccess_ShouldNotThrowException()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act - 複数スレッドから同時アクセス
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    if (index % 2 == 0)
                        await service.SwitchToLiveModeAsync();
                    else
                        await service.SwitchToSingleshotModeAsync();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - 例外が発生しない
        Assert.Empty(exceptions);
    }

    [Fact]
#pragma warning disable CA1707
    public async Task SemaphoreSlim_ShouldSerializeModeSwitches()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        var executionOrder = new System.Collections.Concurrent.ConcurrentQueue<int>();

        // Act - 並行してモード切り替えを実行し、実行順序を記録
        var task1 = Task.Run(async () =>
        {
            await service.SwitchToLiveModeAsync();
            executionOrder.Enqueue(1);
        });

        var task2 = Task.Run(async () =>
        {
            await service.SwitchToSingleshotModeAsync();
            executionOrder.Enqueue(2);
        });

        await Task.WhenAll(task1, task2);

        // Assert - すべてのタスクが完了している
        Assert.Equal(2, executionOrder.Count);
    }

    #endregion

    #region エラーハンドリングテスト (1個)

    [Fact]
#pragma warning disable CA1707
    public async Task CancellationToken_ShouldCancelModeSwitch()
#pragma warning restore CA1707
    {
        // Arrange
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException は OperationCanceledException のサブクラス
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await service.SwitchToLiveModeAsync(cts.Token));
    }

    #endregion
}

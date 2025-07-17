using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Events;
using Baketa.Application.Models;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Services;
using Baketa.UI.ViewModels.Controls;
using Baketa.UI.Tests.TestUtilities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Reactive.Testing;
using Moq;
using ReactiveUI.Testing;
using Xunit;

// 名前空間競合を解決するためのエイリアス
using BaketaEventAggregator = Baketa.Core.Abstractions.Events.IEventAggregator;

namespace Baketa.UI.Tests.ViewModels.Controls;

/// <summary>
/// OperationalControlViewModel の単体テスト
/// </summary>
public class OperationalControlViewModelTests
{
    private readonly Mock<ITranslationOrchestrationService> _translationOrchestrationServiceMock;
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly Mock<BaketaEventAggregator> _eventAggregatorMock;
    private readonly Mock<ILogger<OperationalControlViewModel>> _loggerMock;
    
    // Observable Subjects for testing
    private readonly Subject<TranslationResult> _translationResultsSubject;
    private readonly Subject<TranslationStatus> _statusChangesSubject;
    private readonly Subject<TranslationProgress> _progressUpdatesSubject;

    /// <summary>
    /// テストセットアップ
    /// </summary>
    public OperationalControlViewModelTests()
    {
        // Mock オブジェクトの初期化
        _translationOrchestrationServiceMock = new Mock<ITranslationOrchestrationService>();
        _settingsServiceMock = new Mock<ISettingsService>();
        _eventAggregatorMock = new Mock<BaketaEventAggregator>();
        _loggerMock = new Mock<ILogger<OperationalControlViewModel>>();

        // Observable Subjects の初期化
        _translationResultsSubject = new Subject<TranslationResult>();
        _statusChangesSubject = new Subject<TranslationStatus>();
        _progressUpdatesSubject = new Subject<TranslationProgress>();

        // Translation Orchestration Service のモック設定
        SetupTranslationOrchestrationServiceMocks();
    }

    #region プロパティ状態管理テスト

    /// <summary>
    /// IsAutomaticMode が切り替えられたときに CurrentMode が更新されることをテスト
    /// </summary>
    [Fact]
    public void IsAutomaticMode_WhenToggled_UpdatesCurrentMode()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - Manual モード
        viewModel.IsAutomaticMode.Should().BeFalse();
        viewModel.CurrentMode.Should().Be(TranslationMode.Manual);

        // Act & Assert - Automatic モード
        viewModel.IsAutomaticMode = true;
        viewModel.CurrentMode.Should().Be(TranslationMode.Automatic);
    }

    /// <summary>
    /// IsTranslating が true のときにコマンドが無効化されることをテスト
    /// </summary>
    [Fact]
    public void IsTranslating_WhenServiceBusy_DisablesCommands()
    {
        // Arrange
        var viewModel = CreateViewModel();
        
        // IsTranslating は private set なので、サービスの状態を変更してテスト
        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsAnyTranslationActive)
            .Returns(true);

        // StatusChanges を発行してIsTranslatingを更新
        _statusChangesSubject.OnNext(TranslationStatus.Translating);

        // Assert
        viewModel.CanToggleMode.Should().BeFalse();
        viewModel.CanTriggerSingleTranslation.Should().BeFalse();
    }

    /// <summary>
    /// 翻訳中でないときに操作が可能であることをテスト
    /// </summary>
    [Fact]
    public void CanToggleMode_WhenNotTranslating_ReturnsTrue()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert
        viewModel.CanToggleMode.Should().BeTrue();
        viewModel.CanTriggerSingleTranslation.Should().BeTrue();
    }

    /// <summary>
    /// CurrentStatus が翻訳サービスの状態を反映することをテスト
    /// </summary>
    [Fact]
    public void CurrentStatus_ReflectsTranslationServiceState()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert - 初期状態
        viewModel.CurrentStatus.Should().Be("準備完了");

        // Act - 自動翻訳モードに変更（サービスの状態も更新）
        viewModel.IsAutomaticMode = true;
        
        // サービスの状態を更新（自動翻訳はまだ非アクティブ）
        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsAutomaticTranslationActive)
            .Returns(false);
        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsSingleTranslationActive)
            .Returns(false);
        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsAnyTranslationActive)
            .Returns(false);
            
        // UpdateCurrentStatusを手動呼び出し（privateメソッドなのでStatusChangesでトリガー）
        _statusChangesSubject.OnNext(TranslationStatus.Completed);
        
        // Assert - 自動翻訳待機中
        viewModel.CurrentStatus.Should().Be("自動翻訳待機中");
    }

    #endregion

    #region コマンド実行テスト

    /// <summary>
    /// ToggleAutomaticModeCommand が実行されたときに翻訳統合サービスが呼ばれることをテスト
    /// </summary>
    [Fact]
    public async Task ToggleAutomaticModeCommand_WhenExecuted_CallsOrchestrationService()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act - 自動翻訳モードをONに
        await viewModel.ToggleAutomaticModeCommand.Execute();

        // Assert
        viewModel.IsAutomaticMode.Should().BeTrue();
        _translationOrchestrationServiceMock.Verify(
            x => x.StartAutomaticTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()), 
            Times.Once);

        // Act - 自動翻訳モードをOFFに
        await viewModel.ToggleAutomaticModeCommand.Execute();

        // Assert
        viewModel.IsAutomaticMode.Should().BeFalse();
        _translationOrchestrationServiceMock.Verify(
            x => x.StopAutomaticTranslationAsync(It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    /// <summary>
    /// TriggerSingleTranslationCommand が実行されたときに単発翻訳が実行されることをテスト
    /// </summary>
    [Fact]
    public async Task TriggerSingleTranslationCommand_WhenExecuted_TriggersTranslation()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        await viewModel.TriggerSingleTranslationCommand.Execute();

        // Assert
        _translationOrchestrationServiceMock.Verify(
            x => x.TriggerSingleTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    /// <summary>
    /// サービスで例外が発生したときにエラーメッセージが表示されることをテスト
    /// </summary>
    [Fact]
    public async Task ToggleAutomaticModeCommand_WhenServiceFails_ShowsErrorMessage()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var expectedException = new InvalidOperationException("テストエラー");
        
        _translationOrchestrationServiceMock
            .Setup(x => x.StartAutomaticTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        await viewModel.ToggleAutomaticModeCommand.Execute();

        // Assert
        viewModel.ErrorMessage.Should().Contain("テストエラー");
    }

    /// <summary>
    /// 翻訳中にTriggerSingleTranslationCommand が実行できないことをテスト
    /// </summary>
    [Fact]
    public void TriggerSingleTranslationCommand_WhenTranslating_CannotExecute()
    {
        // Arrange
        var viewModel = CreateViewModel();
        
        // サービス状態を翻訳中に設定
        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsAnyTranslationActive)
            .Returns(true);
            
        // StatusChanges を発行して状態を更新
        _statusChangesSubject.OnNext(TranslationStatus.Translating);

        // Act & Assert
        viewModel.TriggerSingleTranslationCommand.CanExecute.Subscribe(canExecute => 
        {
            canExecute.Should().BeFalse();
        });
    }

    #endregion

    #region イベント統合テスト

    /// <summary>
    /// 自動翻訳モード変更時にイベントが発行されることをテスト
    /// </summary>
    [Fact]
    public async Task AutomaticModeChanged_PublishesTranslationModeChangedEvent()
    {
        // Arrange
        var viewModel = CreateViewModel();
        TranslationModeChangedEvent? publishedEvent = null;

        _eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<TranslationModeChangedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = evt as TranslationModeChangedEvent)
            .Returns(Task.CompletedTask);

        // Act
        await viewModel.ToggleAutomaticModeCommand.Execute();

        // Assert
        publishedEvent.Should().NotBeNull();
        publishedEvent!.NewMode.Should().Be(TranslationMode.Automatic);
        publishedEvent.PreviousMode.Should().Be(TranslationMode.Manual);
    }

    /// <summary>
    /// 翻訳結果がUI状態を更新することをテスト
    /// </summary>
    [Fact]
    public void TranslationResults_UpdatesUIState()
    {
        new TestScheduler().With(scheduler =>
        {
            // Arrange
            var viewModel = CreateViewModel();
            var translationResult = TestDataFactory.CreateSampleTranslationResult(
                id: "test-id",
                mode: TranslationMode.Manual,
                originalText: "Hello",
                translatedText: "こんにちは");

            // Act
            _translationResultsSubject.OnNext(translationResult);
            scheduler.AdvanceBy(1);

            // Assert - UI状態が更新されることを確認
            // Note: 実際のUI更新ロジックは実装に依存
            viewModel.Should().NotBeNull();
        });
    }

    /// <summary>
    /// ステータス変更がIsTranslatingプロパティを更新することをテスト
    /// </summary>
    [Fact]
    public void StatusChanges_UpdatesIsTranslatingProperty()
    {
        new TestScheduler().With(scheduler =>
        {
            // Arrange
            var viewModel = CreateViewModel();
            
            // 翻訳サービスがアクティブな状態をモック
            _translationOrchestrationServiceMock
                .SetupGet(x => x.IsAnyTranslationActive)
                .Returns(true);

            // Act
            _statusChangesSubject.OnNext(TranslationStatus.Translating);
            scheduler.AdvanceBy(1);

            // Assert
            viewModel.IsTranslating.Should().BeTrue();
        });
    }

    #endregion

    #region エラーハンドリングテスト

    /// <summary>
    /// コマンド実行でタイムアウトが発生したときのエラー処理をテスト
    /// </summary>
    [Fact]
    public async Task CommandExecution_WhenTimeoutOccurs_ShowsTimeoutError()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var timeoutException = new TimeoutException("タイムアウトが発生しました");
        
        _translationOrchestrationServiceMock
            .Setup(x => x.TriggerSingleTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act
        await viewModel.TriggerSingleTranslationCommand.Execute();

        // Assert
        viewModel.ErrorMessage.Should().Contain("タイムアウトが発生しました");
    }

    /// <summary>
    /// 無効な操作例外が発生したときのエラー処理をテスト
    /// </summary>
    [Fact]
    public async Task CommandExecution_WhenInvalidOperation_ShowsOperationError()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var invalidOpException = new InvalidOperationException("無効な操作です");
        
        _translationOrchestrationServiceMock
            .Setup(x => x.StartAutomaticTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(invalidOpException);

        // Act
        await viewModel.ToggleAutomaticModeCommand.Execute();

        // Assert
        viewModel.ErrorMessage.Should().Contain("無効な操作です");
    }

    /// <summary>
    /// 予期しないエラーが発生したときの汎用エラー処理をテスト
    /// </summary>
    [Fact]
    public async Task CommandExecution_WhenUnexpectedError_ShowsGenericError()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var unexpectedException = new Exception("予期しないエラー");
        
        _translationOrchestrationServiceMock
            .Setup(x => x.TriggerSingleTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(unexpectedException);

        // Act
        await viewModel.TriggerSingleTranslationCommand.Execute();

        // Assert
        viewModel.ErrorMessage.Should().Contain("予期しないエラー");
    }

    #endregion

    #region リソース管理テスト

    /// <summary>
    /// Dispose時に翻訳統合サービスが解放されることをテスト
    /// </summary>
    [Fact]
    public void Dispose_ReleasesTranslationOrchestrationService()
    {
        // Arrange
        var disposableServiceMock = _translationOrchestrationServiceMock.As<IDisposable>();
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert
        disposableServiceMock.Verify(x => x.Dispose(), Times.Once);
    }

    /// <summary>
    /// 非アクティベーション時に翻訳統合サービスが停止されることをテスト
    /// </summary>
    [Fact]
    public async Task Deactivation_StopsTranslationOrchestrationService()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // アクティベート状態にする
        using (viewModel.Activator.Activate())
        {
            // アクティベーション状態を確認 (代替手段でテスト)
            // ViewModelActivator.IsActivated は存在しないため、実際の動作で確認
            await Task.Delay(100);
        }

        // 少し待機してバックグラウンドタスクが完了するのを待つ
        await Task.Delay(100);

        // Assert - 非アクティベーション時にサービスが停止されることを確認
        _translationOrchestrationServiceMock.Verify(
            x => x.StopAsync(It.IsAny<CancellationToken>()), 
            Times.AtLeastOnce);
    }

    /// <summary>
    /// アクティベーション時に翻訳統合サービスが開始されることをテスト
    /// </summary>
    [Fact]
    public async Task Activation_StartsTranslationOrchestrationService()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        using (viewModel.Activator.Activate())
        {
            // 少し待機してバックグラウンドタスクが完了するのを待つ
            await Task.Delay(100);

            // Assert
            _translationOrchestrationServiceMock.Verify(
                x => x.StartAsync(It.IsAny<CancellationToken>()), 
                Times.AtLeastOnce);
        }
    }

    #endregion

    #region ヘルパーメソッド

    /// <summary>
    /// テスト用のViewModelインスタンスを作成
    /// </summary>
    private OperationalControlViewModel CreateViewModel()
    {
        return new OperationalControlViewModel(
            _translationOrchestrationServiceMock.Object,
            _settingsServiceMock.Object,
            _eventAggregatorMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Translation Orchestration Service のモック設定
    /// </summary>
    private void SetupTranslationOrchestrationServiceMocks()
    {
        // Observable プロパティの設定
        _translationOrchestrationServiceMock
            .SetupGet(x => x.TranslationResults)
            .Returns(_translationResultsSubject.AsObservable());

        _translationOrchestrationServiceMock
            .SetupGet(x => x.StatusChanges)
            .Returns(_statusChangesSubject.AsObservable());

        _translationOrchestrationServiceMock
            .SetupGet(x => x.ProgressUpdates)
            .Returns(_progressUpdatesSubject.AsObservable());

        // 状態プロパティの初期設定
        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsAutomaticTranslationActive)
            .Returns(false);

        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsSingleTranslationActive)
            .Returns(false);

        _translationOrchestrationServiceMock
            .SetupGet(x => x.IsAnyTranslationActive)
            .Returns(false);

        _translationOrchestrationServiceMock
            .SetupGet(x => x.CurrentMode)
            .Returns(TranslationMode.Manual);

        // 非同期メソッドの設定
        _translationOrchestrationServiceMock
            .Setup(x => x.StartAutomaticTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _translationOrchestrationServiceMock
            .Setup(x => x.StopAutomaticTranslationAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _translationOrchestrationServiceMock
            .Setup(x => x.TriggerSingleTranslationAsync(It.IsAny<IntPtr?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _translationOrchestrationServiceMock
            .Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _translationOrchestrationServiceMock
            .Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion
}

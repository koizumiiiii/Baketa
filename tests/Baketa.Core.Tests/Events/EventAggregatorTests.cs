using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Implementation;
using Microsoft.Extensions.Logging;
using System.Threading;
using Xunit;

namespace Baketa.Core.Tests.Events;

    /// <summary>
    /// イベント集約機構のテスト
    /// </summary>
    public class EventAggregatorTests
    {
        private readonly EventAggregator _eventAggregator;
        private readonly TestEventProcessor _testProcessor;
        private readonly ErrorTestEventProcessor _errorProcessor;
        private readonly CancellationTestProcessor _cancellationProcessor;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public EventAggregatorTests()
        {
            // テスト用のイベント集約機構を作成
            _eventAggregator = new EventAggregator();
            
            // テスト用のイベントプロセッサを作成
            _testProcessor = new TestEventProcessor();
            _errorProcessor = new ErrorTestEventProcessor();
            _cancellationProcessor = new CancellationTestProcessor();
        }

        /// <summary>
        /// 基本的なイベント発行と購読のテスト
        /// </summary>
        [Fact]
        public async Task PublishAsync_WithRegisteredProcessor_ProcessesEvent()
        {
            // Arrange
            var testEvent = new TestEvent("テストデータ");
            _eventAggregator.Subscribe(_testProcessor);

            // Act
            await _eventAggregator.PublishAsync(testEvent);
            
            // 🚀 Phase 2対応: 非ブロッキング処理の完了を待機
            await Task.Delay(100); // 非同期処理の完了を待機

            // Assert
            Assert.Equal(1, _testProcessor.CallCount);
            Assert.Single(_testProcessor.ProcessedEvents);
            Assert.Equal("テストデータ", _testProcessor.ProcessedEvents[0]);
        }

        /// <summary>
        /// 購読解除のテスト
        /// </summary>
        [Fact]
        public async Task Unsubscribe_AfterSubscription_DoesNotProcessEvent()
        {
            // Arrange
            var testEvent = new TestEvent("テストデータ");
            _eventAggregator.Subscribe(_testProcessor);
            
            // 一度イベントを発行して登録確認
            await _eventAggregator.PublishAsync(testEvent);
            await Task.Delay(100); // 🚀 Phase 2対応: 非ブロッキング処理完了待機
            Assert.Equal(1, _testProcessor.CallCount);
            
            // テスト履歴をクリア
            _testProcessor.ClearHistory();
            
            // 購読解除
            _eventAggregator.Unsubscribe(_testProcessor);
            
            // Act
            await _eventAggregator.PublishAsync(testEvent);
            await Task.Delay(100); // 🚀 Phase 2対応: 非ブロッキング処理完了待機
            
            // Assert
            Assert.Equal(0, _testProcessor.CallCount);
            Assert.Empty(_testProcessor.ProcessedEvents);
        }

        /// <summary>
        /// 複数のプロセッサでのイベント処理テスト
        /// </summary>
        [Fact]
        public async Task PublishAsync_WithMultipleProcessors_ProcessesEventInAll()
        {
            // Arrange
            var testEvent = new TestEvent("テストデータ");
            var secondProcessor = new TestEventProcessor();
            
            _eventAggregator.Subscribe(_testProcessor);
            _eventAggregator.Subscribe(secondProcessor);
            
            // Act
            await _eventAggregator.PublishAsync(testEvent);
            await Task.Delay(100); // 🚀 Phase 2対応: 非ブロッキング処理完了待機
            
            // Assert
            Assert.Equal(1, _testProcessor.CallCount);
            Assert.Equal(1, secondProcessor.CallCount);
            Assert.Equal("テストデータ", _testProcessor.ProcessedEvents[0]);
            Assert.Equal("テストデータ", secondProcessor.ProcessedEvents[0]);
        }

        /// <summary>
        /// 登録されていないイベントタイプの発行テスト
        /// </summary>
        [Fact]
        public async Task PublishAsync_WithNoRegisteredProcessors_ReturnsWithoutError()
        {
            // Arrange
            var testEvent = new TestEvent("テストデータ");
            
            // イベントプロセッサは登録しない
            
            // Act & Assert
            // エラーが発生しないことを確認
            await _eventAggregator.PublishAsync(testEvent);
            
            // 検証すべき状態はないが、正常に完了することを確認
        }

        /// <summary>
        /// プロセッサでエラーが発生した場合のテスト
        /// </summary>
        [Fact]
        public async Task PublishAsync_WhenProcessorThrowsError_ContinuesProcessing()
        {
            // Arrange
            var errorEvent = new ErrorTestEvent("エラーテスト", true);
            var testEvent = new TestEvent("通常テスト");
            
            _eventAggregator.Subscribe(_errorProcessor);
            _eventAggregator.Subscribe(_testProcessor);
            
            // Act
            // エラーイベントを発行
            await _eventAggregator.PublishAsync(errorEvent);
            await Task.Delay(100); // 🚀 Phase 2対応: 非ブロッキング処理完了待機
            
            // 通常イベントを発行
            await _eventAggregator.PublishAsync(testEvent);
            await Task.Delay(100); // 🚀 Phase 2対応: 非ブロッキング処理完了待機
            
            // Assert
            Assert.True(_errorProcessor.ErrorOccurred);
            Assert.Equal(1, _errorProcessor.ErrorCount);
            Assert.Equal(1, _testProcessor.CallCount);
        }

        /// <summary>
        /// キャンセレーショントークンを使用したイベント発行のテスト
        /// </summary>
        [Fact]
        public void PublishAsync_WithCancellationToken_CancelsProcessing()
        {
            // 別のアプローチでテストを行う
            // キャンセルトークンを生成して、即座にキャンセル済みにする
            var cts = new CancellationTokenSource();
            cts.Cancel(); // 即座にキャンセル
            var canceledToken = cts.Token;

            // 既にキャンセルされたトークンで呼び出すと、すぐに例外が発生するはず
            var testEvent = new TestEvent("キャンセルテスト");
            _eventAggregator.Subscribe(_cancellationProcessor);
            _cancellationProcessor.Reset();
            
            // 即座にキャンセルされたトークンで引数を渡すと、例外が発生することを確認
            Assert.Throws<OperationCanceledException>(() => 
                _eventAggregator.PublishAsync(testEvent, canceledToken).GetAwaiter().GetResult());
            
            // イベント処理が実行されていないことを確認
            Assert.Equal(0, _cancellationProcessor.CompletionCount);
        }

        /// <summary>
        /// nullイベントの発行をテスト
        /// </summary>
        [Fact]
        public async Task PublishAsync_WithNullEvent_ThrowsArgumentNullException()
        {
            // Arrange
            TestEvent? nullEvent = null;
            
            // Act & Assert
#pragma warning disable CS8604 // テスト目的のnull参照の可能性
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _eventAggregator.PublishAsync(nullEvent!));
#pragma warning restore CS8604
        }

        /// <summary>
        /// 同一プロセッサの複数登録テスト
        /// </summary>
        [Fact]
        public async Task Subscribe_SameProcessorTwice_ProcessesEventOnce()
        {
            // Arrange
            var testEvent = new TestEvent("重複登録テスト");
            
            // 同じプロセッサを2回登録
            _eventAggregator.Subscribe(_testProcessor);
            _eventAggregator.Subscribe(_testProcessor);
            
            // Act
            await _eventAggregator.PublishAsync(testEvent);
            await Task.Delay(100); // 🚀 Phase 2対応: 非ブロッキング処理完了待機
            
            // Assert
            Assert.Equal(1, _testProcessor.CallCount);
        }
    }

using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Tests.Events;

    /// <summary>
    /// テスト用イベントプロセッサ
    /// </summary>
    public class TestEventProcessor : IEventProcessor<TestEvent>
    {
        private readonly List<string> _processedEvents = [];

        /// <summary>
        /// 処理されたイベントのデータリスト
        /// </summary>
        public IReadOnlyList<string> ProcessedEvents => _processedEvents;

        /// <summary>
        /// イベント処理が呼ばれた回数
        /// </summary>
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

        /// <inheritdoc />
        public async Task HandleAsync(TestEvent eventData)
        {
            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            // 非同期処理のシミュレーション
            await Task.Delay(10);

            CallCount++;
            _processedEvents.Add(eventData.Data);
        }

        /// <summary>
        /// 処理履歴のクリア
        /// </summary>
        public void ClearHistory()
        {
            CallCount = 0;
            _processedEvents.Clear();
        }
    }

    /// <summary>
    /// エラーテスト用イベントプロセッサ
    /// </summary>
    public class ErrorTestEventProcessor : IEventProcessor<ErrorTestEvent>
    {
        /// <summary>
        /// エラーが発生したかどうか
        /// </summary>
        public bool ErrorOccurred { get; private set; }

        /// <summary>
        /// 成功イベント数
        /// </summary>
        public int SuccessCount { get; private set; }

        /// <summary>
        /// エラーイベント数
        /// </summary>
        public int ErrorCount { get; private set; }

        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

        /// <inheritdoc />
        public async Task HandleAsync(ErrorTestEvent eventData)
        {
            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            // 非同期処理のシミュレーション
            await Task.Delay(10);

            if (eventData.ShouldThrowError)
            {
                ErrorOccurred = true;
                ErrorCount++;
                throw new InvalidOperationException("テスト用エラー");
            }

            SuccessCount++;
        }

        /// <summary>
        /// 状態のリセット
        /// </summary>
        public void Reset()
        {
            ErrorOccurred = false;
            SuccessCount = 0;
            ErrorCount = 0;
        }
    }

    /// <summary>
    /// 非同期キャンセルテスト用プロセッサ
    /// </summary>
    public class CancellationTestProcessor : IEventProcessor<TestEvent>
    {
        /// <summary>
        /// キャンセルされた回数
        /// </summary>
        public int CancellationCount { get; private set; }

        /// <summary>
        /// 完了した回数
        /// </summary>
        public int CompletionCount { get; private set; }

        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

        /// <inheritdoc />
        public async Task HandleAsync(TestEvent eventData)
        {
            // キャンセルトークンは外部からEventAggregatorにPublishAsyncに渡されたもので、
            // プロセッサ内では必要ならプロパゲートされるものであり、
            // このテストの目的は、イベント発行時にキャンセルがあれば
            // このハンドラがイベントを処理する前にキャンセルされることを確認すること

            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            // ハンドラメソッドが呼ばれれば、完了カウントを増やすだけ
            // このプロセッサが実行されたなら、CompletionCountが増える
            CompletionCount++;
            
            // 処理のシミュレーション
            await Task.Delay(10);
        }

        /// <summary>
        /// 状態のリセット
        /// </summary>
        public void Reset()
        {
            CancellationCount = 0;
            CompletionCount = 0;
        }
    }

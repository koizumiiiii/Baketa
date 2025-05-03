using Baketa.Core.Events;

namespace Baketa.Core.Tests.Events
{
    /// <summary>
    /// テスト用イベント
    /// </summary>
    public class TestEvent : EventBase
    {
        /// <summary>
        /// イベントデータ
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="data">イベントデータ</param>
        public TestEvent(string data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <inheritdoc />
        public override string Name => "TestEvent";

        /// <inheritdoc />
        public override string Category => "Test";
    }

    /// <summary>
    /// エラーテスト用イベント
    /// </summary>
    public class ErrorTestEvent : EventBase
    {
        /// <summary>
        /// イベントデータ
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// エラーを発生させるかどうか
        /// </summary>
        public bool ShouldThrowError { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="data">イベントデータ</param>
        /// <param name="shouldThrowError">エラーを発生させるかどうか</param>
        public ErrorTestEvent(string data, bool shouldThrowError)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ShouldThrowError = shouldThrowError;
        }

        /// <inheritdoc />
        public override string Name => "ErrorTestEvent";

        /// <inheritdoc />
        public override string Category => "Test";
    }
}
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events;

namespace Baketa.Core.Tests.Events;

/// <summary>
/// テスト用イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="data">イベントデータ</param>
public class TestEvent(string data) : EventBase
    {
    /// <summary>
    /// イベントデータ
    /// </summary>
    public string Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    /// <inheritdoc />
    public override string Name => "TestEvent";

        /// <inheritdoc />
        public override string Category => "Test";
    }

/// <summary>
/// エラーテスト用イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="data">イベントデータ</param>
/// <param name="shouldThrowError">エラーを発生させるかどうか</param>
public class ErrorTestEvent(string data, bool shouldThrowError) : EventBase
    {
    /// <summary>
    /// イベントデータ
    /// </summary>
    public string Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>
    /// エラーを発生させるかどうか
    /// </summary>
    public bool ShouldThrowError { get; } = shouldThrowError;

    /// <inheritdoc />
    public override string Name => "ErrorTestEvent";

        /// <inheritdoc />
        public override string Category => "Test";
    }

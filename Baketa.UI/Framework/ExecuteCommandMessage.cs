using System;

namespace Baketa.UI.Framework;

/// <summary>
/// コマンド実行メッセージ（デバッグ用）
/// </summary>
internal sealed class ExecuteCommandMessage
{
    /// <summary>
    /// コマンド名
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// コマンド実行メッセージを初期化します
    /// </summary>
    /// <param name="commandName">コマンド名</param>
    public ExecuteCommandMessage(string commandName)
    {
        ArgumentNullException.ThrowIfNull(commandName);
        CommandName = commandName;
    }
}

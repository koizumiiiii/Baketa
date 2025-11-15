using System;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// ゲーム状態の監視・判定を抽象化
/// Gemini改善提案: プラットフォーム固有ロジック分離
/// </summary>
public interface IGameStateProvider
{
    /// <summary>
    /// 現在ゲームがアクティブかどうか
    /// </summary>
    bool IsGameActive();

    /// <summary>
    /// 現在のゲーム情報
    /// </summary>
    GameInfo? CurrentGameInfo { get; }

    /// <summary>
    /// ゲーム状態変化イベント
    /// </summary>
    event EventHandler<GameStateChangedEventArgs>? GameStateChanged;
}

/// <summary>
/// ゲーム情報
/// </summary>
public record GameInfo(
    string ProcessName,
    string WindowTitle,
    bool IsFullScreen,
    DateTime DetectedAt
);

/// <summary>
/// ゲーム状態変化イベント引数
/// </summary>
public class GameStateChangedEventArgs : EventArgs
{
    public GameInfo? PreviousGame { get; }
    public GameInfo? CurrentGame { get; }
    public bool IsGameActivated { get; }

    public GameStateChangedEventArgs(GameInfo? previousGame, GameInfo? currentGame)
    {
        PreviousGame = previousGame;
        CurrentGame = currentGame;
        IsGameActivated = currentGame != null;
    }
}

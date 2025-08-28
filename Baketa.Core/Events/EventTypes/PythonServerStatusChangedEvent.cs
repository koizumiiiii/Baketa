using Baketa.Core.Events;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// Pythonサーバーの状態変更イベント
/// StartButton制御機能で使用（Phase 0: 応急対策）
/// </summary>
public sealed class PythonServerStatusChangedEvent : EventBase
{
    /// <summary>
    /// サーバーが準備完了状態かどうか
    /// </summary>
    public bool IsServerReady { get; init; }
    
    /// <summary>
    /// 状態メッセージ（ユーザー表示用）
    /// </summary>
    public string StatusMessage { get; init; } = string.Empty;
    
    /// <summary>
    /// サーバーポート番号（診断用）
    /// </summary>
    public int ServerPort { get; init; }
    
    /// <summary>
    /// 状態変更の詳細情報（ログ用）
    /// </summary>
    public string Details { get; init; } = string.Empty;

    /// <inheritdoc />
    public override string Name => "PythonServerStatusChanged";
    
    /// <inheritdoc />
    public override string Category => "Translation";

    /// <summary>
    /// サーバー準備完了イベントを作成
    /// </summary>
    public static PythonServerStatusChangedEvent CreateServerReady(int port, string details = "")
        => new()
        {
            IsServerReady = true,
            StatusMessage = "翻訳サーバー準備完了",
            ServerPort = port,
            Details = details
        };

    /// <summary>
    /// サーバー初期化中イベントを作成
    /// </summary>
    public static PythonServerStatusChangedEvent CreateServerInitializing(int port, string details = "")
        => new()
        {
            IsServerReady = false,
            StatusMessage = "翻訳サーバー初期化中...",
            ServerPort = port,
            Details = details
        };

    /// <summary>
    /// サーバー失敗イベントを作成
    /// </summary>
    public static PythonServerStatusChangedEvent CreateServerFailed(int port, string details = "")
        => new()
        {
            IsServerReady = false,
            StatusMessage = "翻訳サーバーエラー",
            ServerPort = port,
            Details = details
        };
}
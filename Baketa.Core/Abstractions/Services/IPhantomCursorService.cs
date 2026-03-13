using System;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #497] ファントムカーソルサービスのインターフェース
/// ゲーム側がカーソルを非表示にしている場合に代替カーソルを表示する
/// </summary>
public interface IPhantomCursorService
{
    /// <summary>
    /// 監視対象のゲームウィンドウハンドルを設定
    /// </summary>
    void SetTargetWindow(nint windowHandle);

    /// <summary>
    /// ファントムカーソル機能を有効化
    /// </summary>
    void Enable();

    /// <summary>
    /// ファントムカーソル機能を無効化
    /// </summary>
    void Disable();

    /// <summary>
    /// 現在有効かどうか
    /// </summary>
    bool IsEnabled { get; }
}

/// <summary>
/// [Issue #497] ファントムカーソルウィンドウの抽象化（Platform層から注入）
/// </summary>
public interface IPhantomCursorWindowAdapter : IDisposable
{
    void UpdatePosition(int screenX, int screenY);
    void Show();
    void Hide();
}

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #497] カーソル状態の詳細情報
/// </summary>
public readonly record struct CursorState(
    bool IsHidden,
    int ScreenX,
    int ScreenY,
    uint Flags,
    nint CursorHandle)
{
    /// <summary>
    /// カーソル非表示の推定タイプ
    /// </summary>
    public CursorHiddenType HiddenType => IsHidden switch
    {
        false => CursorHiddenType.NotHidden,
        true when CursorHandle == 0 => CursorHiddenType.SetCursorNull,
        true => CursorHiddenType.ShowCursorFalse
    };
}

/// <summary>
/// カーソル非表示の推定タイプ
/// </summary>
public enum CursorHiddenType
{
    /// <summary>カーソルは表示されている</summary>
    NotHidden,
    /// <summary>ShowCursor(FALSE) でカウンタが0未満（flags=0, hCursor≠NULL）</summary>
    ShowCursorFalse,
    /// <summary>SetCursor(NULL) でカーソルリソースがnull（hCursor=NULL）</summary>
    SetCursorNull
}

/// <summary>
/// [Issue #497] カーソル状態を提供するインターフェース
/// Platform層のP/Invoke呼び出しを抽象化する
/// </summary>
public interface ICursorStateProvider
{
    /// <summary>
    /// システムカーソルが非表示かどうかを取得し、現在のスクリーン座標を返す
    /// </summary>
    bool IsCursorHidden(out int screenX, out int screenY);

    /// <summary>
    /// カーソル状態の詳細情報を取得
    /// </summary>
    CursorState GetCursorState();

    /// <summary>
    /// 指定ウィンドウがフォアグラウンドかどうか
    /// </summary>
    bool IsWindowForeground(nint windowHandle);

    /// <summary>
    /// 指定スクリーン座標がウィンドウのクライアント領域内かどうか
    /// </summary>
    bool IsPointInClientArea(nint windowHandle, int screenX, int screenY);
}

using System;
using System.Runtime.Versioning;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Win32 Layered Windowベースのオーバーレイウィンドウインターフェース
/// </summary>
/// <remarks>
/// 🎯 [WIN32_OVERLAY_MIGRATION] Phase 1: Clean Architecture準拠インターフェース
/// - Infrastructure層の具象実装から抽象化
/// - Application層/UI層での依存性逆転
/// - テストとモック化の容易化
/// </remarks>
[SupportedOSPlatform("windows")]
public interface ILayeredOverlayWindow : IDisposable
{
    /// <summary>
    /// オーバーレイウィンドウを表示
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_RECOMMENDATION] STAスレッド上で非同期に実行
    /// メッセージキューを経由してWin32ウィンドウに表示命令を送信
    /// </remarks>
    void Show();

    /// <summary>
    /// オーバーレイウィンドウを非表示
    /// </summary>
    void Hide();

    /// <summary>
    /// オーバーレイウィンドウを閉じる
    /// </summary>
    /// <remarks>
    /// 🔥 [CRITICAL] STAスレッドに PostQuitMessage を送信してメッセージループを終了
    /// リソースクリーンアップはDispose()で実施
    /// </remarks>
    void Close();

    /// <summary>
    /// 表示テキストを設定
    /// </summary>
    /// <param name="text">表示するテキスト</param>
    /// <remarks>
    /// スレッドセーフ: メッセージキュー経由でSTAスレッドに転送
    /// GDI描画によりビットマップを生成し、UpdateLayeredWindowで更新
    /// </remarks>
    void SetText(string text);

    /// <summary>
    /// ウィンドウ位置を設定
    /// </summary>
    /// <param name="x">X座標（スクリーン座標）</param>
    /// <param name="y">Y座標（スクリーン座標）</param>
    /// <remarks>
    /// スレッドセーフ: SetWindowPos経由で位置を更新
    /// HWND_TOPMOST により常に最前面表示
    /// </remarks>
    void SetPosition(int x, int y);

    /// <summary>
    /// ウィンドウサイズを設定
    /// </summary>
    /// <param name="width">幅</param>
    /// <param name="height">高さ</param>
    /// <remarks>
    /// テキスト変更時に自動計算されるため、通常は明示的な呼び出し不要
    /// GDI TextRenderer.MeasureTextでテキストサイズを測定
    /// </remarks>
    void SetSize(int width, int height);

    /// <summary>
    /// 背景色を設定（ARGB）
    /// </summary>
    /// <param name="a">アルファ値 (0-255)</param>
    /// <param name="r">赤成分 (0-255)</param>
    /// <param name="g">緑成分 (0-255)</param>
    /// <param name="b">青成分 (0-255)</param>
    /// <remarks>
    /// 🔥 [PHASE2_BLUR_READY] すりガラス効果の背景色
    /// CreateDIBSection で 32bit ARGB ビットマップ生成
    /// </remarks>
    void SetBackgroundColor(byte a, byte r, byte g, byte b);

    /// <summary>
    /// ウィンドウが表示中かどうか
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// ウィンドウハンドル（診断・デバッグ用）
    /// </summary>
    /// <remarks>
    /// 通常はアプリケーション層から使用しない
    /// Win32相互運用が必要な場合のみアクセス
    /// </remarks>
    IntPtr WindowHandle { get; }
}

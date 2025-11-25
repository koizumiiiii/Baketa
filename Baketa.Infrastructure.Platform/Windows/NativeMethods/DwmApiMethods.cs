using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods;

/// <summary>
/// Desktop Window Manager (DWM) API のP/Invoke宣言
/// </summary>
/// <remarks>
/// 🎯 [DWM_BLUR_IMPLEMENTATION] Composition Window + Blur効果用API
///
/// 主要機能:
/// - DwmExtendFrameIntoClientArea: ウィンドウ全体をガラス化（DWM Composition有効化）
/// - DwmEnableBlurBehindWindow: ブラー効果適用
/// - DwmSetWindowAttribute: ウィンドウ属性設定
///
/// 対応OS:
/// - Windows Vista以降（DWM Composition必須）
/// - Windows 7: 基本的なブラー効果
/// - Windows 10/11: Acrylic効果も利用可能（別API）
/// </remarks>
[SupportedOSPlatform("windows6.0")] // Windows Vista+
internal static class DwmApiMethods
{
    private const string DWM_API_DLL = "dwmapi.dll";

    #region Structures

    /// <summary>
    /// ウィンドウのマージン（ガラス化範囲）
    /// </summary>
    /// <remarks>
    /// すべて -1 に設定すると、ウィンドウ全体がガラス化される
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;

        /// <summary>
        /// ウィンドウ全体をガラス化するMARGINSを作成
        /// </summary>
        public static MARGINS CreateFullWindow() => new()
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
    }

    /// <summary>
    /// ブラー効果の設定
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_BLURBEHIND
    {
        public DWM_BB dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;

        /// <summary>
        /// ウィンドウ全体にブラー効果を適用する設定を作成
        /// </summary>
        public static DWM_BLURBEHIND CreateFullWindowBlur() => new()
        {
            dwFlags = DWM_BB.Enable | DWM_BB.BlurRegion,
            fEnable = true,
            hRgnBlur = IntPtr.Zero, // IntPtr.Zero = ウィンドウ全体
            fTransitionOnMaximized = false
        };
    }

    /// <summary>
    /// DWM_BLURBEHIND.dwFlags のフラグ
    /// </summary>
    [Flags]
    public enum DWM_BB : uint
    {
        Enable = 0x00000001,
        BlurRegion = 0x00000002,
        TransitionOnMaximized = 0x00000004
    }

    /// <summary>
    /// DWM Window属性
    /// </summary>
    /// <remarks>
    /// Windows 10以降で追加された属性も含む
    /// </remarks>
    public enum DWMWINDOWATTRIBUTE
    {
        /// <summary>
        /// 非クライアント領域のレンダリングポリシー
        /// </summary>
        DWMWA_NCRENDERING_POLICY = 2,

        /// <summary>
        /// システムバックドロップタイプ（Windows 11 22H2+）
        /// </summary>
        DWMWA_SYSTEMBACKDROP_TYPE = 38,

        /// <summary>
        /// ウィンドウコーナーの優先設定（Windows 11+）
        /// </summary>
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
    }

    /// <summary>
    /// システムバックドロップタイプ（Windows 11 22H2+）
    /// </summary>
    public enum DWM_SYSTEMBACKDROP_TYPE
    {
        /// <summary>
        /// 自動選択（デフォルト）
        /// </summary>
        DWMSBT_AUTO = 0,

        /// <summary>
        /// バックドロップなし
        /// </summary>
        DWMSBT_NONE = 1,

        /// <summary>
        /// Micaバックドロップ（Windows 11）
        /// </summary>
        DWMSBT_MAINWINDOW = 2,

        /// <summary>
        /// Acrylicバックドロップ（半透明ブラー）
        /// </summary>
        DWMSBT_TRANSIENTWINDOW = 3,

        /// <summary>
        /// Mica Altバックドロップ
        /// </summary>
        DWMSBT_TABBEDWINDOW = 4
    }

    #endregion

    #region DWM API Functions

    /// <summary>
    /// ウィンドウのフレームをクライアント領域に拡張（ガラス化）
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    /// <param name="pMarInset">拡張するマージン</param>
    /// <returns>成功時 S_OK (0)</returns>
    /// <remarks>
    /// 🔥 [DWM_COMPOSITION] ウィンドウ全体をDWM Compositionに参加させる
    /// - MARGINS を全て -1 にすると、ウィンドウ全体がガラス化される
    /// - WS_EX_LAYERED と併用不可（排他利用）
    /// - Windows Vista以降で利用可能
    /// </remarks>
    [DllImport(DWM_API_DLL, PreserveSig = true)]
    public static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref MARGINS pMarInset);

    /// <summary>
    /// ウィンドウの背後にブラー効果を適用
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    /// <param name="pBlurBehind">ブラー設定</param>
    /// <returns>成功時 S_OK (0)</returns>
    /// <remarks>
    /// 🔥 [BLUR_EFFECT] Windows Vista/7 スタイルのブラー効果
    /// - DwmExtendFrameIntoClientArea と併用して使用
    /// - Windows 10以降では Acrylic効果が推奨（別API）
    /// - hRgnBlur = IntPtr.Zero でウィンドウ全体にブラー適用
    /// </remarks>
    [DllImport(DWM_API_DLL, PreserveSig = true)]
    public static extern int DwmEnableBlurBehindWindow(
        IntPtr hwnd,
        ref DWM_BLURBEHIND pBlurBehind);

    /// <summary>
    /// DWMウィンドウ属性を設定
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    /// <param name="dwAttribute">設定する属性</param>
    /// <param name="pvAttribute">属性値へのポインタ</param>
    /// <param name="cbAttribute">属性値のサイズ（バイト）</param>
    /// <returns>成功時 S_OK (0)</returns>
    /// <remarks>
    /// 🔥 [MODERN_BACKDROP] Windows 11のMica/Acrylic効果用
    /// - DWMWA_SYSTEMBACKDROP_TYPE: Windows 11 22H2以降
    /// - DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW: Acrylic効果
    /// </remarks>
    [DllImport(DWM_API_DLL, PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        DWMWINDOWATTRIBUTE dwAttribute,
        IntPtr pvAttribute,
        uint cbAttribute);

    /// <summary>
    /// DWM Compositionが有効かどうかを確認
    /// </summary>
    /// <param name="pfEnabled">Composition有効フラグ</param>
    /// <returns>成功時 S_OK (0)</returns>
    /// <remarks>
    /// Windows Vista/7: DWM Compositionが無効化されている可能性がある
    /// Windows 8以降: 常にtrue（DWM Composition常時有効）
    /// </remarks>
    [DllImport(DWM_API_DLL, PreserveSig = true)]
    public static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    #endregion

    #region Helper Methods

    /// <summary>
    /// HRESULTが成功を示すかチェック
    /// </summary>
    /// <param name="hresult">HRESULT値</param>
    /// <returns>成功時 true</returns>
    public static bool SUCCEEDED(int hresult) => hresult >= 0;

    /// <summary>
    /// HRESULTが失敗を示すかチェック
    /// </summary>
    /// <param name="hresult">HRESULT値</param>
    /// <returns>失敗時 true</returns>
    public static bool FAILED(int hresult) => hresult < 0;

    /// <summary>
    /// DWM Compositionがサポートされているかチェック
    /// </summary>
    /// <returns>サポートされている場合 true</returns>
    /// <remarks>
    /// Windows Vista以降でサポート
    /// </remarks>
    public static bool IsCompositionSupported()
    {
        try
        {
            var result = DwmIsCompositionEnabled(out var enabled);
            return SUCCEEDED(result) && enabled;
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll が見つからない = Windows XP以前
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            // API が見つからない = サポートされていない
            return false;
        }
    }

    #endregion
}

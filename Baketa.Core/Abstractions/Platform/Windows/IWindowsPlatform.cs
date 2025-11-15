using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;

namespace Baketa.Core.Abstractions.Platform.Windows;

/// <summary>
/// Windows固有のプラットフォーム機能を提供するインターフェース
/// </summary>
public interface IWindowsPlatform : IPlatform
{
    /// <summary>
    /// Windows画像ファクトリを取得
    /// </summary>
    Factories.IWindowsImageFactory ImageFactory { get; }

    /// <summary>
    /// Windowsキャプチャサービスを取得
    /// </summary>
    IWindowsCapturer Capturer { get; }

    /// <summary>
    /// Windowsウィンドウ管理サービスを取得
    /// </summary>
    IWindowManager WindowManager { get; }

    /// <summary>
    /// Windowsバージョン
    /// </summary>
    Version WindowsVersion { get; }

    /// <summary>
    /// DWM（Desktop Window Manager）が有効かどうか
    /// </summary>
    bool IsDwmEnabled { get; }

    /// <summary>
    /// 管理者権限で実行されているかどうか
    /// </summary>
    bool IsRunningAsAdministrator { get; }

    /// <summary>
    /// システム情報を取得
    /// </summary>
    /// <returns>システム情報</returns>
    Task<WindowsSystemInfo> GetSystemInfoAsync();
}

/// <summary>
/// Windowsシステム情報
/// </summary>
public class WindowsSystemInfo
{
    /// <summary>
    /// オペレーティングシステム名
    /// </summary>
    public required string OsName { get; set; }

    /// <summary>
    /// オペレーティングシステムバージョン
    /// </summary>
    public required Version OsVersion { get; set; }

    /// <summary>
    /// プロセッサ情報
    /// </summary>
    public required string ProcessorInfo { get; set; }

    /// <summary>
    /// 合計物理メモリ (バイト)
    /// </summary>
    public ulong TotalPhysicalMemory { get; set; }

    /// <summary>
    /// 利用可能な物理メモリ (バイト)
    /// </summary>
    public ulong AvailablePhysicalMemory { get; set; }

    /// <summary>
    /// ディスプレイデバイス情報
    /// </summary>
    public required IReadOnlyList<DisplayDeviceInfo> DisplayDevices { get; set; }
}

/// <summary>
/// ディスプレイデバイス情報
/// </summary>
public class DisplayDeviceInfo
{
    /// <summary>
    /// デバイス名
    /// </summary>
    public required string DeviceName { get; set; }

    /// <summary>
    /// 解像度（幅）
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 解像度（高さ）
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// リフレッシュレート (Hz)
    /// </summary>
    public int RefreshRate { get; set; }

    /// <summary>
    /// ビットデプス
    /// </summary>
    public int BitsPerPixel { get; set; }

    /// <summary>
    /// プライマリディスプレイかどうか
    /// </summary>
    public bool IsPrimary { get; set; }
}

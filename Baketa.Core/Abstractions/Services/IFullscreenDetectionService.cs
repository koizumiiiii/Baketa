using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// フルスクリーン検出方法
/// </summary>
public enum FullscreenDetectionMethod
{
    /// <summary>
    /// ウィンドウサイズによる検出
    /// </summary>
    WindowSize,

    /// <summary>
    /// ウィンドウスタイルによる検出
    /// </summary>
    WindowStyle,

    /// <summary>
    /// DirectXによる検出
    /// </summary>
    DirectX,

    /// <summary>
    /// 複合的な検出
    /// </summary>
    Combined
}

/// <summary>
/// フルスクリーン状態情報
/// </summary>
public class FullscreenInfo
{
    /// <summary>
    /// フルスクリーン状態かどうか
    /// </summary>
    public bool IsFullscreen { get; set; }

    /// <summary>
    /// 対象ウィンドウハンドル
    /// </summary>
    public IntPtr WindowHandle { get; set; }

    /// <summary>
    /// ウィンドウサイズ
    /// </summary>
    public Rectangle WindowBounds { get; set; }

    /// <summary>
    /// 対象モニターの解像度
    /// </summary>
    public Rectangle MonitorBounds { get; set; }

    /// <summary>
    /// フルスクリーン検出の信頼度（0.0-1.0）
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 検出方法
    /// </summary>
    public FullscreenDetectionMethod DetectionMethod { get; set; }

    /// <summary>
    /// 検出時刻
    /// </summary>
    public DateTime DetectionTime { get; set; } = DateTime.Now;

    /// <summary>
    /// ウィンドウのプロセス名
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// ウィンドウタイトル
    /// </summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// ゲームかどうかの推定
    /// </summary>
    public bool IsLikelyGame { get; set; }

    /// <summary>
    /// フルスクリーン情報の概要を文字列で返します
    /// </summary>
    /// <returns>フルスクリーン情報の概要</returns>
    public override string ToString()
    {
        return $"IsFullscreen: {IsFullscreen}, Confidence: {Confidence:F2}, " +
               $"Method: {DetectionMethod}, Process: {ProcessName}, " +
               $"Size: {WindowBounds.Size}, Monitor: {MonitorBounds.Size}";
    }
}

/// <summary>
/// フルスクリーン検出設定
/// </summary>
public class FullscreenDetectionSettings
{
    /// <summary>
    /// 検出間隔（ミリ秒）
    /// </summary>
    public int DetectionIntervalMs { get; set; } = 1000;

    /// <summary>
    /// サイズ一致の許容誤差（ピクセル）
    /// </summary>
    public int SizeTolerance { get; set; } = 5;

    /// <summary>
    /// 検出方法の優先順位
    /// </summary>
    public FullscreenDetectionMethod[] PreferredMethods { get; set; } =
    [
        FullscreenDetectionMethod.Combined,
        FullscreenDetectionMethod.WindowSize,
        FullscreenDetectionMethod.WindowStyle
    ];

    /// <summary>
    /// 最小信頼度閾値
    /// </summary>
    public double MinConfidence { get; set; } = 0.8;

    /// <summary>
    /// ゲーム検出のための既知のゲーム実行ファイル名
    /// </summary>
    public HashSet<string> KnownGameExecutables { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unity.exe",
        "UnrealEngine.exe",
        "steam.exe",
        "steamapps",
        "Game.exe",
        "game.exe",
        "launcher.exe",
        "client.exe"
    };

    /// <summary>
    /// ゲーム検出のための除外プロセス名
    /// </summary>
    public HashSet<string> ExcludedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "dwm.exe",
        "winlogon.exe",
        "csrss.exe",
        "services.exe"
    };

    /// <summary>
    /// 設定のクローンを作成します
    /// </summary>
    /// <returns>クローンされた設定</returns>
    public FullscreenDetectionSettings Clone()
    {
        return new FullscreenDetectionSettings
        {
            DetectionIntervalMs = DetectionIntervalMs,
            SizeTolerance = SizeTolerance,
            PreferredMethods = (FullscreenDetectionMethod[])PreferredMethods.Clone(),
            MinConfidence = MinConfidence,
            KnownGameExecutables = new HashSet<string>(KnownGameExecutables, StringComparer.OrdinalIgnoreCase),
            ExcludedProcesses = new HashSet<string>(ExcludedProcesses, StringComparer.OrdinalIgnoreCase)
        };
    }
}

/// <summary>
/// フルスクリーン検出サービス
/// ゲームやアプリケーションのフルスクリーン状態を高精度で検出
/// </summary>
public interface IFullscreenDetectionService
{
    /// <summary>
    /// 現在の検出設定
    /// </summary>
    FullscreenDetectionSettings Settings { get; }

    /// <summary>
    /// 検出サービスが実行中かどうか
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 指定されたウィンドウがフルスクリーンかどうかを検出します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>フルスクリーン情報</returns>
    Task<FullscreenInfo> DetectFullscreenAsync(IntPtr windowHandle);

    /// <summary>
    /// 現在のフォアグラウンドウィンドウがフルスクリーンかどうかを検出します
    /// </summary>
    /// <returns>フルスクリーン情報</returns>
    Task<FullscreenInfo> DetectCurrentFullscreenAsync();

    /// <summary>
    /// フルスクリーン状態の変更を監視します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>フルスクリーン状態変更の通知</returns>
    IAsyncEnumerable<FullscreenInfo> MonitorFullscreenChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// フルスクリーン検出設定を更新します
    /// </summary>
    /// <param name="settings">検出設定</param>
    void UpdateDetectionSettings(FullscreenDetectionSettings settings);

    /// <summary>
    /// 監視を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 監視を停止します
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// フルスクリーン状態変更イベント
    /// </summary>
    /// <remarks>
    /// 設計上、FullscreenInfoを直接イベント引数として使用しています。
    /// EventArgsの継承は今後のバージョンで検討予定です。
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "FullscreenInfo contains all necessary event data")]
    event EventHandler<FullscreenInfo>? FullscreenStateChanged;
}

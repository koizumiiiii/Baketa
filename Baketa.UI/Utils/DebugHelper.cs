using System;
using System.IO;

namespace Baketa.UI.Utils;

/// <summary>
/// デバッグ用ヘルパークラス
/// Console.WriteLineが表示されない場合の代替手段
/// </summary>
public static class DebugHelper
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "baketa_debug.log");

    /// <summary>
    /// ファイルとコンソールの両方にログ出力
    /// デバッグ時のみ有効（#if DEBUG）
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        var logMessage = $"[{timestamp}] {message}";
        
        // コンソール出力
        Console.WriteLine(logMessage);
        
        // ファイル出力
        try
        {
            File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
        }
        catch
        {
            // ファイル出力失敗は無視
        }
    }
    
    /// <summary>
    /// 重要な情報をMessageBoxで表示
    /// </summary>
    public static void ShowMessage(string title, string message)
    {
        try
        {
            // Windows環境での簡易MessageBox
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c echo {message} & pause",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
                CreateNoWindow = false
            });
        }
        catch
        {
            // MessageBox失敗時はファイルログのみ
            Log($"MESSAGE: {title} - {message}");
        }
    }
}
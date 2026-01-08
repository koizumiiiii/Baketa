namespace Baketa.UI.Helpers;

/// <summary>
/// [Issue #256] バイト数フォーマットユーティリティ
/// DRY原則: 複数箇所で使用されるバイト数フォーマット処理を一元化
/// </summary>
public static class ByteFormatter
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// バイト数を人間が読みやすい形式に変換
    /// </summary>
    /// <param name="bytes">バイト数</param>
    /// <returns>フォーマットされた文字列（例: "1.5 GB"）</returns>
    public static string Format(long bytes) => Format((double)bytes);

    /// <summary>
    /// バイト数を人間が読みやすい形式に変換（double版）
    /// </summary>
    /// <param name="bytes">バイト数</param>
    /// <returns>フォーマットされた文字列（例: "1.5 GB"）</returns>
    public static string Format(double bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        int unitIndex = 0;

        while (bytes >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            unitIndex++;
            bytes /= 1024;
        }

        return $"{bytes:0.##} {SizeUnits[unitIndex]}";
    }
}

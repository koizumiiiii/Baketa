namespace Baketa.Core.Models.Hardware;

/// <summary>
/// [Issue #335] ハードウェア要件の定義
/// </summary>
public static class HardwareRequirements
{
    /// <summary>
    /// 最低要件
    /// </summary>
    public static class Minimum
    {
        public const int CpuCores = 4;
        public const int RamGb = 8;
        public const int VramMb = 4096;  // 4GB
        public const int StorageGb = 10;
    }

    /// <summary>
    /// 推奨要件
    /// </summary>
    public static class Recommended
    {
        public const int CpuCores = 6;
        public const int RamGb = 16;
        public const int VramMb = 6144;  // 6GB
        public const int StorageGb = 20;
    }

    /// <summary>
    /// 致命的（起動ブロック）要件
    /// </summary>
    public static class Critical
    {
        public const int MinRamGb = 4;
        public const int MinVramMb = 2048;  // 2GB
    }
}

/// <summary>
/// [Issue #335] ハードウェアチェック結果
/// </summary>
public record HardwareCheckResult
{
    /// <summary>
    /// CPU論理コア数
    /// </summary>
    public required int CpuCores { get; init; }

    /// <summary>
    /// 搭載RAM（GB）
    /// </summary>
    public required int TotalRamGb { get; init; }

    /// <summary>
    /// GPU名
    /// </summary>
    public required string GpuName { get; init; }

    /// <summary>
    /// VRAM（MB）
    /// </summary>
    public required int VramMb { get; init; }

    /// <summary>
    /// 最低要件を満たしているか
    /// </summary>
    public required bool MeetsMinimum { get; init; }

    /// <summary>
    /// 推奨要件を満たしているか
    /// </summary>
    public required bool MeetsRecommended { get; init; }

    /// <summary>
    /// 警告レベル
    /// </summary>
    public required HardwareWarningLevel WarningLevel { get; init; }

    /// <summary>
    /// 警告メッセージリスト
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// [Issue #335] ハードウェア警告レベル
/// </summary>
public enum HardwareWarningLevel
{
    /// <summary>
    /// 問題なし（推奨要件を満たす）
    /// </summary>
    Ok,

    /// <summary>
    /// 情報（推奨には満たないが最低要件は満たす）
    /// </summary>
    Info,

    /// <summary>
    /// 警告（最低要件を満たすが動作が不安定になる可能性）
    /// </summary>
    Warning,

    /// <summary>
    /// 致命的（起動を推奨しない）
    /// </summary>
    Critical
}

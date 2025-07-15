using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services;

/// <summary>
/// Semver準拠のバージョン比較サービス
/// 堅牢なバージョン文字列解析と比較機能を提供
/// </summary>
public sealed partial class VersionComparisonService(ILogger<VersionComparisonService> logger)
{
    private readonly ILogger<VersionComparisonService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Semver正規表現パターン（C# 12生成正規表現）
    [GeneratedRegex(@"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$")]
    private static partial Regex SemverRegex();

    // バージョンプレフィックス除去（v1.0.0 → 1.0.0）
    [GeneratedRegex(@"^v?(.+)$")]
    private static partial Regex VersionPrefixRegex();

    /// <summary>
    /// バージョン文字列をSemverとして解析
    /// </summary>
    /// <param name="versionString">バージョン文字列</param>
    /// <returns>解析されたバージョン情報</returns>
    /// <exception cref="ArgumentException">無効なバージョン文字列</exception>
    public SemverVersion ParseVersion(string versionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionString);

        // プレフィックス除去（v1.0.0 → 1.0.0）
        var cleanVersion = VersionPrefixRegex().Match(versionString).Groups[1].Value;
        
        var match = SemverRegex().Match(cleanVersion);
        if (!match.Success)
        {
            _logger.LogWarning("無効なSemverバージョン文字列: {Version}", versionString);
            throw new ArgumentException($"無効なSemverバージョン文字列: {versionString}", nameof(versionString));
        }

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        var patch = int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture);
        var prerelease = match.Groups["prerelease"].Value;
        var buildMetadata = match.Groups["buildmetadata"].Value;

        var version = new SemverVersion
        {
            Major = major,
            Minor = minor,
            Patch = patch,
            Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease,
            BuildMetadata = string.IsNullOrEmpty(buildMetadata) ? null : buildMetadata,
            OriginalString = versionString
        };

        _logger.LogDebug("バージョン解析完了: {Original} → {Parsed}", versionString, version);
        return version;
    }

    /// <summary>
    /// System.Versionからの変換
    /// </summary>
    /// <param name="version">System.Versionオブジェクト</param>
    /// <returns>SemverVersionオブジェクト</returns>
    public SemverVersion FromSystemVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SemverVersion
        {
            Major = version.Major,
            Minor = version.Minor,
            Patch = version.Build >= 0 ? version.Build : 0,
            Prerelease = null,
            BuildMetadata = version.Revision > 0 ? version.Revision.ToString(CultureInfo.InvariantCulture) : null,
            OriginalString = version.ToString()
        };
    }

    /// <summary>
    /// 2つのバージョンを比較
    /// </summary>
    /// <param name="version1">比較元バージョン</param>
    /// <param name="version2">比較先バージョン</param>
    /// <returns>比較結果（-1: version1 < version2, 0: 等しい, 1: version1 > version2）</returns>
    public int CompareVersions(SemverVersion version1, SemverVersion version2)
    {
        ArgumentNullException.ThrowIfNull(version1);
        ArgumentNullException.ThrowIfNull(version2);

        // メジャー・マイナー・パッチバージョン比較
        var coreComparison = CompareCore(version1, version2);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        // プレリリース比較
        return ComparePrereleases(version1.Prerelease, version2.Prerelease);
    }

    /// <summary>
    /// バージョン文字列の比較
    /// </summary>
    /// <param name="version1String">比較元バージョン文字列</param>
    /// <param name="version2String">比較先バージョン文字列</param>
    /// <returns>比較結果</returns>
    public int CompareVersionStrings(string version1String, string version2String)
    {
        var version1 = ParseVersion(version1String);
        var version2 = ParseVersion(version2String);
        return CompareVersions(version1, version2);
    }

    /// <summary>
    /// version1がversion2より新しいかを判定
    /// </summary>
    /// <param name="version1">比較元バージョン</param>
    /// <param name="version2">比較先バージョン</param>
    /// <returns>version1が新しい場合true</returns>
    public bool IsNewer(SemverVersion version1, SemverVersion version2)
    {
        return CompareVersions(version1, version2) > 0;
    }

    /// <summary>
    /// バージョン文字列での新旧判定
    /// </summary>
    /// <param name="version1String">比較元バージョン文字列</param>
    /// <param name="version2String">比較先バージョン文字列</param>
    /// <returns>version1が新しい場合true</returns>
    public bool IsNewer(string version1String, string version2String)
    {
        return CompareVersionStrings(version1String, version2String) > 0;
    }

    /// <summary>
    /// バージョンが有効なSemverかを検証
    /// </summary>
    /// <param name="versionString">検証対象バージョン文字列</param>
    /// <returns>有効な場合true</returns>
    public bool IsValidSemver(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
            return false;

        try
        {
            ParseVersion(versionString);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// メジャー・マイナー・パッチの比較
    /// </summary>
    private static int CompareCore(SemverVersion version1, SemverVersion version2)
    {
        if (version1.Major != version2.Major)
            return version1.Major.CompareTo(version2.Major);

        if (version1.Minor != version2.Minor)
            return version1.Minor.CompareTo(version2.Minor);

        return version1.Patch.CompareTo(version2.Patch);
    }

    /// <summary>
    /// プレリリース版の比較
    /// </summary>
    private static int ComparePrereleases(string? prerelease1, string? prerelease2)
    {
        // プレリリースなし > プレリリースあり
        if (prerelease1 == null && prerelease2 == null) return 0;
        if (prerelease1 == null) return 1;
        if (prerelease2 == null) return -1;

        // プレリリース同士の比較
        var parts1 = prerelease1.Split('.');
        var parts2 = prerelease2.Split('.');

        var minLength = Math.Min(parts1.Length, parts2.Length);
        for (var i = 0; i < minLength; i++)
        {
            var comparison = ComparePrereleaseIdentifiers(parts1[i], parts2[i]);
            if (comparison != 0)
                return comparison;
        }

        // 短い方が小さい
        return parts1.Length.CompareTo(parts2.Length);
    }

    /// <summary>
    /// プレリリース識別子の比較
    /// </summary>
    private static int ComparePrereleaseIdentifiers(string identifier1, string identifier2)
    {
        var isNumeric1 = int.TryParse(identifier1, out var num1);
        var isNumeric2 = int.TryParse(identifier2, out var num2);

        if (isNumeric1 && isNumeric2)
            return num1.CompareTo(num2);

        if (isNumeric1) return -1; // 数値 < 文字列
        if (isNumeric2) return 1;  // 文字列 > 数値

        return string.CompareOrdinal(identifier1, identifier2);
    }
}

/// <summary>
/// Semverバージョン情報
/// </summary>
public sealed record SemverVersion
{
    /// <summary>
    /// メジャーバージョン
    /// </summary>
    public required int Major { get; init; }

    /// <summary>
    /// マイナーバージョン
    /// </summary>
    public required int Minor { get; init; }

    /// <summary>
    /// パッチバージョン
    /// </summary>
    public required int Patch { get; init; }

    /// <summary>
    /// プレリリース識別子
    /// </summary>
    public string? Prerelease { get; init; }

    /// <summary>
    /// ビルドメタデータ
    /// </summary>
    public string? BuildMetadata { get; init; }

    /// <summary>
    /// 元の文字列
    /// </summary>
    public required string OriginalString { get; init; }

    /// <summary>
    /// プレリリース版かどうか
    /// </summary>
    public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);

    /// <summary>
    /// 安定版かどうか
    /// </summary>
    public bool IsStable => !IsPrerelease;

    /// <summary>
    /// System.Versionへの変換
    /// </summary>
    /// <returns>System.Versionオブジェクト</returns>
    public Version ToSystemVersion()
    {
        return new Version(Major, Minor, Patch);
    }

    /// <summary>
    /// 正規化されたバージョン文字列を取得
    /// </summary>
    /// <returns>正規化文字列</returns>
    public string ToNormalizedString()
    {
        var baseVersion = $"{Major}.{Minor}.{Patch}";
        
        if (!string.IsNullOrEmpty(Prerelease))
            baseVersion += $"-{Prerelease}";
            
        if (!string.IsNullOrEmpty(BuildMetadata))
            baseVersion += $"+{BuildMetadata}";
            
        return baseVersion;
    }

    public override string ToString() => ToNormalizedString();
}
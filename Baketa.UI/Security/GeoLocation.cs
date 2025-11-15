using System;

namespace Baketa.UI.Security;

/// <summary>
/// 地理的位置情報
/// </summary>
/// <param name="Latitude">緯度</param>
/// <param name="Longitude">経度</param>
/// <param name="City">都市名（オプション）</param>
/// <param name="Country">国名（オプション）</param>
/// <param name="Region">地域名（オプション）</param>
/// <param name="Accuracy">位置精度（メートル、オプション）</param>
public sealed record GeoLocation(
    double Latitude,
    double Longitude,
    string? City = null,
    string? Country = null,
    string? Region = null,
    double? Accuracy = null)
{
    /// <summary>
    /// 位置情報の文字列表現を取得
    /// </summary>
    /// <returns>位置情報の文字列</returns>
    public override string ToString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(City))
            parts.Add(City);
        if (!string.IsNullOrWhiteSpace(Region))
            parts.Add(Region);
        if (!string.IsNullOrWhiteSpace(Country))
            parts.Add(Country);

        var locationString = parts.Count > 0 ? string.Join(", ", parts) : "Unknown";

        return $"{locationString} ({Latitude:F4}, {Longitude:F4})";
    }

    /// <summary>
    /// 位置情報が有効かどうかを判定
    /// </summary>
    public bool IsValid =>
        Latitude >= -90 && Latitude <= 90 &&
        Longitude >= -180 && Longitude <= 180;

    /// <summary>
    /// IPアドレスから地理的位置を推定（簡易版）
    /// </summary>
    /// <param name="ipAddress">IPアドレス</param>
    /// <returns>推定された地理的位置、推定できない場合はnull</returns>
    public static GeoLocation? FromIPAddress(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress == "Unknown")
            return null;

        // 簡易的な実装（実際の実装では地理的IPデータベースを使用）
        // プライベートIPアドレスの場合
        if (IsPrivateIP(ipAddress))
        {
            return new GeoLocation(
                Latitude: 0.0,
                Longitude: 0.0,
                City: "Private Network",
                Country: "Local",
                Region: "LAN");
        }

        // TODO: 実際の実装では以下のような外部サービスを使用
        // - MaxMind GeoIP2
        // - IP2Location
        // - IPStack API
        // - GeoJS API

        // 現在は null を返す（位置情報なし）
        return null;
    }

    /// <summary>
    /// プライベートIPアドレスかどうかを判定
    /// </summary>
    /// <param name="ipAddress">IPアドレス</param>
    /// <returns>プライベートIPの場合true</returns>
    private static bool IsPrivateIP(string ipAddress)
    {
        var privateRanges = new[]
        {
            "10.",
            "172.16.", "172.17.", "172.18.", "172.19.", "172.20.", "172.21.", "172.22.", "172.23.",
            "172.24.", "172.25.", "172.26.", "172.27.", "172.28.", "172.29.", "172.30.", "172.31.",
            "192.168.",
            "127.",
            "169.254."
        };

        return privateRanges.Any(range => ipAddress.StartsWith(range, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ブラウザのGeolocation APIから地理的位置を作成
    /// </summary>
    /// <param name="latitude">緯度</param>
    /// <param name="longitude">経度</param>
    /// <param name="accuracy">精度（メートル）</param>
    /// <returns>地理的位置情報</returns>
    public static GeoLocation FromBrowserGeolocation(double latitude, double longitude, double? accuracy = null)
    {
        return new GeoLocation(latitude, longitude, Accuracy: accuracy);
    }

    /// <summary>
    /// 2つの位置が実質的に同じかどうかを判定
    /// </summary>
    /// <param name="other">比較対象の位置</param>
    /// <param name="toleranceKm">許容距離（キロメートル）</param>
    /// <returns>同じ位置とみなせる場合true</returns>
    public bool IsSameLocation(GeoLocation other, double toleranceKm = 50.0)
    {
        if (other == null)
            return false;

        const double EarthRadiusKm = 6371.0;

        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distance = EarthRadiusKm * c;

        return distance <= toleranceKm;
    }

    /// <summary>
    /// 度をラジアンに変換
    /// </summary>
    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}

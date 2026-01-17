namespace Baketa.Core.Abstractions.License;

/// <summary>
/// [Issue #297] ライセンス情報の読み取り専用プロバイダー
/// 循環依存を回避するため、ILicenseManagerから必要最小限の情報のみを公開
/// </summary>
/// <remarks>
/// UsageAnalyticsServiceなど、セッション情報のみを必要とするサービスが
/// ILicenseManagerに依存せずに認証情報を取得できるようにするためのインターフェース。
/// </remarks>
public interface ILicenseInfoProvider
{
    /// <summary>
    /// 現在のセッションID（API認証用）
    /// </summary>
    /// <remarks>
    /// ログイン中はセッショントークンを返し、未ログイン時はnullを返す。
    /// </remarks>
    string? CurrentSessionId { get; }
}

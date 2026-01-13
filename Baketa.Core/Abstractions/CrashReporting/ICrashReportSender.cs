namespace Baketa.Core.Abstractions.CrashReporting;

/// <summary>
/// [Issue #287] クラッシュレポート送信インターフェース
/// HTTP送信ロジックをInfrastructure層に分離するため追加
/// </summary>
public interface ICrashReportSender
{
    /// <summary>
    /// クラッシュレポートをRelay Serverに送信
    /// </summary>
    /// <param name="report">送信するクラッシュレポート</param>
    /// <param name="includeSystemInfo">システム情報を含めるか</param>
    /// <param name="includeLogs">ログを含めるか</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>送信成功の場合true</returns>
    Task<bool> SendAsync(
        CrashReport report,
        bool includeSystemInfo,
        bool includeLogs,
        CancellationToken cancellationToken = default);
}

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #269] 使用統計収集サービスインターフェース
/// プライバシーポリシーに記載のデータ収集を実行
/// </summary>
public interface IUsageAnalyticsService
{
    /// <summary>
    /// 使用統計イベントを記録
    /// バッファに蓄積し、条件を満たしたらバッチ送信
    /// </summary>
    /// <param name="eventType">イベント種別（session_start, translation, etc.）</param>
    /// <param name="eventData">イベントデータ（オプション）</param>
    void TrackEvent(string eventType, Dictionary<string, object>? eventData = null);

    /// <summary>
    /// バッファ内のイベントを即座に送信
    /// アプリ終了時に呼び出す
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>送信成功した場合はtrue</returns>
    Task<bool> FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のセッションID
    /// アプリ起動時に生成、終了まで維持
    /// </summary>
    Guid SessionId { get; }

    /// <summary>
    /// 統計収集が有効かどうか
    /// PrivacyConsentService.HasUsageStatisticsConsent に基づく
    /// </summary>
    bool IsEnabled { get; }
}

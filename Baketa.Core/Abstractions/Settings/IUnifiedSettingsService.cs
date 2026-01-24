using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Settings;

/// <summary>
/// 統一設定管理サービスのインターフェース
/// アプリケーション設定とユーザー設定を統合的に管理し、設定変更の監視とリアルタイム更新を提供
/// </summary>
public interface IUnifiedSettingsService
{
    /// <summary>
    /// 設定が変更されたときに発生するイベント
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <summary>
    /// 翻訳設定を取得します
    /// </summary>
    /// <returns>現在有効な翻訳設定</returns>
    ITranslationSettings GetTranslationSettings();

    /// <summary>
    /// OCR設定を取得します
    /// </summary>
    /// <returns>現在有効なOCR設定</returns>
    IOcrSettings GetOcrSettings();

    /// <summary>
    /// アプリケーション設定を取得します
    /// </summary>
    /// <returns>現在有効なアプリケーション設定</returns>
    IAppSettings GetAppSettings();

    /// <summary>
    /// 翻訳設定を更新します
    /// </summary>
    /// <param name="settings">新しい翻訳設定</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task UpdateTranslationSettingsAsync(ITranslationSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// OCR設定を更新します
    /// </summary>
    /// <param name="settings">新しいOCR設定</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task UpdateOcrSettingsAsync(IOcrSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// [Issue #237] プロモーション設定を取得します
    /// </summary>
    /// <returns>現在有効なプロモーション設定</returns>
    IPromotionSettings GetPromotionSettings();

    /// <summary>
    /// [Issue #237] プロモーション設定を更新します
    /// </summary>
    /// <param name="settings">新しいプロモーション設定</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task UpdatePromotionSettingsAsync(IPromotionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// [Issue #258] モックモードのトークン使用量を永続化します
    /// </summary>
    /// <param name="tokenUsage">トークン使用量</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task UpdateMockTokenUsageAsync(long tokenUsage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 設定をファイルからリロードします
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ReloadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 設定ファイルの変更を監視開始します
    /// </summary>
    void StartWatching();

    /// <summary>
    /// 設定ファイルの変更監視を停止します
    /// </summary>
    void StopWatching();
}

/// <summary>
/// 設定変更イベント引数
/// </summary>
public sealed class SettingsChangedEventArgs(string sectionName, SettingsType settingsType) : EventArgs
{
    /// <summary>
    /// 変更されたセクション名
    /// </summary>
    public string SectionName { get; } = sectionName;

    /// <summary>
    /// 変更された設定の種類
    /// </summary>
    public SettingsType SettingsType { get; } = settingsType;
}

/// <summary>
/// 設定の種類
/// </summary>
public enum SettingsType
{
    Translation,
    Ocr,
    Application,
    User,
    /// <summary>
    /// [Issue #237] プロモーション設定
    /// </summary>
    Promotion
}

/// <summary>
/// 翻訳設定インターフェース（読み取り専用）
/// </summary>
public interface ITranslationSettings
{
    bool AutoDetectSourceLanguage { get; }
    string DefaultSourceLanguage { get; }
    string DefaultTargetLanguage { get; }
    string DefaultEngine { get; }
    bool UseLocalEngine { get; }
    double ConfidenceThreshold { get; }
    int TimeoutMs { get; }
    int OverlayFontSize { get; }

    /// <summary>
    /// [Issue #78 Phase 5] Cloud AI翻訳の有効化フラグ
    /// Pro/Premiaプランでのみ利用可能
    /// </summary>
    bool EnableCloudAiTranslation { get; }
}

/// <summary>
/// OCR設定インターフェース（読み取り専用）
/// </summary>
public interface IOcrSettings
{
    string DefaultLanguage { get; }
    double ConfidenceThreshold { get; }
    int TimeoutMs { get; }
    bool EnablePreprocessing { get; }

    // [Issue #229] ボーダーライン信頼度緩和設定
    bool EnableBorderlineConfidenceRelaxation { get; }
    double BorderlineMinConfidence { get; }
    double BorderlineRelaxedThreshold { get; }
    int BorderlineMinTextLength { get; }
    int BorderlineMinBoundsHeight { get; }
    double BorderlineMinAspectRatio { get; }

    // [Issue #293] ROI学習済み領域の信頼度緩和設定
    bool EnableRoiConfidenceRelaxation { get; }
    double RoiConfidenceThreshold { get; }
}

/// <summary>
/// アプリケーション設定インターフェース（読み取り専用）
/// </summary>
public interface IAppSettings
{
    ITranslationSettings Translation { get; }
    IOcrSettings Ocr { get; }
    string LogLevel { get; }
    bool EnableDebugMode { get; }
}

/// <summary>
/// [Issue #237] プロモーション設定インターフェース（読み取り専用）
/// プロモーションコードの適用状態と有効期限を管理
/// </summary>
public interface IPromotionSettings
{
    /// <summary>
    /// 適用済みプロモーションコード（暗号化済み）
    /// </summary>
    string? AppliedPromotionCode { get; }

    /// <summary>
    /// プロモーションで適用されたプラン（int型：0=Free, 1=Standard, 2=Pro, 3=Premia）
    /// </summary>
    int? PromotionPlanType { get; }

    /// <summary>
    /// プロモーション有効期限（ISO 8601形式）
    /// </summary>
    string? PromotionExpiresAt { get; }

    /// <summary>
    /// プロモーション適用日時（ISO 8601形式）
    /// </summary>
    string? PromotionAppliedAt { get; }

    /// <summary>
    /// 最終オンライン検証日時（時計巻き戻し対策用）
    /// </summary>
    string? LastOnlineVerification { get; }

    /// <summary>
    /// プロモーションが有効かどうか
    /// </summary>
    bool IsPromotionActive { get; }

    /// <summary>
    /// [Issue #258] モックモード時のトークン使用量
    /// 開発/テスト用の永続化（本番では使用しない）
    /// </summary>
    long MockTokenUsage { get; }
}

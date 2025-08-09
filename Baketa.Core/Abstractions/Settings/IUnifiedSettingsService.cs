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
public sealed class SettingsChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更されたセクション名
    /// </summary>
    public string SectionName { get; }

    /// <summary>
    /// 変更された設定の種類
    /// </summary>
    public SettingsType SettingsType { get; }

    public SettingsChangedEventArgs(string sectionName, SettingsType settingsType)
    {
        SectionName = sectionName;
        SettingsType = settingsType;
    }
}

/// <summary>
/// 設定の種類
/// </summary>
public enum SettingsType
{
    Translation,
    Ocr,
    Application,
    User
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
using Baketa.Core.Models.Translation;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// 言語設定管理サービス
/// UI設定を単一ソースとした統一言語設定管理を提供
/// Clean Architecture準拠でテスト可能な抽象化
/// </summary>
public interface ILanguageConfigurationService
{
    /// <summary>
    /// 現在の言語ペアを取得（同期）
    /// キャッシュされた値を即座に返す
    /// </summary>
    /// <returns>現在の言語ペア</returns>
    LanguagePair GetCurrentLanguagePair();

    /// <summary>
    /// 現在の言語ペアを取得（非同期）
    /// I/O操作が必要な場合に使用
    /// </summary>
    /// <returns>現在の言語ペア</returns>
    Task<LanguagePair> GetLanguagePairAsync();

    /// <summary>
    /// 自動ソース言語検出が有効かどうか
    /// </summary>
    bool IsAutoDetectionEnabled { get; }

    /// <summary>
    /// 言語ペアを永続化ストレージに更新
    /// UI設定とキャッシュを同期更新
    /// </summary>
    /// <param name="pair">新しい言語ペア</param>
    /// <returns>更新タスク</returns>
    Task UpdateLanguagePairAsync(LanguagePair pair);

    /// <summary>
    /// 言語設定変更イベント
    /// 言語ペアが変更された際に発火
    /// </summary>
    event EventHandler<LanguagePair>? LanguagePairChanged;

    /// <summary>
    /// ソース言語コードを取得（レガシー互換）
    /// 既存コードとの互換性のため提供
    /// </summary>
    string GetSourceLanguageCode();

    /// <summary>
    /// ターゲット言語コードを取得（レガシー互換）
    /// 既存コードとの互換性のため提供
    /// </summary>
    string GetTargetLanguageCode();
}
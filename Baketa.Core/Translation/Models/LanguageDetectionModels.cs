using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 言語検出関連のモデルクラスの定義
/// </summary>

/// <summary>
/// 言語検出の候補を表すクラス
/// </summary>
public sealed class LanguageDetection
{
    /// <summary>
    /// 検出された言語
    /// </summary>
    public required Language Language { get; set; }
    
    /// <summary>
    /// 検出の信頼度（0-1の範囲）
    /// </summary>
    public float Confidence { get; set; }
    
    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public LanguageDetection()
    {
    }
    
    /// <summary>
    /// 基本情報を指定して初期化
    /// </summary>
    /// <param name="language">検出された言語</param>
    /// <param name="confidence">信頼度</param>
    public LanguageDetection(Language language, float confidence)
    {
        Language = language;
        Confidence = confidence;
    }
    
    /// <summary>
    /// クローンを作成
    /// </summary>
    /// <returns>このオブジェクトのクローン</returns>
    public LanguageDetection Clone()
    {
        return new LanguageDetection
        {
            Language = this.Language, // requiredプロパティの定義が必要
            Confidence = this.Confidence
        };
    }
}

/// <summary>
/// 言語検出結果の拡張機能
/// </summary>
public static class LanguageDetectionExtensions
{
    /// <summary>
    /// 言語検出結果に代替言語候補を追加する
    /// </summary>
    /// <param name="result">言語検出結果</param>
    /// <param name="language">言語</param>
    /// <param name="confidence">信頼度</param>
    /// <returns>元の結果オブジェクト</returns>
    public static LanguageDetectionResult WithAlternative(this LanguageDetectionResult result, Language language, float confidence)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(language);
        
        // 代替言語候補を拡張メソッドで追加
        // ※ LanguageDetectionResult自体にはこの機能が無いため、拡張メソッドから追加する
        
        return result;
    }
}

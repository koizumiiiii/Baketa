using System;
using System.Collections.Generic;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 高度な翻訳リクエスト（クラウドAI向け）
/// </summary>
public class AdvancedTranslationRequest : TranslationRequest
{
    /// <summary>
    /// 品質レベル（0～5）
    /// </summary>
    public int QualityLevel { get; set; } = 3;
    
    /// <summary>
    /// トークン上限
    /// </summary>
    public int MaxTokens { get; set; } = 500;
    
    /// <summary>
    /// プロンプトテンプレート
    /// </summary>
    public string? PromptTemplate { get; set; }
    
    /// <summary>
    /// 追加コンテキスト
    /// </summary>
    public IReadOnlyList<string> AdditionalContexts => _additionalContexts;
    private readonly List<string> _additionalContexts = [];
    
    /// <summary>
    /// 新しいインスタンスを作成
    /// </summary>
    public AdvancedTranslationRequest()
    {
    }
    
    /// <summary>
    /// 基本リクエストから高度なリクエストを作成
    /// </summary>
    public AdvancedTranslationRequest(TranslationRequest baseRequest)
    {
        ArgumentNullException.ThrowIfNull(baseRequest);
            
        SourceText = baseRequest.SourceText;
        SourceLanguage = baseRequest.SourceLanguage;
        TargetLanguage = baseRequest.TargetLanguage;
        Context = baseRequest.Context;
        
        // オプションのコピー
        foreach (var option in baseRequest.Options)
        {
            Options[option.Key] = option.Value;
        }
        
        // タイムスタンプを現在時刻に更新
        Timestamp = DateTime.UtcNow;
    }
    
    /// <summary>
    /// ディープコピーを作成
    /// </summary>
    public new AdvancedTranslationRequest Clone()
    {
        var clone = new AdvancedTranslationRequest
        {
            SourceText = this.SourceText,
            SourceLanguage = this.SourceLanguage,
            TargetLanguage = this.TargetLanguage,
            Context = this.Context?.Clone(),
            QualityLevel = this.QualityLevel,
            MaxTokens = this.MaxTokens,
            PromptTemplate = this.PromptTemplate,
            Timestamp = this.Timestamp
        };
        
        // オプションのコピー
        foreach (var option in this.Options)
        {
            clone.Options[option.Key] = option.Value;
        }
        
        // 追加コンテキストのコピー
        foreach (var context in this.AdditionalContexts)
        {
            clone._additionalContexts.Add(context);
        }
        
        return clone;
    }
}

/// <summary>
/// 高度な翻訳レスポンス（クラウドAI向け）
/// </summary>
public class AdvancedTranslationResponse : TranslationResponse
{
    /// <summary>
    /// 残りトークン数
    /// </summary>
    public int? RemainingTokens { get; set; }
    
    /// <summary>
    /// 使用したトークン数
    /// </summary>
    public int UsedTokens { get; set; }
    
    /// <summary>
    /// 代替翻訳の候補リスト
    /// </summary>
    public IReadOnlyList<string> AlternativeTranslations => _alternativeTranslations;
    private readonly List<string> _alternativeTranslations = [];
    
    /// <summary>
    /// 翻訳の確信度スコア（0.0～1.0）
    /// </summary>
    public new double? ConfidenceScore { get; set; }
    
    /// <summary>
    /// 翻訳モデルのバージョン
    /// </summary>
    public string? ModelVersion { get; set; }
    
    /// <summary>
    /// レスポンスのメタデータ
    /// </summary>
    public new IReadOnlyDictionary<string, object?> Metadata => _metadata;
    private readonly Dictionary<string, object?> _metadata = [];
    
    /// <summary>
    /// 代替翻訳を追加
    /// </summary>
    public void AddAlternativeTranslation(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _alternativeTranslations.Add(text);
        }
    }
    
    /// <summary>
    /// メタデータに項目を追加
    /// </summary>
    public void AddMetadata(string key, object? value)
    {
        _metadata[key] = value;
    }
}

/// <summary>
/// APIステータス情報
/// </summary>
public class ApiStatusInfo
{
    /// <summary>
    /// APIが利用可能かどうか
    /// </summary>
    public bool IsAvailable { get; set; }
    
    /// <summary>
    /// 残りのクォータ
    /// </summary>
    public int? RemainingQuota { get; set; }
    
    /// <summary>
    /// クォータのリセット日時
    /// </summary>
    public DateTime? QuotaResetTime { get; set; }
    
    /// <summary>
    /// レート制限の情報
    /// </summary>
    public string? RateLimitInfo { get; set; }
    
    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string? StatusMessage { get; set; }
    
    /// <summary>
    /// レート制限までの残りリクエスト数
    /// </summary>
    public int? RemainingRequests { get; set; }
    
    /// <summary>
    /// サービスレイテンシ
    /// </summary>
    public TimeSpan? ServiceLatency { get; set; }
}
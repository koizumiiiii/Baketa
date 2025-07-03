using System;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.Chinese;

/// <summary>
/// 中国語変種検出サービス
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="processor">中国語言語処理プロセッサ</param>
/// <param name="logger">ロガー</param>
/// <exception cref="ArgumentNullException">引数がnullの場合</exception>
public class ChineseVariantDetectionService(
    ChineseLanguageProcessor processor,
    ILogger<ChineseVariantDetectionService> logger)
{
    private readonly ChineseLanguageProcessor _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    private readonly ILogger<ChineseVariantDetectionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// テキストから中国語変種を検出
    /// </summary>
    /// <param name="text">検出対象のテキスト</param>
    /// <returns>検出された中国語変種</returns>
    public ChineseVariant DetectVariant(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("空のテキストが提供されました。Autoを返します。");
            return ChineseVariant.Auto;
        }

        try
        {
            var scriptType = _processor.DetectScriptType(text);
            var result = scriptType switch
            {
                ChineseScriptType.Simplified => ChineseVariant.Simplified,
                ChineseScriptType.Traditional => ChineseVariant.Traditional,
                ChineseScriptType.Mixed => ChineseVariant.Auto,
                ChineseScriptType.Unknown => ChineseVariant.Auto,
                _ => ChineseVariant.Auto
            };

            _logger.LogDebug("テキスト '{Text}' の変種検出結果: {ScriptType} -> {Variant}", 
                text.Length > 50 ? text[..50] + "..." : text, scriptType, result);

            return result;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "中国語変種検出中に無効な操作エラーが発生しました。Autoを返します。");
            return ChineseVariant.Auto;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "中国語変種検出中に引数エラーが発生しました。Autoを返します。");
            return ChineseVariant.Auto;
        }
#pragma warning disable CA1031 // 変種検出では全ての例外をキャッチして継続する必要がある
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "中国語変種検出中に予期しないエラーが発生しました。Autoを返します。");
            return ChineseVariant.Auto;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 言語コードから中国語変種を検出
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>検出された中国語変種</returns>
    public ChineseVariant DetectVariantFromLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return ChineseVariant.Auto;
        }

        var normalizedCode = languageCode.Trim().ToUpperInvariant();
        var result = normalizedCode switch
        {
            "ZH-CN" or "ZH-HANS" or "ZH-CHS" or "CMN_HANS" => ChineseVariant.Simplified,
            "ZH-TW" or "ZH-HK" or "ZH-MO" or "ZH-HANT" or "ZH-CHT" or "CMN_HANT" => ChineseVariant.Traditional,
            "YUE" or "YUE-HK" or "YUE-CN" => ChineseVariant.Cantonese,
            "ZH" or "ZHO" or "CMN" => ChineseVariant.Auto,
            _ => ChineseVariant.Auto
        };

        _logger.LogDebug("言語コード '{LanguageCode}' から変種検出: {Variant}", languageCode, result);
        return result;
    }

    /// <summary>
    /// 言語オブジェクトから中国語変種を検出
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>検出された中国語変種</returns>
    public ChineseVariant DetectVariantFromLanguage(Language language)
    {
        if (language == null)
        {
            return ChineseVariant.Auto;
        }

        // 地域コードがある場合はそれを含めて検出
        if (!string.IsNullOrWhiteSpace(language.RegionCode))
        {
            var fullCode = $"{language.Code}-{language.RegionCode}";
            var variantWithRegion = DetectVariantFromLanguageCode(fullCode);
            if (variantWithRegion != ChineseVariant.Auto)
            {
                return variantWithRegion;
            }
        }

        return DetectVariantFromLanguageCode(language.Code);
    }

    /// <summary>
    /// 指定された言語が中国語かどうかを判定
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語の場合はtrue</returns>
    public bool IsChineseLanguage(string languageCode)
    {
        return _processor.IsChineseLanguageCode(languageCode);
    }

    /// <summary>
    /// 言語オブジェクトが中国語かどうかを判定
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>中国語の場合はtrue</returns>
    public bool IsChineseLanguage(Language language)
    {
        if (language == null)
        {
            return false;
        }

        return IsChineseLanguage(language.Code) || 
               (!string.IsNullOrWhiteSpace(language.RegionCode) && 
                IsChineseLanguage($"{language.Code}-{language.RegionCode}"));
    }

    /// <summary>
    /// 推奨される言語情報を取得
    /// </summary>
    /// <param name="text">テキスト</param>
    /// <returns>推奨される言語情報</returns>
    public LanguageInfo GetRecommendedLanguageInfo(string text)
    {
        var variant = DetectVariant(text);
        var languageCode = variant.ToLanguageCode();
        
        var langInfo = LanguageConfiguration.GetLanguageInfo(languageCode);
        if (langInfo != null)
        {
            return langInfo;
        }

        // デフォルト値を返す
        return new LanguageInfo
        {
            Code = languageCode,
            Name = variant.GetDisplayName(),
            NativeName = variant.GetNativeDisplayName(),
            OpusPrefix = variant.GetOpusPrefix(),
            Variant = variant
        };
    }
}

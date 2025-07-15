using System;
using System.Linq;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Core.Settings.Validation;

/// <summary>
/// αテスト用検証ルールの基底クラス
/// </summary>
public abstract class AlphaTestValidationRuleBase : IValidationRule
{
    /// <inheritdoc />
    public abstract string PropertyPath { get; }

    /// <inheritdoc />
    public virtual int Priority => 100;

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract SettingValidationResult Validate(object? value, ValidationContext context);

    /// <inheritdoc />
    public virtual bool CanApplyTo(string propertyPath)
    {
        return string.Equals(PropertyPath, propertyPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 検証成功結果を作成します
    /// </summary>
    protected SettingValidationResult CreateSuccess(object? value, string? warning = null)
    {
        var metadata = CreateMetadata();
        return SettingValidationResult.Success(metadata, value, warning);
    }

    /// <summary>
    /// 検証失敗結果を作成します
    /// </summary>
    protected SettingValidationResult CreateFailure(object? value, string errorMessage)
    {
        var metadata = CreateMetadata();
        return SettingValidationResult.Failure(metadata, value, errorMessage);
    }

    /// <summary>
    /// メタデータを作成します
    /// </summary>
    protected virtual SettingMetadata CreateMetadata()
    {
        var dummyProperty = typeof(DummyValidationSettings).GetProperty(nameof(DummyValidationSettings.Property))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Validation", Description);
        return new SettingMetadata(dummyProperty, attribute);
    }
}

/// <summary>
/// αテスト翻訳エンジン検証ルール
/// OPUS-MTエンジンのみ許可
/// </summary>
public sealed class AlphaTestTranslationEngineRule : AlphaTestValidationRuleBase
{
    public override string PropertyPath => "DefaultEngine";
    public override string Description => "翻訳エンジン選択（αテストではOPUS-MTのみ）";

    public override SettingValidationResult Validate(object? value, ValidationContext context)
    {
        if (value is not TranslationEngine engine)
        {
            return CreateFailure(value, "翻訳エンジンが設定されていません");
        }

        // αテストではLocalエンジン（OPUS-MT）のみ許可
        // 新仕様：Geminiも追加されるが、αテストはローカルのみ
        if (engine != TranslationEngine.Local)
        {
            return CreateFailure(value, $"αテストでは翻訳エンジンは'Local (OPUS-MT)'のみ利用可能です。現在の設定: '{engine}'");
        }

        return CreateSuccess(value);
    }
}

/// <summary>
/// αテスト言語ペア検証ルール
/// 日英・英日ペアのみ許可
/// </summary>
public sealed class AlphaTestLanguagePairRule : AlphaTestValidationRuleBase
{
    public override string PropertyPath => "DefaultSourceLanguage";
    public override string Description => "言語ペア選択（αテストでは日英・英日のみ）";

    public override SettingValidationResult Validate(object? value, ValidationContext context)
    {
        if (value is not string sourceLanguage)
        {
            return CreateFailure(value, "翻訳元言語が設定されていません");
        }

        // αテストでは日本語↔英語のみ許可
        var allowedLanguages = new[] { "Japanese", "English", "ja", "en", "jp", "日本語", "英語" };
        
        if (!allowedLanguages.Contains(sourceLanguage, StringComparer.OrdinalIgnoreCase))
        {
            return CreateFailure(value, $"αテストでは日本語↔英語の翻訳のみ利用可能です。現在の設定: '{sourceLanguage}'");
        }

        // ターゲット言語も確認
        var targetLanguage = context.Settings.Translation.DefaultTargetLanguage;
        if (!string.IsNullOrEmpty(targetLanguage))
        {
            if (!allowedLanguages.Contains(targetLanguage, StringComparer.OrdinalIgnoreCase))
            {
                return CreateFailure(value, $"αテストでは日本語↔英語の翻訳のみ利用可能です。翻訳先言語: '{targetLanguage}'");
            }

            // 同じ言語への翻訳は禁止
            if (IsSameLanguage(sourceLanguage, targetLanguage))
            {
                return CreateFailure(value, "翻訳元と翻訳先が同じ言語です");
            }
        }

        return CreateSuccess(value);
    }

    private static bool IsSameLanguage(string lang1, string lang2)
    {
        var japanese = new[] { "Japanese", "ja", "jp", "日本語" };
        var english = new[] { "English", "en", "英語" };

        var lang1IsJapanese = japanese.Contains(lang1, StringComparer.OrdinalIgnoreCase);
        var lang2IsJapanese = japanese.Contains(lang2, StringComparer.OrdinalIgnoreCase);
        var lang1IsEnglish = english.Contains(lang1, StringComparer.OrdinalIgnoreCase);
        var lang2IsEnglish = english.Contains(lang2, StringComparer.OrdinalIgnoreCase);

        return (lang1IsJapanese && lang2IsJapanese) || (lang1IsEnglish && lang2IsEnglish);
    }
}

/// <summary>
/// αテストフォントサイズ検証ルール
/// 8-48pxの範囲制限
/// </summary>
public sealed class AlphaTestFontSizeRule : AlphaTestValidationRuleBase
{
    public override string PropertyPath => "FontSize";
    public override string Description => "フォントサイズ（8-48px）";
    public override int Priority => 50; // 高優先度

    public override SettingValidationResult Validate(object? value, ValidationContext context)
    {
        if (value is not int fontSize)
        {
            return CreateFailure(value, "フォントサイズが正しく設定されていません");
        }

        const int minFontSize = 8;
        const int maxFontSize = 48;

        if (fontSize < minFontSize || fontSize > maxFontSize)
        {
            return CreateFailure(value, $"フォントサイズは{minFontSize}px以上{maxFontSize}px以下で設定してください。現在の設定: {fontSize}px");
        }

        // αテストでは全サイズ利用可能だが、推奨サイズをチェック
        if (fontSize > 36)
        {
            return CreateSuccess(value, "大きなフォントサイズは画面を多く占有します");
        }

        return CreateSuccess(value);
    }
}

/// <summary>
/// αテスト透明度検証ルール
/// 10-90%の範囲制限
/// </summary>
public sealed class AlphaTestOpacityRule : AlphaTestValidationRuleBase
{
    public override string PropertyPath => "Opacity";
    public override string Description => "オーバーレイ透明度（10-90%）";
    public override int Priority => 50; // 高優先度

    public override SettingValidationResult Validate(object? value, ValidationContext context)
    {
        double opacity;

        // 型変換処理
        switch (value)
        {
            case double d:
                opacity = d;
                break;
            case float f:
                opacity = f;
                break;
            case int i:
                opacity = i / 100.0; // パーセント値として扱う
                break;
            case decimal dec:
                opacity = (double)dec;
                break;
            default:
                return CreateFailure(value, "透明度は数値で指定してください");
        }

        // 0-1の範囲の場合はパーセント変換
        if (opacity <= 1.0)
        {
            opacity *= 100;
        }

        const double minOpacity = 10.0;
        const double maxOpacity = 90.0;

        if (opacity < minOpacity || opacity > maxOpacity)
        {
            return CreateFailure(value, $"透明度は{minOpacity}%以上{maxOpacity}%以下で設定してください。現在の設定: {opacity:F1}%");
        }

        // 極端な値の場合は警告
        if (opacity < 20.0)
        {
            return CreateSuccess(value, $"透明度{opacity:F1}%は非常に薄く、テキストが見えにくい可能性があります");
        }
        
        if (opacity > 80.0)
        {
            return CreateSuccess(value, $"透明度{opacity:F1}%は非常に濃く、背景が見えにくい可能性があります");
        }

        return CreateSuccess(value);
    }
}

/// <summary>
/// αテストキャプチャ間隔検証ルール
/// 100-5000msの範囲制限
/// </summary>
public sealed class AlphaTestCaptureIntervalRule : AlphaTestValidationRuleBase
{
    public override string PropertyPath => "CaptureIntervalMs";
    public override string Description => "キャプチャ間隔（100-5000ms）";

    public override SettingValidationResult Validate(object? value, ValidationContext context)
    {
        if (value is not int interval)
        {
            return CreateFailure(value, "キャプチャ間隔は数値（ミリ秒）で指定してください");
        }

        const int minInterval = 100;
        const int maxInterval = 5000;

        if (interval < minInterval || interval > maxInterval)
        {
            return CreateFailure(value, $"キャプチャ間隔は{minInterval}ms以上{maxInterval}ms以下で設定してください。現在の設定: {interval}ms");
        }

        // パフォーマンス警告
        if (interval < 500)
        {
            return CreateSuccess(value, $"キャプチャ間隔{interval}msは短く、CPUやGPUの使用率が高くなる可能性があります");
        }

        if (interval > 3000)
        {
            return CreateSuccess(value, $"キャプチャ間隔{interval}msは長く、テキスト変化の検出が遅れる可能性があります");
        }

        return CreateSuccess(value);
    }
}

/// <summary>
/// ダミー検証設定クラス
/// </summary>
internal sealed class DummyValidationSettings
{
    public string Property { get; set; } = string.Empty;
}
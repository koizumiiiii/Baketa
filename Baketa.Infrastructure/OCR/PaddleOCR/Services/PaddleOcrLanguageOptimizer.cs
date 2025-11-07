using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// 言語別最適化、言語判定、パラメータ調整を担当するサービス
/// Phase 2.3: PaddleOcrEngineから抽出された言語最適化実装
/// </summary>
public sealed class PaddleOcrLanguageOptimizer : IPaddleOcrLanguageOptimizer
{
    private readonly IUnifiedSettingsService _unifiedSettingsService;
    private readonly ILogger<PaddleOcrLanguageOptimizer>? _logger;

    public PaddleOcrLanguageOptimizer(
        IUnifiedSettingsService unifiedSettingsService,
        ILogger<PaddleOcrLanguageOptimizer>? logger = null)
    {
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrLanguageOptimizer初期化完了");
    }

    /// <summary>
    /// 翻訳設定とOCR設定を統合して使用言語を決定
    /// </summary>
    public string DetermineLanguageFromSettings(OcrEngineSettings settings)
    {
        try
        {
            // 1. 明示的なOCR言語設定を最優先
            if (!string.IsNullOrWhiteSpace(settings.Language) && settings.Language != "jpn")
            {
                var mappedLanguage = MapDisplayNameToLanguageCode(settings.Language);
                _logger?.LogDebug("🎯 OCR設定から言語決定: '{Language}' → '{MappedLanguage}'", settings.Language, mappedLanguage);
                return mappedLanguage;
            }

            // 2. 翻訳設定から言語を推測
            var translationSourceLanguage = GetTranslationSourceLanguageFromConfig();
            if (!string.IsNullOrWhiteSpace(translationSourceLanguage))
            {
                var mappedLanguage = MapDisplayNameToLanguageCode(translationSourceLanguage);
                _logger?.LogDebug("🌐 翻訳設定から言語決定: '{SourceLanguage}' → '{MappedLanguage}'", translationSourceLanguage, mappedLanguage);
                return mappedLanguage;
            }

            // 3. デフォルト言語（日本語）
            _logger?.LogDebug("📋 設定が見つからないため、デフォルト言語 'jpn' を使用");
            return "jpn";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ 言語決定処理でエラー");
            return "jpn"; // フォールバック
        }
    }

    /// <summary>
    /// 表示名をOCR言語コードにマッピング
    /// </summary>
    public string MapDisplayNameToLanguageCode(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "jpn";

        var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 日本語
            { "日本語", "jpn" },
            { "Japanese", "jpn" },
            { "ja", "jpn" },
            { "jpn", "jpn" },

            // 英語
            { "英語", "eng" },
            { "English", "eng" },
            { "en", "eng" },
            { "eng", "eng" },

            // 中国語（簡体字）
            { "簡体字中国語", "chi_sim" },
            { "简体中文", "chi_sim" },
            { "Chinese (Simplified)", "chi_sim" },
            { "zh-CN", "chi_sim" },
            { "zh_cn", "chi_sim" },

            // 中国語（繁体字）
            { "繁体字中国語", "chi_tra" },
            { "繁體中文", "chi_tra" },
            { "Chinese (Traditional)", "chi_tra" },
            { "zh-TW", "chi_tra" },
            { "zh_tw", "chi_tra" },

            // 韓国語
            { "韓国語", "kor" },
            { "한국어", "kor" },
            { "Korean", "kor" },
            { "ko", "kor" },
            { "kor", "kor" }
        };

        if (languageMapping.TryGetValue(displayName, out var languageCode))
        {
            return languageCode;
        }

        _logger?.LogWarning("⚠️ 未知の言語表示名 '{DisplayName}'、デフォルト 'jpn' を使用", displayName);
        return "jpn";
    }

    /// <summary>
    /// 言語別最適化適用
    /// </summary>
    public void ApplyLanguageOptimizations(PaddleOcrAll engine, string language)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var ocrType = engine.GetType();

        if (language.Equals("jpn", StringComparison.OrdinalIgnoreCase) ||
            language.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            ApplyJapaneseOptimizations(ocrType, engine);
        }
        else if (language.Equals("eng", StringComparison.OrdinalIgnoreCase) ||
                 language.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            ApplyEnglishOptimizations(ocrType, engine);
        }
        else
        {
            _logger?.LogDebug("📝 言語 '{Language}' には特別な最適化なし", language);
        }
    }

    /// <summary>
    /// 最適ゲームプロファイル選択
    /// 注意: Phase 2.3では簡易実装、詳細な画像分析は将来のフェーズで対応
    /// </summary>
    public string SelectOptimalGameProfile(ImageCharacteristics characteristics)
    {
        // Phase 2.3: 簡易実装 - AverageBrightnessのみを使用
        // 完全な実装はPhase 2.5（画像プロセッサー実装）で対応予定

        if (characteristics.AverageBrightness < 80)
        {
            _logger?.LogDebug("🌙 暗い背景プロファイル選択: Brightness={Brightness}", characteristics.AverageBrightness);
            return "darkbackground";
        }
        else if (characteristics.AverageBrightness > 140)
        {
            _logger?.LogDebug("☀️ 明るい背景プロファイル選択: Brightness={Brightness}", characteristics.AverageBrightness);
            return "lightbackground";
        }
        else
        {
            _logger?.LogDebug("📋 デフォルトプロファイル選択: Brightness={Brightness}", characteristics.AverageBrightness);
            return "default";
        }
    }

    /// <summary>
    /// 設定サービスから翻訳元言語を取得
    /// </summary>
    private string? GetTranslationSourceLanguageFromConfig()
    {
        try
        {
            _logger?.LogDebug("📁 設定サービスからSourceLanguage取得試行...");

            var translationSettings = _unifiedSettingsService.GetTranslationSettings();
            var sourceLanguage = translationSettings?.DefaultSourceLanguage;

            if (!string.IsNullOrWhiteSpace(sourceLanguage))
            {
                _logger?.LogDebug("📁 設定サービスから翻訳元言語取得: '{SourceLanguage}'", sourceLanguage);
                return sourceLanguage;
            }

            _logger?.LogDebug("📁 設定サービスからSourceLanguageが取得できませんでした");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ 設定サービス取得エラー");
            return null;
        }
    }

    /// <summary>
    /// 日本語認識に特化した最適化パラメータを適用
    /// </summary>
    private void ApplyJapaneseOptimizations(Type ocrType, object ocrEngine)
    {
        try
        {
            // 🔥 [PHASE10.26_FIX] 回転検出を無効化（横書き専用モード）
            // Angle=90°誤検出によるX座標ずれと検出失敗を完全解消
            var rotationProp = ocrType.GetProperty("AllowRotateDetection");
            if (rotationProp != null && rotationProp.CanWrite)
            {
                rotationProp.SetValue(ocrEngine, false);
                _logger?.LogDebug("✅ [PHASE10.26_FIX] 横書き専用モード: 回転検出無効化");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "❌ 日本語最適化設定エラー");
        }
    }

    /// <summary>
    /// 英語認識に特化した最適化パラメータを適用
    /// </summary>
    private void ApplyEnglishOptimizations(Type ocrType, object ocrEngine)
    {
        try
        {
            // 180度分類を有効化（英語テキストの向き対応）
            var classificationProp = ocrType.GetProperty("Enable180Classification");
            if (classificationProp != null && classificationProp.CanWrite)
            {
                classificationProp.SetValue(ocrEngine, true);
                _logger?.LogDebug("🔄 英語テキスト向き対応: 180度分類有効");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "❌ 英語最適化設定エラー");
        }
    }
}

using System;
using System.Collections.Generic;
using ReactiveUI;

namespace Baketa.UI.Models;

    /// <summary>
    /// 翻訳履歴アイテムのデータモデル
    /// </summary>
    public sealed class TranslationHistoryItem
    {
        public string Id { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 言語ペア設定のデータモデル
    /// </summary>
    public sealed class LanguagePairConfiguration : ReactiveObject
    {
        private string _sourceLanguage = string.Empty;
        private string _targetLanguage = string.Empty;
        private string _sourceLanguageDisplay = string.Empty;
        private string _targetLanguageDisplay = string.Empty;
        private string _selectedEngine = "LocalOnly";
        private TranslationStrategy _strategy = TranslationStrategy.Direct;
        private ChineseVariant _chineseVariant = ChineseVariant.Auto;
        private int _priority = 1;
        private bool _isEnabled = true;
        private bool _requiresDownload;
        private double _estimatedLatencyMs = 50.0;
        private string _description = string.Empty;
        
        /// <summary>
        /// ソース言語コード
        /// </summary>
        public string SourceLanguage
        {
            get => _sourceLanguage;
            set => this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
        }
        
        /// <summary>
        /// ターゲット言語コード
        /// </summary>
        public string TargetLanguage
        {
            get => _targetLanguage;
            set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
        }
        
        /// <summary>
        /// ソース言語表示名
        /// </summary>
        public string SourceLanguageDisplay
        {
            get => _sourceLanguageDisplay;
            set => this.RaiseAndSetIfChanged(ref _sourceLanguageDisplay, value);
        }
        
        /// <summary>
        /// ターゲット言語表示名
        /// </summary>
        public string TargetLanguageDisplay
        {
            get => _targetLanguageDisplay;
            set => this.RaiseAndSetIfChanged(ref _targetLanguageDisplay, value);
        }
        
        /// <summary>
        /// 選択されたエンジン
        /// </summary>
        public string SelectedEngine
        {
            get => _selectedEngine;
            set => this.RaiseAndSetIfChanged(ref _selectedEngine, value);
        }
        
        /// <summary>
        /// 翻訳戦略
        /// </summary>
        public TranslationStrategy Strategy
        {
            get => _strategy;
            set => this.RaiseAndSetIfChanged(ref _strategy, value);
        }
        
        /// <summary>
        /// 中国語変種（中国語関連の言語ペアのみ）
        /// </summary>
        public ChineseVariant ChineseVariant
        {
            get => _chineseVariant;
            set => this.RaiseAndSetIfChanged(ref _chineseVariant, value);
        }
        
        /// <summary>
        /// 優先順位（低い数値ほど高優先）
        /// </summary>
        public int Priority
        {
            get => _priority;
            set => this.RaiseAndSetIfChanged(ref _priority, value);
        }
        
        /// <summary>
        /// 有効化フラグ
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
        }
        
        /// <summary>
        /// ダウンロードが必要かどうか
        /// </summary>
        public bool RequiresDownload
        {
            get => _requiresDownload;
            set => this.RaiseAndSetIfChanged(ref _requiresDownload, value);
        }
        
        /// <summary>
        /// 推定レイテンシ（ミリ秒）
        /// </summary>
        public double EstimatedLatencyMs
        {
            get => _estimatedLatencyMs;
            set => this.RaiseAndSetIfChanged(ref _estimatedLatencyMs, value);
        }
        
        /// <summary>
        /// 説明
        /// </summary>
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        /// <summary>
        /// サポートされているかどうか
        /// </summary>
        public bool IsSupported
        {
            get => IsEnabled && !RequiresDownload;
        }
        
        /// <summary>
        /// 言語ペアキー（例: "ja-en"）
        /// </summary>
        public string LanguagePairKey => $"{SourceLanguage}-{TargetLanguage}";
        
        /// <summary>
        /// 表示用言語ペア名
        /// </summary>
        public string DisplayName => $"{SourceLanguageDisplay} → {TargetLanguageDisplay}";
        
        /// <summary>
        /// 中国語関連の言語ペアかどうか
        /// </summary>
        public bool IsChineseRelated => 
            (!string.IsNullOrEmpty(SourceLanguage) && SourceLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) || 
            (!string.IsNullOrEmpty(TargetLanguage) && TargetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        
        /// <summary>
        /// 2段階翻訳が利用可能かどうか
        /// </summary>
        public bool SupportsTwoStageTranslation => Strategy == TranslationStrategy.TwoStage;
        
        /// <summary>
        /// レイテンシの表示テキスト
        /// </summary>
        public string LatencyDisplayText => EstimatedLatencyMs < 1000 ? 
            $"{EstimatedLatencyMs:F0}ms" : 
            $"{EstimatedLatencyMs / 1000:F1}s";
    }

    /// <summary>
    /// 翻訳戦略の列挙型
    /// </summary>
    public enum TranslationStrategy
    {
        /// <summary>直接翻訳</summary>
        Direct,
        /// <summary>2段階翻訳</summary>
        TwoStage,
        /// <summary>ハイブリッド翻訳</summary>
        Hybrid
    }

    /// <summary>
    /// 翻訳エンジンの列挙型
    /// </summary>
    public enum TranslationEngine
    {
        /// <summary>ローカルエンジンのみ（OPUS-MT）</summary>
        LocalOnly,
        /// <summary>クラウドエンジンのみ（Gemini API等）</summary>
        CloudOnly,
        /// <summary>ハイブリッドエンジン（ローカル＋クラウド）</summary>
        Hybrid
    }

    /// <summary>
    /// 中国語変種の列挙型
    /// </summary>
    public enum ChineseVariant
    {
        /// <summary>自動選択</summary>
        Auto,
        /// <summary>簡体字</summary>
        Simplified,
        /// <summary>繁体字</summary>
        Traditional,
        /// <summary>広東語</summary>
        Cantonese
    }

    /// <summary>
    /// 利用可能な言語の定義
    /// </summary>
    public static class AvailableLanguages
    {
        /// <summary>
        /// サポートされている言語のリスト
        /// </summary>
        public static readonly IReadOnlyList<LanguageInfo> SupportedLanguages =
        [
            new() { Code = "auto", DisplayName = "自動検出", NativeName = "Auto Detect", Flag = "🌍", IsAutoDetect = true },
            new() { Code = "ja", DisplayName = "日本語", NativeName = "日本語", Flag = "🇯🇵", RegionCode = "JP" },
            new() { Code = "en", DisplayName = "英語", NativeName = "English", Flag = "🇺🇸", RegionCode = "US" },
            new() { Code = "zh", DisplayName = "中国語（自動）", NativeName = "中文（自动）", Flag = "🇨🇳", Variant = "Auto" },
            new() { Code = "zh-Hans", DisplayName = "中国語（簡体字）", NativeName = "中文（简体）", Flag = "🇨🇳", Variant = "Simplified", RegionCode = "CN" },
            new() { Code = "zh-Hant", DisplayName = "中国語（繁体字）", NativeName = "中文（繁體）", Flag = "🇹🇼", Variant = "Traditional", RegionCode = "TW" },
            new() { Code = "yue", DisplayName = "広東語", NativeName = "粵語", Flag = "🇭🇰", Variant = "Cantonese", RegionCode = "HK" },
            new() { Code = "ko", DisplayName = "韓国語", NativeName = "한국어", Flag = "🇰🇷", RegionCode = "KR" },
            new() { Code = "es", DisplayName = "スペイン語", NativeName = "Español", Flag = "🇪🇸", RegionCode = "ES" },
            new() { Code = "fr", DisplayName = "フランス語", NativeName = "Français", Flag = "🇫🇷", RegionCode = "FR" },
            new() { Code = "de", DisplayName = "ドイツ語", NativeName = "Deutsch", Flag = "🇩🇪", RegionCode = "DE" },
            new() { Code = "ru", DisplayName = "ロシア語", NativeName = "Русский", Flag = "🇷🇺", RegionCode = "RU" },
            new() { Code = "ar", DisplayName = "アラビア語", NativeName = "العربية", Flag = "🇸🇦", RegionCode = "SA", IsRightToLeft = true }
        ];
        
        /// <summary>
        /// 現在サポートされている言語ペア
        /// </summary>
        public static readonly IReadOnlyList<string> SupportedLanguagePairs =
        [
            "ja-en",   // 日本語 → 英語
            "en-ja",   // 英語 → 日本語
            "zh-en",   // 中国語 → 英語
            "en-zh",   // 英語 → 中国語
            "zh-ja",   // 中国語 → 日本語
            "ja-zh",   // 日本語 → 中国語（2段階）
            "zh-Hans-ja", // 簡体字中国語 → 日本語
            "ja-zh-Hans"  // 日本語 → 簡体字中国語
        ];
    }

    /// <summary>
    /// 翻訳エンジンアイテム
    /// </summary>
    public sealed record TranslationEngineItem(
        TranslationEngine Engine,
        string Id,
        string DisplayName,
        string Description);

    /// <summary>
    /// 翻訳戦略アイテム
    /// </summary>
    public sealed record TranslationStrategyItem(
        TranslationStrategy Strategy,
        string DisplayName,
        string Description,
        bool IsAvailable);

    /// <summary>
    /// 言語情報
    /// </summary>
    public sealed class LanguageInfo
    {
        /// <summary>言語コード</summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>表示名</summary>
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>ネイティブ名</summary>
        public string NativeName { get; set; } = string.Empty;
        /// <summary>フラグ絵文字</summary>
        public string Flag { get; set; } = string.Empty;
        /// <summary>地域コード</summary>
        public string RegionCode { get; set; } = string.Empty;
        /// <summary>言語変種</summary>
        public string Variant { get; set; } = string.Empty;
        /// <summary>自動検出言語かどうか</summary>
        public bool IsAutoDetect { get; set; }
        /// <summary>右から左に書く言語かどうか</summary>
        public bool IsRightToLeft { get; set; }
    }

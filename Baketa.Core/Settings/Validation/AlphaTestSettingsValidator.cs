using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Core.Settings.Validation;

/// <summary>
/// αテスト専用設定バリデーター
/// αテストに必要な最小限の設定検証機能を提供
/// </summary>
public sealed class AlphaTestSettingsValidator : ISettingsValidator
{
    private readonly Dictionary<string, List<IValidationRule>> _globalRules = [];
    private readonly Dictionary<string, Dictionary<string, List<IValidationRule>>> _categoryRules = [];
    private readonly ILogger<AlphaTestSettingsValidator>? _logger;

    /// <summary>
    /// AlphaTestSettingsValidatorを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public AlphaTestSettingsValidator(ILogger<AlphaTestSettingsValidator>? logger = null)
    {
        _logger = logger;
        RegisterDefaultAlphaTestRules();
        _logger?.LogInformation("αテスト設定バリデーターが初期化されました");
    }

    /// <inheritdoc />
    public SettingsValidationResult Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        var results = new List<SettingValidationResult>();
        var context = new ValidationContext(settings, string.Empty, string.Empty, mode: ValidationMode.AlphaTest);
        
        var startTime = DateTime.Now;
        
        try
        {
            // グローバルルールの適用
            ApplyGlobalRules(settings, results, context);
            
            // αテスト対象カテゴリの検証
            ValidateAlphaTestCategories(settings, results, context);
            
            var elapsed = (DateTime.Now - startTime).Milliseconds;
            _logger?.LogDebug("設定検証完了: {ResultCount}件の結果, {ElapsedMs}ms", results.Count, elapsed);
            
            return new SettingsValidationResult(results, validationTimeMs: elapsed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "設定検証中にエラーが発生しました");
            return SettingsValidationResult.CreateFailure("設定検証中に予期しないエラーが発生しました");
        }
    }

    /// <inheritdoc />
    public SettingsValidationResult Validate<T>(string category, T settings) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        ArgumentNullException.ThrowIfNull(settings);
        
        var results = new List<SettingValidationResult>();
        var dummySettings = CreateDummySettings(category, settings);
        var context = new ValidationContext(dummySettings, category, category, mode: ValidationMode.AlphaTest);
        
        try
        {
            ValidateCategory(category, settings, results, context);
            return new SettingsValidationResult(results, category);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "カテゴリ設定検証中にエラーが発生しました: {Category}", category);
            return SettingsValidationResult.CreateFailure($"カテゴリ'{category}'の検証中にエラーが発生しました");
        }
    }

    /// <inheritdoc />
    public void AddRule(IValidationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        
        var path = rule.PropertyPath;
        if (!_globalRules.TryGetValue(path, out var rules))
        {
            rules = [];
            _globalRules[path] = rules;
        }
        
        rules.Add(rule);
        SortRules(rules);
        
        _logger?.LogDebug("グローバル検証ルールを追加: {Path}", path);
    }

    /// <inheritdoc />
    public void AddRule(string category, IValidationRule rule)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        ArgumentNullException.ThrowIfNull(rule);
        
        if (!_categoryRules.TryGetValue(category, out var categoryRuleDict))
        {
            categoryRuleDict = [];
            _categoryRules[category] = categoryRuleDict;
        }
        
        var path = rule.PropertyPath;
        if (!categoryRuleDict.TryGetValue(path, out var rules))
        {
            rules = [];
            categoryRuleDict[path] = rules;
        }
        
        rules.Add(rule);
        SortRules(rules);
        
        _logger?.LogDebug("カテゴリ検証ルールを追加: {Category}.{Path}", category, path);
    }

    /// <inheritdoc />
    public IReadOnlyList<IValidationRule> GetRules()
    {
        return _globalRules.Values.SelectMany(rules => rules).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<IValidationRule> GetRules(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        
        if (_categoryRules.TryGetValue(category, out var categoryRules))
        {
            return categoryRules.Values.SelectMany(rules => rules).ToList();
        }
        
        return [];
    }

    #region Private Methods

    /// <summary>
    /// αテスト用のデフォルト検証ルールを登録します
    /// </summary>
    private void RegisterDefaultAlphaTestRules()
    {
        // 翻訳設定のルール
        AddRule("Translation", new AlphaTestTranslationEngineRule());
        AddRule("Translation", new AlphaTestLanguagePairRule());
        
        // UI設定のルール
        AddRule("MainUi", new AlphaTestPanelSizeRule());
        // 透明度設定は削除済み（固定値0.9を使用）
        
        // キャプチャ設定のルール
        AddRule("Capture", new AlphaTestCaptureIntervalRule());
        
        // テスト用にグローバルルールとしても追加
        AddRule(new AlphaTestTranslationEngineRule());
        AddRule(new AlphaTestLanguagePairRule());
        AddRule(new AlphaTestPanelSizeRule());
        // AddRule(new AlphaTestOpacityRule()); // 透明度設定は削除済み
        AddRule(new AlphaTestCaptureIntervalRule());
        
        _logger?.LogInformation("αテスト用デフォルト検証ルールを登録しました");
    }

    /// <summary>
    /// グローバルルールを適用します
    /// </summary>
    private void ApplyGlobalRules(AppSettings settings, List<SettingValidationResult> results, ValidationContext context)
    {
        foreach (var (path, rules) in _globalRules)
        {
            foreach (var rule in rules)
            {
                try
                {
                    var value = GetValueByPath(settings, path);
                    // 値が存在する場合のみ検証を実行
                    if (value != null)
                    {
                        var result = rule.Validate(value, context);
                        results.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "グローバルルール適用エラー: {Path}", path);
                    var errorResult = CreateValidationError(path, $"検証ルール適用エラー: {ex.Message}");
                    results.Add(errorResult);
                }
            }
        }
    }

    /// <summary>
    /// αテスト対象カテゴリを検証します
    /// </summary>
    private void ValidateAlphaTestCategories(AppSettings settings, List<SettingValidationResult> results, ValidationContext context)
    {
        // αテスト対象カテゴリのみ検証（nullチェック付き）
        if (settings.Translation != null)
            ValidateCategory("Translation", settings.Translation, results, context);
        if (settings.MainUi != null)
            ValidateCategory("MainUi", settings.MainUi, results, context);
        if (settings.Overlay != null)
            ValidateCategory("Overlay", settings.Overlay, results, context);
        if (settings.Capture != null)
            ValidateCategory("Capture", settings.Capture, results, context);
        if (settings.Ocr != null)
            ValidateCategory("Ocr", settings.Ocr, results, context);
    }

    /// <summary>
    /// 特定カテゴリを検証します
    /// </summary>
    private void ValidateCategory<T>(string category, T settings, List<SettingValidationResult> results, ValidationContext context) where T : class
    {
        if (settings == null)
        {
            return; // null設定はスキップ
        }

        if (!_categoryRules.TryGetValue(category, out var categoryRules))
        {
            return; // ルールが定義されていないカテゴリはスキップ
        }

        var categoryContext = context.CreateChild(category, category);

        foreach (var (path, rules) in categoryRules)
        {
            foreach (var rule in rules)
            {
                try
                {
                    var value = GetValueByPath(settings, path);
                    // 値が存在する場合のみ検証を実行
                    if (value != null)
                    {
                        var result = rule.Validate(value, categoryContext);
                        results.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "カテゴリルール適用エラー: {Category}.{Path}", category, path);
                    var errorResult = CreateValidationError($"{category}.{path}", $"検証ルール適用エラー: {ex.Message}");
                    results.Add(errorResult);
                }
            }
        }
    }

    /// <summary>
    /// ルールを優先度順にソートします
    /// </summary>
    private static void SortRules(List<IValidationRule> rules)
    {
        rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// パスから値を取得します
    /// </summary>
    private static object? GetValueByPath(object obj, string path)
    {
        var parts = path.Split('.');
        object? current = obj;

        foreach (var part in parts)
        {
            if (current == null) return null;
            
            var property = current.GetType().GetProperty(part);
            if (property == null) return null;
            
            current = property.GetValue(current);
        }

        return current;
    }

    /// <summary>
    /// ダミー設定を作成します
    /// </summary>
    private static AppSettings CreateDummySettings<T>(string category, T settings) where T : class
    {
        var dummySettings = new AppSettings();
        
        // カテゴリに応じて適切なプロパティに設定を割り当て
        var property = typeof(AppSettings).GetProperty(category);
        property?.SetValue(dummySettings, settings);
        
        return dummySettings;
    }

    /// <summary>
    /// 検証エラー結果を作成します
    /// </summary>
    private static SettingValidationResult CreateValidationError(string path, string errorMessage)
    {
        var dummyMetadata = CreateDummyMetadata(path);
        return SettingValidationResult.Failure(dummyMetadata, null, errorMessage);
    }

    /// <summary>
    /// ダミーメタデータを作成します
    /// </summary>
    private static SettingMetadata CreateDummyMetadata(string path)
    {
        var dummyProperty = typeof(DummySettings).GetProperty(nameof(DummySettings.ErrorProperty))!;
        var dummyAttribute = new SettingMetadataAttribute(SettingLevel.Basic, "Error", path);
        return new SettingMetadata(dummyProperty, dummyAttribute);
    }

    #endregion
}

/// <summary>
/// ダミー設定クラス（エラー用）
/// </summary>
internal sealed class DummySettings
{
    public string ErrorProperty { get; set; } = string.Empty;
}
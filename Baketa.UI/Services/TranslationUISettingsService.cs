using Baketa.Core.Abstractions.Services;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// UI設定の翻訳言語ペアにアクセスする実装クラス
/// </summary>
public sealed class TranslationUISettingsService : ITranslationUISettingsService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationUISettingsService> _logger;
    
    public TranslationUISettingsService(IServiceProvider serviceProvider, ILogger<TranslationUISettingsService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public string GetCurrentSourceLanguage()
    {
        try
        {
            _logger.LogDebug("🔍 [UI_SETTINGS] ソース言語取得開始");
            
            var translationSettings = _serviceProvider.GetService<TranslationSettingsViewModel>();
            _logger.LogDebug("🔍 [UI_SETTINGS] TranslationSettingsViewModel取得: {IsNull}", translationSettings == null ? "NULL" : "取得成功");
            
            if (translationSettings != null)
            {
                var languagePairSelection = translationSettings.LanguagePairSelection;
                _logger.LogDebug("🔍 [UI_SETTINGS] LanguagePairSelection取得: {IsNull}", languagePairSelection == null ? "NULL" : "取得成功");
                
                if (languagePairSelection != null)
                {
                    var selectedPair = languagePairSelection.SelectedLanguagePair;
                    _logger.LogDebug("🔍 [UI_SETTINGS] SelectedLanguagePair取得: {IsNull}", selectedPair == null ? "NULL" : "取得成功");
                    
                    if (selectedPair?.SourceLanguage is not null)
                    {
                        _logger.LogInformation("✅ [UI_SETTINGS] UI設定からソース言語取得成功: '{SourceLanguage}'", selectedPair.SourceLanguage);
                        return selectedPair.SourceLanguage;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [UI_SETTINGS] SelectedPair.SourceLanguageがnull");
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [UI_SETTINGS] LanguagePairSelectionがnull");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [UI_SETTINGS] TranslationSettingsViewModelがnull");
            }
            
            // フォールバック: デフォルト値を返す
            _logger.LogWarning("🔄 [UI_SETTINGS] フォールバック: デフォルト値 'ja' を使用");
            return "ja";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [UI_SETTINGS] ソース言語取得中にエラーが発生");
            return "ja"; // フォールバック
        }
    }
    
    /// <inheritdoc />
    public string GetCurrentTargetLanguage()
    {
        try
        {
            _logger.LogDebug("🔍 [UI_SETTINGS] ターゲット言語取得開始");
            
            var translationSettings = _serviceProvider.GetService<TranslationSettingsViewModel>();
            _logger.LogDebug("🔍 [UI_SETTINGS] TranslationSettingsViewModel取得: {IsNull}", translationSettings == null ? "NULL" : "取得成功");
            
            if (translationSettings != null)
            {
                var languagePairSelection = translationSettings.LanguagePairSelection;
                _logger.LogDebug("🔍 [UI_SETTINGS] LanguagePairSelection取得: {IsNull}", languagePairSelection == null ? "NULL" : "取得成功");
                
                if (languagePairSelection != null)
                {
                    var selectedPair = languagePairSelection.SelectedLanguagePair;
                    _logger.LogDebug("🔍 [UI_SETTINGS] SelectedLanguagePair取得: {IsNull}", selectedPair == null ? "NULL" : "取得成功");
                    
                    if (selectedPair?.TargetLanguage is not null)
                    {
                        _logger.LogInformation("✅ [UI_SETTINGS] UI設定からターゲット言語取得成功: '{TargetLanguage}'", selectedPair.TargetLanguage);
                        return selectedPair.TargetLanguage;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [UI_SETTINGS] SelectedPair.TargetLanguageがnull");
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [UI_SETTINGS] LanguagePairSelectionがnull");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [UI_SETTINGS] TranslationSettingsViewModelがnull");
            }
            
            // フォールバック: デフォルト値を返す
            _logger.LogWarning("🔄 [UI_SETTINGS] フォールバック: デフォルト値 'en' を使用");
            return "en";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [UI_SETTINGS] ターゲット言語取得中にエラーが発生");
            return "en"; // フォールバック
        }
    }
    
    /// <inheritdoc />
    public bool IsAutoDetectEnabled()
    {
        try
        {
            _logger.LogDebug("🔍 [UI_SETTINGS] 自動検出状態取得開始");
            
            var translationSettings = _serviceProvider.GetService<TranslationSettingsViewModel>();
            _logger.LogDebug("🔍 [UI_SETTINGS] TranslationSettingsViewModel取得: {IsNull}", translationSettings == null ? "NULL" : "取得成功");
            
            if (translationSettings != null)
            {
                var languagePairSelection = translationSettings.LanguagePairSelection;
                _logger.LogDebug("🔍 [UI_SETTINGS] LanguagePairSelection取得: {IsNull}", languagePairSelection == null ? "NULL" : "取得成功");
                
                if (languagePairSelection != null)
                {
                    var selectedPair = languagePairSelection.SelectedLanguagePair;
                    _logger.LogDebug("🔍 [UI_SETTINGS] SelectedLanguagePair取得: {IsNull}", selectedPair == null ? "NULL" : "取得成功");
                    
                    // ソース言語が "auto" の場合は自動検出が有効
                    bool isAutoDetect = selectedPair?.SourceLanguage == "auto";
                    
                    _logger.LogInformation("✅ [UI_SETTINGS] UI設定から自動検出状態取得: {IsAutoDetect} (SourceLanguage: '{SourceLanguage}')", 
                        isAutoDetect, selectedPair?.SourceLanguage ?? "null");
                    return isAutoDetect;
                }
                else
                {
                    _logger.LogWarning("⚠️ [UI_SETTINGS] LanguagePairSelectionがnull");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [UI_SETTINGS] TranslationSettingsViewModelがnull");
            }
            
            // フォールバック: 自動検出無効
            _logger.LogWarning("🔄 [UI_SETTINGS] フォールバック: 自動検出無効 (false) を使用");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [UI_SETTINGS] 自動検出状態取得中にエラーが発生");
            return false; // フォールバック: 自動検出無効
        }
    }
}
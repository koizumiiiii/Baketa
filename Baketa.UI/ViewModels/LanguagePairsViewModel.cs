using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.UI.Framework;
using Baketa.UI.Framework.ReactiveUI;
using Baketa.UI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;

// 名前空間エイリアスを使用して衝突を解決
// using UIEvents = Baketa.UI.Framework.Events; // 古いEventsを削除

namespace Baketa.UI.ViewModels;

/// <summary>
/// 言語ペア設定のビューモデル
/// </summary>
internal sealed class LanguagePairsViewModel : Framework.ViewModelBase
{
    // LoggerMessageデリゲート
    private static readonly Action<ILogger, string, Exception?> _logLanguagePairOperationError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(_logLanguagePairOperationError)),
            "言語ペア設定の操作中にエラーが発生しました: {Message}");

    // 選択された言語ペア
    private LanguagePairConfiguration? _selectedLanguagePair;
    
    /// <summary>
    /// サポートされる言語のリストを取得
    /// </summary>
    private static List<LanguageInfo> GetSupportedLanguages()
    {
        return [.. Baketa.UI.Models.AvailableLanguages.SupportedLanguages];
    }
    
    /// <summary>
    /// 言語ペア設定のコレクション
    /// </summary>
    public ObservableCollection<LanguagePairConfiguration> LanguagePairs { get; }
    
    /// <summary>
    /// 選択された言語ペア
    /// </summary>
    public LanguagePairConfiguration? SelectedLanguagePair
    {
        get => _selectedLanguagePair;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedLanguagePair, value);
    }
    
    /// <summary>
    /// 利用可能な言語のリスト
    /// </summary>
    public ObservableCollection<LanguageInfo> AvailableLanguages { get; }
    
    /// <summary>
    /// 利用可能なエンジンのリスト
    /// </summary>
    public ObservableCollection<string> AvailableEngines { get; }
    
    /// <summary>
    /// 利用可能な翻訳戦略のリスト
    /// </summary>
    public ObservableCollection<TranslationStrategy> AvailableStrategies { get; }
    
    /// <summary>
    /// 利用可能な中国語変種のリスト
    /// </summary>
    public ObservableCollection<ChineseVariant> AvailableChineseVariants { get; }
    
    // コマンド
    public ReactiveCommand<Unit, Unit> AddLanguagePairCommand { get; }
    public ReactiveCommand<LanguagePairConfiguration, Unit> RemoveLanguagePairCommand { get; }
    public ReactiveCommand<LanguagePairConfiguration, Unit> EditLanguagePairCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveLanguagePairsCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadPresetCommand { get; }
    
    /// <summary>
    /// 新しいLanguagePairsViewModelを初期化します
    /// </summary>
    public LanguagePairsViewModel(
    Baketa.Core.Abstractions.Events.IEventAggregator eventAggregator,
        ILogger? logger = null)
        : base(eventAggregator, logger)
    {
        // コレクションの初期化
        LanguagePairs = [];
        AvailableLanguages = new(GetSupportedLanguages());
        AvailableEngines = new(["LocalOnly", "CloudOnly"]);
        AvailableStrategies = 
        [
            TranslationStrategy.Direct,
            TranslationStrategy.TwoStage
        ];
        AvailableChineseVariants =
        [
            ChineseVariant.Auto,
            ChineseVariant.Simplified,
            ChineseVariant.Traditional,
            ChineseVariant.Cantonese
        ];
        
        // コマンドの初期化
        AddLanguagePairCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteAddLanguagePairAsync);
        RemoveLanguagePairCommand = ReactiveCommand.Create<LanguagePairConfiguration>(ExecuteRemoveLanguagePair);
        EditLanguagePairCommand = ReactiveCommand.Create<LanguagePairConfiguration>(ExecuteEditLanguagePair);
        ResetToDefaultsCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteResetToDefaultsAsync);
        SaveLanguagePairsCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteSaveLanguagePairsAsync);
        LoadPresetCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteLoadPresetAsync);
        
        // デフォルトの言語ペアを読み込み
        LoadDefaultLanguagePairs();
    }
    
    /// <summary>
    /// アクティベーション時の処理
    /// </summary>
    protected override void HandleActivation()
    {
        // 言語ペア設定を読み込む
        LoadLanguagePairSettings();
    }
    
    /// <summary>
    /// デフォルトの言語ペアを読み込み
    /// </summary>
    private void LoadDefaultLanguagePairs()
    {
        try
        {
            var defaultPairs = new[]
            {
                CreateLanguagePair("ja", "en", "日本語", "英語", TranslationStrategy.Direct, 50, 1, "日本語から英語への高速直接翻訳"),
                CreateLanguagePair("en", "ja", "英語", "日本語", TranslationStrategy.Direct, 45, 1, "英語から日本語への高速直接翻訳"),
                CreateLanguagePair("zh", "en", "中国語", "英語", TranslationStrategy.Direct, 60, 2, "中国語から英語への直接翻訳"),
                CreateLanguagePair("en", "zh", "英語", "中国語", TranslationStrategy.Direct, 65, 2, "英語から中国語への直接翻訳（変種対応）"),
                CreateLanguagePair("zh", "ja", "中国語", "日本語", TranslationStrategy.Direct, 55, 2, "中国語から日本語への直接翻訳"),
                CreateLanguagePair("ja", "zh", "日本語", "中国語", TranslationStrategy.TwoStage, 120, 3, "日本語から中国語への2段階翻訳（ja→en→zh）"),
                CreateLanguagePair("zh-Hans", "ja", "簡体字中国語", "日本語", TranslationStrategy.Direct, 55, 2, "簡体字中国語から日本語への直接翻訳"),
                CreateLanguagePair("ja", "zh-Hans", "日本語", "簡体字中国語", TranslationStrategy.TwoStage, 120, 3, "日本語から簡体字中国語への2段階翻訳")
            };
            
            foreach (var pair in defaultPairs)
            {
                LanguagePairs.Add(pair);
            }
            
            Logger?.LogInformation("デフォルトの言語ペア {Count} 個を読み込みました", defaultPairs.Length);
        }
        catch (ArgumentException ex)
        {
            Logger?.LogError(ex, "言語ペア設定の引数が無効です");
        }
        catch (InvalidOperationException ex)
        {
            Logger?.LogError(ex, "言語ペアコレクションの操作に失敗しました");
        }
        catch (OutOfMemoryException ex)
        {
            Logger?.LogError(ex, "メモリ不足により言語ペアの読み込みに失敗しました");
        }
    }
    
    /// <summary>
    /// 言語ペアを作成
    /// </summary>
    private static LanguagePairConfiguration CreateLanguagePair(
        string sourceCode, 
        string targetCode, 
        string sourceDisplay, 
        string targetDisplay, 
        TranslationStrategy strategy, 
        double latency, 
        int priority,
        string description)
    {
        var pair = new LanguagePairConfiguration
        {
            SourceLanguage = sourceCode,
            TargetLanguage = targetCode,
            SourceLanguageDisplay = sourceDisplay,
            TargetLanguageDisplay = targetDisplay,
            Strategy = strategy,
            EstimatedLatencyMs = latency,
            Priority = priority,
            Description = description,
            IsEnabled = true,
            SelectedEngine = "LocalOnly" // デフォルトエンジン
        };
        
        // 中国語関連の場合は中国語変種を設定
        if (pair.IsChineseRelated)
        {
            pair.ChineseVariant = sourceCode.Contains("Hans", StringComparison.Ordinal) || targetCode.Contains("Hans", StringComparison.Ordinal) ? 
                ChineseVariant.Simplified : ChineseVariant.Auto;
        }
        
        return pair;
    }
    
    /// <summary>
    /// 言語ペア設定を読み込み
    /// </summary>
    private void LoadLanguagePairSettings()
    {
        // TODO: 永続化された設定を読み込む処理を実装する
        Logger?.LogDebug("言語ペア設定を読み込みました");
    }
    
    /// <summary>
    /// 言語ペア追加コマンド実行
    /// </summary>
    private async Task ExecuteAddLanguagePairAsync()
    {
        try
        {
            // 新しい言語ペアのデフォルト設定
            var newPair = new LanguagePairConfiguration
            {
                SourceLanguage = "ja",
                TargetLanguage = "en",
                SourceLanguageDisplay = "日本語",
                TargetLanguageDisplay = "英語",
                Strategy = TranslationStrategy.Direct,
                EstimatedLatencyMs = 50,
                Priority = LanguagePairs.Count + 1,
                Description = "新しい言語ペア",
                IsEnabled = true,
                SelectedEngine = "LocalOnly"
            };
            
            LanguagePairs.Add(newPair);
            SelectedLanguagePair = newPair;
            
            Logger?.LogDebug("新しい言語ペアを追加しました: {LanguagePair}", newPair.LanguagePairKey);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = $"言語ペアの追加パラメータが無効です: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"言語ペアの追加操作が無効です: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (OutOfMemoryException ex)
        {
            ErrorMessage = $"メモリ不足により言語ペアの追加に失敗しました: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
    }
    
    /// <summary>
    /// 言語ペア削除コマンド実行
    /// </summary>
    private void ExecuteRemoveLanguagePair(LanguagePairConfiguration languagePair)
    {
        try
        {
            if (languagePair == null)
            {
                return;
            }
            
            LanguagePairs.Remove(languagePair);
            
            if (SelectedLanguagePair == languagePair)
            {
                SelectedLanguagePair = LanguagePairs.FirstOrDefault();
            }
            
            Logger?.LogDebug("言語ペアを削除しました: {LanguagePair}", languagePair.LanguagePairKey);
        }
        catch (ArgumentNullException ex)
        {
            ErrorMessage = $"削除対象の言語ペアが指定されていません: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"言語ペアの削除操作が無効です: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
    }
    
    /// <summary>
    /// 言語ペア編集コマンド実行
    /// </summary>
    private void ExecuteEditLanguagePair(LanguagePairConfiguration languagePair)
    {
        try
        {
            if (languagePair == null)
            {
                return;
            }
            
            SelectedLanguagePair = languagePair;
            
            Logger?.LogDebug("言語ペアを編集モードにしました: {LanguagePair}", languagePair.LanguagePairKey);
        }
        catch (ArgumentNullException ex)
        {
            ErrorMessage = $"編集対象の言語ペアが指定されていません: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
    }
    
    /// <summary>
    /// デフォルトにリセットコマンド実行
    /// </summary>
    private async Task ExecuteResetToDefaultsAsync()
    {
        try
        {
            LanguagePairs.Clear();
            LoadDefaultLanguagePairs();
            SelectedLanguagePair = LanguagePairs.FirstOrDefault();
            
            Logger?.LogInformation("言語ペア設定をデフォルトにリセットしました");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"デフォルトリセット操作が無効です: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (OutOfMemoryException ex)
        {
            ErrorMessage = $"メモリ不足によりリセットに失敗しました: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
    }
    
    /// <summary>
    /// 言語ペア保存コマンド実行
    /// </summary>
    private async Task ExecuteSaveLanguagePairsAsync()
    {
        IsLoading = true;
        try
        {
            // TODO: 永続化処理を実装
            
            // 言語ペア設定変更イベントを発行
            var languagePairEvent = new LanguagePairSettingsChangedEvent
            {
                LanguagePairs = [.. LanguagePairs],
                TotalPairs = LanguagePairs.Count,
                EnabledPairs = LanguagePairs.Count(p => p.IsEnabled)
            };
            await PublishEventAsync(languagePairEvent).ConfigureAwait(false);
            
            Logger?.LogInformation("言語ペア設定を保存しました: {Count} ペア", LanguagePairs.Count);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"言語ペア設定の保存操作が無効です: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = $"設定ファイルへのアクセスが拒否されました: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (IOException ex)
        {
            ErrorMessage = $"設定ファイルの入出力エラーが発生しました: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// プリセット読み込みコマンド実行
    /// </summary>
    private async Task ExecuteLoadPresetAsync()
    {
        try
        {
            // TODO: プリセット選択ダイアログの実装
            Logger?.LogDebug("プリセット読み込み機能は今後実装予定です");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (NotImplementedException ex)
        {
            ErrorMessage = $"プリセット機能はまだ実装されていません: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = $"プリセットファイルへのアクセスが拒否されました: {ex.Message}";
            _logLanguagePairOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
    }
    
    /// <summary>
    /// 言語コードから表示名を取得
    /// </summary>
    public string GetLanguageDisplayName(string languageCode)
    {
        LanguageInfo? language = AvailableLanguages.FirstOrDefault(l => l.Code == languageCode);
        return language?.DisplayName ?? languageCode;
    }
    
    /// <summary>
    /// 言語ペアの妥当性を検証
    /// </summary>
    public bool ValidateLanguagePair(LanguagePairConfiguration pair)
    {
        if (pair == null) return false;
        
        // 同じ言語ペアの重複チェック
        var duplicate = LanguagePairs.Any(p => 
        p != pair && 
        string.Equals(p.SourceLanguage, pair.SourceLanguage, StringComparison.Ordinal) && 
        string.Equals(p.TargetLanguage, pair.TargetLanguage, StringComparison.Ordinal));
            
        if (duplicate)
        {
            ErrorMessage = "同じ言語ペアが既に存在します";
            return false;
        }
        
        // 無効な言語コードチェック
        if (string.IsNullOrEmpty(pair.SourceLanguage) || string.IsNullOrEmpty(pair.TargetLanguage))
        {
            ErrorMessage = "言語コードが指定されていません";
            return false;
        }
        
        // 同じ言語チェック
        if (string.Equals(pair.SourceLanguage, pair.TargetLanguage, StringComparison.Ordinal))
        {
            ErrorMessage = "ソース言語とターゲット言語が同じです";
            return false;
        }
        
        return true;
    }
}

/// <summary>
/// 言語ペア設定変更イベント
/// </summary>
internal sealed class LanguagePairSettingsChangedEvent : Baketa.Core.Events.EventBase
{
    /// <inheritdoc/>
    public override string Name => "LanguagePairSettingsChanged";
    
    /// <inheritdoc/>
    public override string Category => "UI.Settings";
    
    /// <summary>
    /// 言語ペアの設定リスト
    /// </summary>
    public List<LanguagePairConfiguration> LanguagePairs { get; set; } = [];
    
    /// <summary>
    /// 総ペア数
    /// </summary>
    public int TotalPairs { get; set; }
    
    /// <summary>
    /// 有効ペア数
    /// </summary>
    public int EnabledPairs { get; set; }
}

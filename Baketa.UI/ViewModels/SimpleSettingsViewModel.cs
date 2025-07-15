using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Utils;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// シンプル設定画面のViewModel
/// αテスト向け基本設定のみ - タブなしのシンプル版
/// </summary>
public class SimpleSettingsViewModel : ViewModelBase
{
    private bool _useLocalEngine = true;
    private string _sourceLanguage = "Japanese";
    private string _targetLanguage = "English";
    private int _fontSize = 14;
    private bool _hasChanges;

    // 設定保存用（他の設定ファイルと統一）
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".baketa", "settings", "translation-settings.json");

    // JSON設定オプション（再利用）
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true // 大文字小文字を区別しない
    };

    // 設定データクラス
    private class SimpleSettingsData
    {
        public bool UseLocalEngine { get; set; } = true;
        public string SourceLanguage { get; set; } = "Japanese";
        public string TargetLanguage { get; set; } = "English";
        public int FontSize { get; set; } = 14;
    }

    public SimpleSettingsViewModel(
        IEventAggregator eventAggregator,
        ILogger<SimpleSettingsViewModel> logger)
        : base(eventAggregator, logger)
    {
        var vmHash = GetHashCode();
        DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] コンストラクタ開始");
        InitializeCommands();
        InitializeCollections();
        DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] コンストラクタ完了 - 初期設定: {DebugInfo}");
    }

    #region Properties

    /// <summary>
    /// αテストモードかどうか
    /// </summary>
    public bool IsAlphaTest => true; // αテスト期間中は常にtrue

    /// <summary>
    /// クラウド翻訳が使用可能かどうか（αテストでは無効）
    /// </summary>
    public bool IsCloudTranslationEnabled => !IsAlphaTest;

    /// <summary>
    /// ローカル翻訳エンジンを使用するか
    /// </summary>
    public bool UseLocalEngine
    {
        get => _useLocalEngine;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _useLocalEngine, value);
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でUseLocalEngine設定失敗 - 直接設定で続行");
                _useLocalEngine = value;
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
        }
    }

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
                UpdateAvailableTargetLanguages();
                this.RaisePropertyChanged(nameof(IsLanguagePairValid));
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でSourceLanguage設定失敗 - 直接設定で続行");
                _sourceLanguage = value;
                UpdateAvailableTargetLanguages();
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
        }
    }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _targetLanguage, value);
                this.RaisePropertyChanged(nameof(IsLanguagePairValid));
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でTargetLanguage設定失敗 - 直接設定で続行");
                _targetLanguage = value;
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
        }
    }

    /// <summary>
    /// フォントサイズ
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _fontSize, value);
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でFontSize設定失敗 - 直接設定で続行");
                _fontSize = value;
                this.RaisePropertyChanged(nameof(DebugInfo));
            }
        }
    }


    /// <summary>
    /// 変更があるかどうか
    /// </summary>
    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _hasChanges, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でHasChanges設定失敗 - 直接設定で続行");
                _hasChanges = value;
            }
        }
    }

    /// <summary>
    /// 利用可能な言語リスト
    /// </summary>
    public ObservableCollection<string> AvailableLanguages { get; } = [];

    /// <summary>
    /// 翻訳先で選択可能な言語リスト（翻訳元によって動的に変更）
    /// </summary>
    public ObservableCollection<string> AvailableTargetLanguages { get; } = [];

    /// <summary>
    /// フォントサイズ選択肢
    /// </summary>
    public ObservableCollection<int> FontSizeOptions { get; } = [];

    /// <summary>
    /// デバッグ用：現在の設定値を表示
    /// </summary>
    public string DebugInfo => $"Local:{UseLocalEngine} {SourceLanguage}→{TargetLanguage} Font:{FontSize}";

    /// <summary>
    /// 言語ペアが有効かどうか
    /// </summary>
    public bool IsLanguagePairValid => !string.Equals(SourceLanguage, TargetLanguage, StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> ApplyCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> CancelCommand { get; private set; } = null!;

    #endregion

    #region Initialization

    private void InitializeCommands()
    {
        // コマンドをUIスレッドで安全に初期化
        try
        {
            ApplyCommand = ReactiveCommand.CreateFromTask(ExecuteApplyAsync,
                this.WhenAnyValue(x => x.HasChanges).ObserveOn(RxApp.MainThreadScheduler),
                outputScheduler: RxApp.MainThreadScheduler);
            CancelCommand = ReactiveCommand.CreateFromTask(ExecuteCancelAsync,
                outputScheduler: RxApp.MainThreadScheduler);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "SimpleSettingsViewModelのReactiveCommand初期化エラー");
            throw;
        }
    }

    private void InitializeCollections()
    {
        // 言語選択肢（αテスト版は限定）
        AvailableLanguages.Add("Japanese");
        AvailableLanguages.Add("English");

        // フォントサイズ選択肢
        FontSizeOptions.Add(10);
        FontSizeOptions.Add(12);
        FontSizeOptions.Add(14);
        FontSizeOptions.Add(16);
        FontSizeOptions.Add(18);
        FontSizeOptions.Add(20);

        // 初期状態の翻訳先言語を設定
        UpdateAvailableTargetLanguages();

        // プロパティ変更監視（UIスレッドで安全に処理）
        try
        {
            this.WhenAnyValue(
                    x => x.UseLocalEngine,
                    x => x.SourceLanguage,
                    x => x.TargetLanguage,
                    x => x.FontSize)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => 
                {
                    try
                    {
                        HasChanges = true;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Logger?.LogWarning(ex, "UIスレッド違反でHasChanges設定失敗 - 続行");
                    }
                });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "SimpleSettingsViewModelのプロパティ監視初期化エラー");
            throw;
        }
    }

    #endregion

    #region Command Handlers

    private async Task ExecuteApplyAsync()
    {
        try
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ExecuteApplyAsync開始 - スレッドID: {Environment.CurrentManagedThreadId}");
            Logger?.LogInformation("Applying settings changes");

            // 設定適用イベントを発行
            var settingsEvent = new SettingsChangedEvent
            {
                UseLocalEngine = UseLocalEngine,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                FontSize = FontSize,
                OverlayOpacity = 0.9 // 固定値
            };

            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] SettingsChangedEvent発行前");
            await PublishEventAsync(settingsEvent).ConfigureAwait(false);
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] SettingsChangedEvent発行完了");

            // TODO: 実際の設定ファイルに保存する処理を実装
            // 現在は一時的にメモリに保持のみ
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] 設定保存開始");
            await SaveCurrentSettingsAsync().ConfigureAwait(false);
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] 設定保存完了");

            HasChanges = false;
            Logger?.LogInformation("Settings applied successfully");

            // 適用ボタンは設定を保存するだけでウィンドウは閉じない
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] 適用完了 - ウィンドウは開いたまま");
        }
        catch (Exception ex)
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"💥 [SimpleSettingsViewModel#{vmHash}] ExecuteApplyAsync例外: {ex.Message}");
            Logger?.LogError(ex, "Failed to apply settings");
        }
    }

    private async Task ExecuteCancelAsync()
    {
        try
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ExecuteCancelAsync開始");
            Logger?.LogDebug("Settings changes cancelled");

            // 設定画面を閉じる（変更は保存しない）
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] CancelでCloseRequested?.Invoke()呼び出し - 変更は保存されません");
            CloseRequested?.Invoke();
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] CancelでCloseRequested?.Invoke()完了");
        }
        catch (Exception ex)
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"💥 [SimpleSettingsViewModel#{vmHash}] ExecuteCancelAsync例外: {ex.Message}");
            Logger?.LogError(ex, "Failed to cancel settings");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }


    #endregion

    #region Events

    /// <summary>
    /// ウィンドウを閉じる要求イベント
    /// </summary>
    public event Action? CloseRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// 設定を読み込み
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        try
        {
            var vmHash = GetHashCode();
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] LoadSettingsAsync開始");
            Logger?.LogDebug("Loading current settings");

            var settings = await LoadSettingsFromFileAsync().ConfigureAwait(false);
            
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] 読み込み設定: UseLocalEngine={settings.UseLocalEngine}, SourceLanguage={settings.SourceLanguage}, TargetLanguage={settings.TargetLanguage}, FontSize={settings.FontSize}");
            
            // 設定を適用（UIスレッドで既に実行されている前提）
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] 設定適用開始");
            try
            {
                UseLocalEngine = settings.UseLocalEngine;
                DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] UseLocalEngine設定完了: {UseLocalEngine}");
                
                SourceLanguage = settings.SourceLanguage;
                DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] SourceLanguage設定完了: {SourceLanguage}");
                
                TargetLanguage = settings.TargetLanguage;
                DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] TargetLanguage設定完了: {TargetLanguage}");
                
                FontSize = settings.FontSize;
                DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] FontSize設定完了: {FontSize}");
                
                HasChanges = false;
                DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] 設定適用完了: {DebugInfo}");
            }
            catch (Exception propEx)
            {
                DebugHelper.Log($"💥 [SimpleSettingsViewModel#{vmHash}] プロパティ設定例外: {propEx.Message}");
                throw;
            }
            Logger?.LogDebug("Settings loaded successfully");
        }
        catch (Exception ex)
        {
            var vmHash = GetHashCode();
            DebugHelper.Log($"💥 [SimpleSettingsViewModel#{vmHash}] LoadSettingsAsync例外: {ex.Message}");
            Logger?.LogError(ex, "Failed to load settings");
        }
    }

    /// <summary>
    /// 外部から設定値を更新
    /// </summary>
    public void UpdateSettings(bool useLocalEngine, string sourceLanguage, string targetLanguage, int fontSize)
    {
        UseLocalEngine = useLocalEngine;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        FontSize = fontSize;
        HasChanges = false;
    }

    /// <summary>
    /// 翻訳元言語に基づいて利用可能な翻訳先言語を更新
    /// </summary>
    private void UpdateAvailableTargetLanguages()
    {
        AvailableTargetLanguages.Clear();
        
        // αテストでは日本語↔英語のペアのみ
        if (SourceLanguage == "Japanese")
        {
            AvailableTargetLanguages.Add("English");
        }
        else if (SourceLanguage == "English")
        {
            AvailableTargetLanguages.Add("Japanese");
        }
        
        // 現在の翻訳先が使用不可になった場合は自動調整
        if (!AvailableTargetLanguages.Contains(TargetLanguage) && AvailableTargetLanguages.Count > 0)
        {
            TargetLanguage = AvailableTargetLanguages.First();
        }
    }

    /// <summary>
    /// 現在の設定を保存
    /// </summary>
    private async Task SaveCurrentSettingsAsync()
    {
        try
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] SaveCurrentSettingsAsync開始");
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] 保存する設定: UseLocalEngine={UseLocalEngine}, SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}, FontSize={FontSize}");
            
            var settings = new SimpleSettingsData
            {
                UseLocalEngine = UseLocalEngine,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                FontSize = FontSize
            };

            await SaveSettingsToFileAsync(settings).ConfigureAwait(false);
            
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ファイル保存完了");
            Logger?.LogDebug("Settings saved - UseLocalEngine: {UseLocalEngine}, SourceLanguage: {SourceLanguage}, TargetLanguage: {TargetLanguage}, FontSize: {FontSize}", 
                UseLocalEngine, SourceLanguage, TargetLanguage, FontSize);
        }
        catch (Exception ex)
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"💥 [SimpleSettingsViewModel#{vmHash}] SaveCurrentSettingsAsync例外: {ex.Message}");
            Logger?.LogError(ex, "Failed to save settings");
        }
    }

    /// <summary>
    /// 設定ファイルから読み込み
    /// </summary>
    private async Task<SimpleSettingsData> LoadSettingsFromFileAsync()
    {
        try
        {
            var vmHash = GetHashCode();
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] LoadSettingsFromFileAsync開始 - ファイルパス: {SettingsFilePath}");
            
            if (!File.Exists(SettingsFilePath))
            {
                DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] 設定ファイルが存在しません - デフォルト値を使用");
                Logger?.LogDebug("Settings file not found, using defaults");
                return new SimpleSettingsData();
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath).ConfigureAwait(false);
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] ファイル読み込み完了: {json}");
            
            var settings = JsonSerializer.Deserialize<SimpleSettingsData>(json, JsonOptions);
            var result = settings ?? new SimpleSettingsData();
            
            DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] デシリアライズ完了: UseLocalEngine={result.UseLocalEngine}, SourceLanguage={result.SourceLanguage}, TargetLanguage={result.TargetLanguage}, FontSize={result.FontSize}");
            
            return result;
        }
        catch (Exception ex)
        {
            var vmHash = GetHashCode();
            DebugHelper.Log($"💥 [SimpleSettingsViewModel#{vmHash}] LoadSettingsFromFileAsync例外: {ex.Message}");
            Logger?.LogWarning(ex, "Failed to load settings from file, using defaults");
            return new SimpleSettingsData();
        }
    }

    /// <summary>
    /// 設定ファイルに保存
    /// </summary>
    private async Task SaveSettingsToFileAsync(SimpleSettingsData settings)
    {
        try
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] SaveSettingsToFileAsync開始 - ファイルパス: {SettingsFilePath}");
            
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ディレクトリ作成: {directory}");
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] JSON化完了: {json}");
            
            await File.WriteAllTextAsync(SettingsFilePath, json).ConfigureAwait(false);
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ファイル書き込み完了");
        }
        catch (Exception ex)
        {
            var vmHash = GetHashCode();
            Console.WriteLine($"💥 [SimpleSettingsViewModel#{vmHash}] SaveSettingsToFileAsync例外: {ex.Message}");
            Logger?.LogError(ex, "Failed to save settings to file");
            throw;
        }
    }

    #endregion
}
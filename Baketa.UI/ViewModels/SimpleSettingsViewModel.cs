using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Services;
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
    private readonly Baketa.Application.Services.Translation.TranslationOrchestrationService? _translationOrchestrationService;
    private readonly ISettingsService? _settingsService;
    
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
        ILogger<SimpleSettingsViewModel> logger,
        Baketa.Application.Services.Translation.TranslationOrchestrationService? translationOrchestrationService = null,
        ISettingsService? settingsService = null)
        : base(eventAggregator, logger)
    {
        _translationOrchestrationService = translationOrchestrationService;
        _settingsService = settingsService;
        
        var vmHash = GetHashCode();
        DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] コンストラクタ開始");
        Console.WriteLine($"🔧 [SIMPLE_SETTINGS_INIT] TranslationOrchestrationService: {_translationOrchestrationService?.GetType().Name ?? "NULL"}");
        Console.WriteLine($"🔧 [SIMPLE_SETTINGS_INIT] ISettingsService: {_settingsService?.GetType().Name ?? "NULL"}");
        
        // デバッグファイルログにも記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [SIMPLE_SETTINGS_INIT] TranslationOrchestrationService: {_translationOrchestrationService?.GetType().Name ?? "NULL"}, ISettingsService: {_settingsService?.GetType().Name ?? "NULL"}{Environment.NewLine}");
        }
        catch { }
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [SIMPLE_SETTINGS_INIT] TranslationOrchestrationService: {_translationOrchestrationService?.GetType().Name ?? "NULL"}{Environment.NewLine}");
        }
        catch { }
        
        InitializeCommands();
        InitializeCollections();
        InitializeTranslationStateMonitoring();
        
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

    /// <summary>
    /// 翻訳実行中かどうか
    /// </summary>
    public bool IsTranslationInProgress => _translationOrchestrationService?.IsAnyTranslationActive ?? false;
    
    /// <summary>
    /// 設定変更可能かどうか
    /// </summary>
    public bool CanEditSettings => !IsTranslationInProgress;
    
    /// <summary>
    /// 設定ロック中メッセージ
    /// </summary>
    public string SettingsLockedMessage => "翻訳処理中は設定を変更できません";

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
            // 設定画面内のボタンは翻訳状態に関係なく使用可能にする（UX改善）
            // メインのSetボタンは別途MainOverlayViewModelで制御
            var canApply = this.WhenAnyValue(x => x.HasChanges)
                .ObserveOn(RxApp.MainThreadScheduler);
                
            // キャンセルボタンは常に使用可能
            var canCancel = Observable.Return(true)
                .ObserveOn(RxApp.MainThreadScheduler);
            
            ApplyCommand = ReactiveCommand.CreateFromTask(ExecuteApplyAsync, canApply, outputScheduler: RxApp.MainThreadScheduler);
            CancelCommand = ReactiveCommand.CreateFromTask(ExecuteCancelAsync, canCancel, outputScheduler: RxApp.MainThreadScheduler);
            
            // 🚨 CRITICAL DEBUG: ApplyCommandのCanExecute状態をログ出力
            canApply.Subscribe(canExecute =>
            {
                try
                {
                    System.IO.File.AppendAllText(@"E:\dev\Baketa\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [APPLY_BUTTON_STATE] ApplyCommand.CanExecute={canExecute}, HasChanges={HasChanges}{Environment.NewLine}");
                }
                catch { }
                Console.WriteLine($"🔍 [APPLY_BUTTON_STATE] ApplyCommand.CanExecute={canExecute}, HasChanges={HasChanges}");
            });
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
                        // 🚨 CRITICAL DEBUG: プロパティ変更検出ログ
                        try
                        {
                            System.IO.File.AppendAllText(@"E:\dev\Baketa\debug_app_logs.txt", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [PROPERTY_CHANGED] HasChanges=true設定: UseLocalEngine={UseLocalEngine}, SourceLanguage={SourceLanguage}{Environment.NewLine}");
                        }
                        catch { }
                        
                        HasChanges = true;
                        Console.WriteLine($"🔍 [PROPERTY_CHANGED] HasChanges設定: true, 現在値: UseLocalEngine={UseLocalEngine}, SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}, FontSize={FontSize}");
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

    
    /// <summary>
    /// 翻訳状態監視を初期化します
    /// </summary>
    private void InitializeTranslationStateMonitoring()
    {
        Console.WriteLine($"🔧 [SIMPLE_SETTINGS_MONITORING] TranslationOrchestrationService: {_translationOrchestrationService?.GetType().Name ?? "NULL"}");
        
        if (_translationOrchestrationService == null) 
        {
            Console.WriteLine("⚠️ [SIMPLE_SETTINGS_MONITORING] TranslationOrchestrationServiceがnullです - 翻訳状態監視を無効化");
            return;
        }
        
        Console.WriteLine("🔧 [SIMPLE_SETTINGS_MONITORING] 翻訳状態監視を開始");
        
        // TranslationOrchestrationServiceのIsAnyTranslationActive変更を監視
        _translationOrchestrationService.WhenAnyValue(x => x.IsAnyTranslationActive)
            .Subscribe(isActive =>
            {
                this.RaisePropertyChanged(nameof(IsTranslationInProgress));
                this.RaisePropertyChanged(nameof(CanEditSettings));
                Console.WriteLine($"🔒 [SIMPLE_SETTINGS_STATE] 翻訳状態変更: IsActive={isActive}, CanEditSettings={CanEditSettings}");
            });
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

            // 🚨 CRITICAL DEBUG: ExecuteApplyAsync実行確認
            try
            {
                System.IO.File.AppendAllText(@"E:\dev\Baketa\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [APPLY_BUTTON] ExecuteApplyAsync実行開始: SourceLanguage='{SourceLanguage}'{Environment.NewLine}");
            }
            catch { }

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

            // JSON設定ファイルに保存
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] JSON設定保存開始");
            await SaveCurrentSettingsAsync().ConfigureAwait(false);
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] JSON設定保存完了");
            
            // ISettingsServiceにも翻訳言語設定を保存（TranslationOrchestrationServiceが読み取り可能にする）
            if (_settingsService != null)
            {
                Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ISettingsService設定保存開始");
                try
                {
                    // 🚨 CRITICAL DEBUG: SetValue呼び出し前の確認
                    var beforeValue = _settingsService.GetValue("UI:TranslationLanguage", "設定前");
                    Console.WriteLine($"🔍 [SimpleSettingsViewModel#{vmHash}] SetValue前確認: '{beforeValue}'");
                    
                    try
                    {
                        System.IO.File.AppendAllText(@"E:\dev\Baketa\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [SETVALUE_BEFORE] SetValue呼び出し前: key='UI:TranslationLanguage', value='{SourceLanguage}', beforeValue='{beforeValue}'{Environment.NewLine}");
                    }
                    catch { }
                    
                    // 翻訳言語設定をISettingsServiceに保存（同期メソッド）
                    _settingsService.SetValue("UI:TranslationLanguage", SourceLanguage);
                    Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ISettingsService翻訳言語保存完了: {SourceLanguage}");
                    
                    // 🚨 CRITICAL DEBUG: SetValue呼び出し直後の確認
                    var afterSetValue = _settingsService.GetValue("UI:TranslationLanguage", "設定直後失敗");
                    Console.WriteLine($"🔍 [SimpleSettingsViewModel#{vmHash}] SetValue直後確認: '{afterSetValue}'");
                    
                    try
                    {
                        System.IO.File.AppendAllText(@"E:\dev\Baketa\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [SETVALUE_AFTER] SetValue呼び出し直後: afterSetValue='{afterSetValue}'{Environment.NewLine}");
                    }
                    catch { }
                    
                    // 設定を永続化
                    await _settingsService.SaveAsync().ConfigureAwait(false);
                    Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] ISettingsService設定永続化完了");
                    
                    // 保存確認用に読み取り直し
                    var savedValue = _settingsService.GetValue("UI:TranslationLanguage", "確認失敗");
                    Console.WriteLine($"🔍 [SimpleSettingsViewModel#{vmHash}] 保存確認 - 読み取り結果: '{savedValue}'");
                    
                    // ファイルログにも記録
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [SIMPLE_SETTINGS_SAVE] 保存: '{SourceLanguage}', 確認: '{savedValue}'{Environment.NewLine}");
                    }
                    catch { }
                }
                catch (Exception settingsEx)
                {
                    Console.WriteLine($"💥 [SimpleSettingsViewModel#{vmHash}] ISettingsService設定保存エラー: {settingsEx.Message}");
                    Logger?.LogError(settingsEx, "ISettingsService設定保存失敗");
                }
            }
            else
            {
                Console.WriteLine($"⚠️ [SimpleSettingsViewModel#{vmHash}] ISettingsService が null - 設定保存をスキップ");
            }

            HasChanges = false;
            Logger?.LogInformation("Settings applied successfully");

            // 適用ボタンクリックで設定画面を閉じる（UIスレッドで実行）
            Console.WriteLine($"🔧 [SimpleSettingsViewModel#{vmHash}] 適用完了 - 設定画面を閉じます");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloseRequested?.Invoke();
            });
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
            
            // ISettingsServiceからも翻訳言語設定を読み込み（優先）
            if (_settingsService != null)
            {
                try
                {
                    var translationLanguage = _settingsService.GetValue<string>("UI:TranslationLanguage", "");
                    if (!string.IsNullOrEmpty(translationLanguage))
                    {
                        settings.SourceLanguage = translationLanguage;
                        DebugHelper.Log($"🔧 [SimpleSettingsViewModel#{vmHash}] ISettingsServiceから翻訳言語を上書き: {translationLanguage}");
                    }
                }
                catch (Exception settingsEx)
                {
                    DebugHelper.Log($"⚠️ [SimpleSettingsViewModel#{vmHash}] ISettingsServiceからの読み込み失敗: {settingsEx.Message}");
                }
            }
            
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
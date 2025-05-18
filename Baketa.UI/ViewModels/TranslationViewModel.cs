using System;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Framework.ReactiveUI;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// 翻訳設定画面のビューモデル
    /// </summary>
    internal sealed class TranslationViewModel : Framework.ViewModelBase
    {
        // 翻訳先言語
        private string _targetLanguage = "英語";
        public string TargetLanguage
        {
            get => _targetLanguage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _targetLanguage, value);
        }
        
        // 翻訳エンジン
        private string _translationEngine = "Google";
        public string TranslationEngine
        {
            get => _translationEngine;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationEngine, value);
        }
        
        // APIキー
        private string _apiKey = string.Empty;
        public string ApiKey
        {
            get => _apiKey;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _apiKey, value);
        }
        
        // 翻訳設定
        private bool _autoTranslate = true;
        public bool AutoTranslate
        {
            get => _autoTranslate;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _autoTranslate, value);
        }
        
        private bool _useCache = true;
        public bool UseCache
        {
            get => _useCache;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _useCache, value);
        }
        
        private int _cacheExpiration = 24;
        public int CacheExpiration
        {
            get => _cacheExpiration;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _cacheExpiration, value);
        }
        
        // テスト翻訳テキスト
        private string _testText = "翻訳テスト";
        public string TestText
        {
            get => _testText;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _testText, value);
        }
        
        private string _translatedText = string.Empty;
        public string TranslatedText
        {
            get => _translatedText;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translatedText, value);
        }
        
        // 翻訳エンジン選択肢
        public string[] AvailableEngines { get; } = ["Google", "DeepL", "Microsoft", "ローカルモデル"];
        
        // 言語選択肢
        public string[] AvailableLanguages { get; } = [
            "日本語", "英語", "中国語", "韓国語", "フランス語", "ドイツ語", "スペイン語", "イタリア語", "ロシア語"
        ];
        
        // コマンド
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> TestTranslationCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearCacheCommand { get; }
        
        /// <summary>
        /// 新しいTranslationViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public TranslationViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // コマンドの初期化
            SaveSettingsCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteSaveSettingsAsync);
            TestTranslationCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteTestTranslationAsync);
            ClearCacheCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteClearCacheAsync);
        }
        
        // 設定保存コマンド実行
        private async Task ExecuteSaveSettingsAsync()
        {
            //_logger?.LogInformation("翻訳設定保存コマンドが実行されました");
            
            // 設定保存ロジック
            await PublishEventAsync(new TranslationSettingsChangedEvent(TranslationEngine, TargetLanguage)).ConfigureAwait(true);
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // テスト翻訳コマンド実行
        private async Task ExecuteTestTranslationAsync()
        {
            if (string.IsNullOrWhiteSpace(TestText))
            {
                return;
            }
            
            //_logger?.LogInformation("テスト翻訳コマンドが実行されました");
            
            IsLoading = true;
            
            try
            {
                // テスト翻訳の実行（実際には翻訳サービスを呼び出す）
                await Task.Delay(500).ConfigureAwait(false); // 翻訳中の遅延をシミュレート
                
                // 翻訳エンジンによって結果を変える（デモ用）
                TranslatedText = TranslationEngine switch
                {
                    "Google" => "Translation Test",
                    "DeepL" => "Translation Test (DeepL)",
                    "Microsoft" => "Translation Test (Microsoft)",
                    "ローカルモデル" => "Translation Test (Local)",
                    _ => "Translation Test"
                };
            }
            catch (InvalidOperationException ex)
            {
                //_logger?.LogError(ex, "テスト翻訳中に操作エラーが発生しました");
                ErrorMessage = $"翻訳中に操作エラーが発生しました: {ex.Message}";
            }
            catch (TimeoutException ex)
            {
                //_logger?.LogError(ex, "テスト翻訳がタイムアウトしました");
                ErrorMessage = $"翻訳がタイムアウトしました: {ex.Message}";
            }
            catch (ArgumentException ex)
            {
                //_logger?.LogError(ex, "テスト翻訳の引数が不正です");
                ErrorMessage = $"翻訳のパラメータが不正です: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        // キャッシュクリアコマンド実行
        private async Task ExecuteClearCacheAsync()
        {
            //_logger?.LogInformation("翻訳キャッシュクリアコマンドが実行されました");
            
            // キャッシュクリアロジック
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
    }

# Issue 12-1: 設定データモデルと永続化システムの実装

## 概要
アプリケーション設定を管理するためのデータモデルと、それらを永続化するためのシステムを実装します。これにより、ユーザー設定を保存・読み込みし、アプリケーション全体で一貫して利用できるようになります。

## 目的・理由
設定データモデルと永続化システムは以下の理由で重要です：

1. ユーザー設定を一貫した形式で表現し、アプリケーション全体で統一的にアクセスできるようにする
2. 設定の検証とデフォルト値の提供により、アプリケーションの安定性を確保する
3. 設定変更の通知メカニズムにより、設定に依存するコンポーネントがリアルタイムに反応できる
4. 再起動後も設定を保持するための永続化機能を提供する

## 詳細
- 設定モデルの設計と実装
- 設定カテゴリの定義
- 設定の検証とデフォルト値メカニズムの実装
- 設定永続化システムの実装（JSONファイル形式）

## タスク分解
- [ ] 設定サービスの基本設計
  - [ ] `ISettingsService`インターフェースの設計
  - [ ] `SettingsService`クラスの実装
  - [ ] 設定変更通知メカニズムの設計
- [ ] 設定モデルの実装
  - [ ] 基本設定モデルの設計
  - [ ] 各カテゴリの設定モデルの実装
  - [ ] 設定間の依存関係管理の実装
- [ ] 設定の検証システム
  - [ ] 検証ルールインターフェースの設計
  - [ ] 各種検証ルールの実装
  - [ ] 検証エラー報告メカニズムの実装
- [ ] デフォルト設定管理
  - [ ] デフォルト設定プロバイダーの設計
  - [ ] 設定リセット機能の実装
  - [ ] デフォルト値と現在値の区別メカニズム
- [ ] 永続化システム
  - [ ] ファイルベースの設定ストレージの実装
  - [ ] JSON形式でのシリアライズ/デシリアライズ
  - [ ] 設定のバックアップと復元機能
- [ ] マイグレーションシステム
  - [ ] 設定スキーマのバージョン管理
  - [ ] 旧バージョン設定の変換機能
  - [ ] 互換性チェック機能
- [ ] プロファイル管理
  - [ ] 複数設定プロファイルの管理機能
  - [ ] プロファイル切り替え機能
  - [ ] ゲーム別設定プロファイルの連携
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.Core.Settings
{
    /// <summary>
    /// 設定サービスインターフェース
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// 現在のアプリケーション設定
        /// </summary>
        AppSettings CurrentSettings { get; }
        
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        event EventHandler<SettingsChangedEventArgs> SettingsChanged;
        
        /// <summary>
        /// 設定を読み込みます
        /// </summary>
        /// <returns>読み込みが成功したかどうか</returns>
        Task<bool> LoadSettingsAsync();
        
        /// <summary>
        /// 特定のカテゴリの設定を読み込みます
        /// </summary>
        /// <typeparam name="T">設定の型</typeparam>
        /// <param name="category">カテゴリ名</param>
        /// <returns>設定オブジェクト、存在しない場合はnull</returns>
        Task<T?> LoadSettingsAsync<T>(string category) where T : class, new();
        
        /// <summary>
        /// 設定を保存します
        /// </summary>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SaveSettingsAsync();
        
        /// <summary>
        /// 特定のカテゴリの設定を保存します
        /// </summary>
        /// <typeparam name="T">設定の型</typeparam>
        /// <param name="category">カテゴリ名</param>
        /// <param name="settings">設定オブジェクト</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SaveSettingsAsync<T>(string category, T settings) where T : class;
        
        /// <summary>
        /// 設定を更新します
        /// </summary>
        /// <param name="settings">新しい設定</param>
        /// <param name="raiseEvent">イベントを発生させるかどうか</param>
        /// <returns>更新が成功したかどうか</returns>
        Task<bool> UpdateSettingsAsync(AppSettings settings, bool raiseEvent = true);
        
        /// <summary>
        /// 設定を検証します
        /// </summary>
        /// <param name="settings">検証する設定</param>
        /// <returns>検証結果</returns>
        ValidationResult ValidateSettings(AppSettings settings);
        
        /// <summary>
        /// 特定のカテゴリの設定を検証します
        /// </summary>
        /// <typeparam name="T">設定の型</typeparam>
        /// <param name="category">カテゴリ名</param>
        /// <param name="settings">検証する設定</param>
        /// <returns>検証結果</returns>
        ValidationResult ValidateSettings<T>(string category, T settings) where T : class;
        
        /// <summary>
        /// 設定をデフォルト値にリセットします
        /// </summary>
        /// <returns>リセットが成功したかどうか</returns>
        Task<bool> ResetToDefaultsAsync();
        
        /// <summary>
        /// 特定のカテゴリの設定をデフォルト値にリセットします
        /// </summary>
        /// <param name="category">カテゴリ名</param>
        /// <returns>リセットが成功したかどうか</returns>
        Task<bool> ResetToDefaultsAsync(string category);
        
        /// <summary>
        /// 設定のバックアップを作成します
        /// </summary>
        /// <param name="backupPath">バックアップファイルのパス</param>
        /// <returns>バックアップが成功したかどうか</returns>
        Task<bool> BackupSettingsAsync(string backupPath);
        
        /// <summary>
        /// バックアップから設定を復元します
        /// </summary>
        /// <param name="backupPath">バックアップファイルのパス</param>
        /// <returns>復元が成功したかどうか</returns>
        Task<bool> RestoreSettingsAsync(string backupPath);
        
        /// <summary>
        /// 現在のプロファイル名を取得します
        /// </summary>
        /// <returns>プロファイル名</returns>
        string GetCurrentProfileName();
        
        /// <summary>
        /// 設定プロファイルを切り替えます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>切り替えが成功したかどうか</returns>
        Task<bool> SwitchProfileAsync(string profileName);
        
        /// <summary>
        /// 利用可能なプロファイル名のリストを取得します
        /// </summary>
        /// <returns>プロファイル名のリスト</returns>
        Task<IReadOnlyList<string>> GetAvailableProfilesAsync();
    }
    
    /// <summary>
    /// アプリケーション設定クラス
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// 一般設定
        /// </summary>
        public GeneralSettings General { get; set; } = new GeneralSettings();
        
        /// <summary>
        /// UIテーマ設定
        /// </summary>
        public ThemeSettings Theme { get; set; } = new ThemeSettings();
        
        /// <summary>
        /// ローカライズ設定
        /// </summary>
        public LocalizationSettings Localization { get; set; } = new LocalizationSettings();
        
        /// <summary>
        /// キャプチャ設定
        /// </summary>
        public CaptureSettings Capture { get; set; } = new CaptureSettings();
        
        /// <summary>
        /// OCR設定
        /// </summary>
        public OcrSettings Ocr { get; set; } = new OcrSettings();
        
        /// <summary>
        /// 翻訳設定
        /// </summary>
        public TranslationSettings Translation { get; set; } = new TranslationSettings();
        
        /// <summary>
        /// オーバーレイ設定
        /// </summary>
        public OverlaySettings Overlay { get; set; } = new OverlaySettings();
        
        /// <summary>
        /// ホットキー設定
        /// </summary>
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
        
        /// <summary>
        /// 拡張設定
        /// </summary>
        public AdvancedSettings Advanced { get; set; } = new AdvancedSettings();
        
        /// <summary>
        /// ゲームプロファイル設定
        /// </summary>
        public Dictionary<string, GameProfileSettings> GameProfiles { get; set; } = new Dictionary<string, GameProfileSettings>();
    }
    
    /// <summary>
    /// 一般設定クラス
    /// </summary>
    public class GeneralSettings
    {
        /// <summary>
        /// 起動時に自動的に開始
        /// </summary>
        public bool AutoStartOnLaunch { get; set; } = false;
        
        /// <summary>
        /// システム起動時に自動的に起動
        /// </summary>
        public bool AutoStartWithSystem { get; set; } = false;
        
        /// <summary>
        /// 最小化時にタスクトレイに格納
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;
        
        /// <summary>
        /// 閉じるボタンで最小化
        /// </summary>
        public bool CloseButtonMinimizes { get; set; } = true;
        
        /// <summary>
        /// 自動アップデートを有効化
        /// </summary>
        public bool EnableAutoUpdate { get; set; } = true;
        
        /// <summary>
        /// 診断情報の送信を許可
        /// </summary>
        public bool AllowTelemetry { get; set; } = false;
        
        /// <summary>
        /// ログレベル
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Information;
        
        /// <summary>
        /// ログファイルの保持日数
        /// </summary>
        public int LogRetentionDays { get; set; } = 7;
    }
    
    /// <summary>
    /// テーマ設定クラス
    /// </summary>
    public class ThemeSettings
    {
        /// <summary>
        /// テーマ名
        /// </summary>
        public string ThemeName { get; set; } = "Dark";
        
        /// <summary>
        /// システムテーマに従う
        /// </summary>
        public bool FollowSystemTheme { get; set; } = true;
        
        /// <summary>
        /// カスタムアクセントカラー
        /// </summary>
        public string AccentColor { get; set; } = "#00A0FF";
        
        /// <summary>
        /// カスタムテーマパス
        /// </summary>
        public string? CustomThemePath { get; set; }
        
        /// <summary>
        /// フォントファミリー
        /// </summary>
        public string FontFamily { get; set; } = "Yu Gothic UI";
        
        /// <summary>
        /// フォントサイズ
        /// </summary>
        public double FontSize { get; set; } = 12.0;
    }
    
    /// <summary>
    /// ローカライズ設定クラス
    /// </summary>
    public class LocalizationSettings
    {
        /// <summary>
        /// 言語コード
        /// </summary>
        public string LanguageCode { get; set; } = "ja-JP";
        
        /// <summary>
        /// システム言語に従う
        /// </summary>
        public bool FollowSystemLanguage { get; set; } = true;
        
        /// <summary>
        /// 日付時刻形式
        /// </summary>
        public string DateTimeFormat { get; set; } = "yyyy/MM/dd HH:mm:ss";
        
        /// <summary>
        /// 数値形式
        /// </summary>
        public string NumberFormat { get; set; } = "0,0.00";
    }
    
    /// <summary>
    /// キャプチャ設定クラス
    /// </summary>
    public class CaptureSettings
    {
        /// <summary>
        /// キャプチャメソッド
        /// </summary>
        public CaptureMethod Method { get; set; } = CaptureMethod.GDI;
        
        /// <summary>
        /// キャプチャ間隔（ミリ秒）
        /// </summary>
        public int Interval { get; set; } = 500;
        
        /// <summary>
        /// 差分検出を有効化
        /// </summary>
        public bool EnableDifferenceDetection { get; set; } = true;
        
        /// <summary>
        /// 差分閾値
        /// </summary>
        public double DifferenceThreshold { get; set; } = 0.05;
        
        /// <summary>
        /// キャプチャ領域
        /// </summary>
        public CaptureRegion Region { get; set; } = CaptureRegion.FullWindow;
        
        /// <summary>
        /// カスタム領域（Region = Customの場合）
        /// </summary>
        public Rect? CustomRegion { get; set; }
        
        /// <summary>
        /// FPS制限
        /// </summary>
        public int FpsLimit { get; set; } = 10;
    }
    
    /// <summary>
    /// OCR設定クラス
    /// </summary>
    public class OcrSettings
    {
        /// <summary>
        /// OCRエンジン
        /// </summary>
        public string Engine { get; set; } = "PaddleOCR";
        
        /// <summary>
        /// 認識言語
        /// </summary>
        public string Language { get; set; } = "ja";
        
        /// <summary>
        /// 信頼度閾値
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.7f;
        
        /// <summary>
        /// 前処理を有効化
        /// </summary>
        public bool EnablePreprocessing { get; set; } = true;
        
        /// <summary>
        /// 前処理パイプライン名
        /// </summary>
        public string PreprocessingPipeline { get; set; } = "Default";
        
        /// <summary>
        /// テキスト領域検出を有効化
        /// </summary>
        public bool EnableTextRegionDetection { get; set; } = true;
        
        /// <summary>
        /// モデルパス
        /// </summary>
        public string? ModelPath { get; set; }
        
        /// <summary>
        /// GPUアクセラレーションを有効化
        /// </summary>
        public bool EnableGpuAcceleration { get; set; } = false;
    }
    
    /// <summary>
    /// 翻訳設定クラス
    /// </summary>
    public class TranslationSettings
    {
        /// <summary>
        /// 翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = "GoogleTranslate";
        
        /// <summary>
        /// API認証情報
        /// </summary>
        public Dictionary<string, string> ApiCredentials { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// 元言語
        /// </summary>
        public string SourceLanguage { get; set; } = "ja";
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public string TargetLanguage { get; set; } = "en";
        
        /// <summary>
        /// 自動言語検出を有効化
        /// </summary>
        public bool EnableAutoDetection { get; set; } = true;
        
        /// <summary>
        /// キャッシュを有効化
        /// </summary>
        public bool EnableCache { get; set; } = true;
        
        /// <summary>
        /// キャッシュサイズ制限（エントリー数）
        /// </summary>
        public int CacheLimit { get; set; } = 10000;
        
        /// <summary>
        /// 自動翻訳を有効化
        /// </summary>
        public bool EnableAutoTranslation { get; set; } = true;
        
        /// <summary>
        /// 翻訳でアスタリスク（*）をマスクするかどうか
        /// </summary>
        public bool MaskAsterisks { get; set; } = true;
    }
    
    /// <summary>
    /// オーバーレイ設定クラス
    /// </summary>
    public class OverlaySettings
    {
        /// <summary>
        /// 背景色
        /// </summary>
        public string BackgroundColor { get; set; } = "#B0000000";
        
        /// <summary>
        /// 背景の不透明度
        /// </summary>
        public double BackgroundOpacity { get; set; } = 0.8;
        
        /// <summary>
        /// テキスト色
        /// </summary>
        public string TextColor { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// フォントサイズ
        /// </summary>
        public double FontSize { get; set; } = 16.0;
        
        /// <summary>
        /// フォントウェイト
        /// </summary>
        public string FontWeight { get; set; } = "Normal";
        
        /// <summary>
        /// 表示位置モード
        /// </summary>
        public PositionMode PositionMode { get; set; } = PositionMode.TextRegionBased;
        
        /// <summary>
        /// 固定位置（PositionMode = Fixedの場合）
        /// </summary>
        public Point? FixedPosition { get; set; }
        
        /// <summary>
        /// 表示アニメーション
        /// </summary>
        public OverlayAnimation Animation { get; set; } = OverlayAnimation.FadeIn;
        
        /// <summary>
        /// 自動非表示を有効化
        /// </summary>
        public bool EnableAutoHide { get; set; } = false;
        
        /// <summary>
        /// 自動非表示までの時間（秒）
        /// </summary>
        public int AutoHideDelay { get; set; } = 5;
        
        /// <summary>
        /// 表示効果
        /// </summary>
        public OverlayEffect Effect { get; set; } = OverlayEffect.None;
    }
    
    /// <summary>
    /// ホットキー設定クラス
    /// </summary>
    public class HotkeySettings
    {
        /// <summary>
        /// ホットキーを有効化
        /// </summary>
        public bool EnableHotkeys { get; set; } = true;
        
        /// <summary>
        /// キャプチャ開始/停止ホットキー
        /// </summary>
        public string ToggleCaptureHotkey { get; set; } = "Ctrl+F10";
        
        /// <summary>
        /// オーバーレイ表示/非表示ホットキー
        /// </summary>
        public string ToggleOverlayHotkey { get; set; } = "Ctrl+F9";
        
        /// <summary>
        /// メインウィンドウ表示/非表示ホットキー
        /// </summary>
        public string ToggleMainWindowHotkey { get; set; } = "Ctrl+F11";
        
        /// <summary>
        /// 選択テキスト翻訳ホットキー
        /// </summary>
        public string TranslateSelectionHotkey { get; set; } = "Ctrl+Shift+T";
    }
    
    /// <summary>
    /// 拡張設定クラス
    /// </summary>
    public class AdvancedSettings
    {
        /// <summary>
        /// デバッグモードを有効化
        /// </summary>
        public bool EnableDebugMode { get; set; } = false;
        
        /// <summary>
        /// パフォーマンス情報を表示
        /// </summary>
        public bool ShowPerformanceInfo { get; set; } = false;
        
        /// <summary>
        /// 実験的機能を有効化
        /// </summary>
        public bool EnableExperimentalFeatures { get; set; } = false;
        
        /// <summary>
        /// スレッドプール設定
        /// </summary>
        public int MaxThreads { get; set; } = 4;
        
        /// <summary>
        /// メモリ使用量制限（MB）
        /// </summary>
        public int MemoryLimit { get; set; } = 512;
    }
    
    /// <summary>
    /// ゲームプロファイル設定クラス
    /// </summary>
    public class GameProfileSettings
    {
        /// <summary>
        /// プロファイル名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// ゲーム実行ファイルパス
        /// </summary>
        public string ExecutablePath { get; set; } = string.Empty;
        
        /// <summary>
        /// ウィンドウタイトルパターン
        /// </summary>
        public string WindowTitlePattern { get; set; } = string.Empty;
        
        /// <summary>
        /// キャプチャ設定
        /// </summary>
        public CaptureSettings? Capture { get; set; }
        
        /// <summary>
        /// OCR設定
        /// </summary>
        public OcrSettings? Ocr { get; set; }
        
        /// <summary>
        /// 翻訳設定
        /// </summary>
        public TranslationSettings? Translation { get; set; }
        
        /// <summary>
        /// オーバーレイ設定
        /// </summary>
        public OverlaySettings? Overlay { get; set; }
    }
    
    /// <summary>
    /// キャプチャメソッド列挙型
    /// </summary>
    public enum CaptureMethod
    {
        /// <summary>
        /// GDI+
        /// </summary>
        GDI,
        
        /// <summary>
        /// Direct3D
        /// </summary>
        Direct3D,
        
        /// <summary>
        /// DWM（Desktop Window Manager）
        /// </summary>
        DWM,
        
        /// <summary>
        /// 自動選択
        /// </summary>
        Auto
    }
    
    /// <summary>
    /// キャプチャ領域列挙型
    /// </summary>
    public enum CaptureRegion
    {
        /// <summary>
        /// ウィンドウ全体
        /// </summary>
        FullWindow,
        
        /// <summary>
        /// クライアント領域のみ
        /// </summary>
        ClientAreaOnly,
        
        /// <summary>
        /// カスタム領域
        /// </summary>
        Custom
    }
    
    /// <summary>
    /// 位置モード列挙型
    /// </summary>
    public enum PositionMode
    {
        /// <summary>
        /// テキスト領域ベース
        /// </summary>
        TextRegionBased,
        
        /// <summary>
        /// 固定位置
        /// </summary>
        Fixed,
        
        /// <summary>
        /// スマート配置
        /// </summary>
        Smart,
        
        /// <summary>
        /// フォローモード
        /// </summary>
        Follow
    }
    
    /// <summary>
    /// オーバーレイアニメーション列挙型
    /// </summary>
    public enum OverlayAnimation
    {
        /// <summary>
        /// なし
        /// </summary>
        None,
        
        /// <summary>
        /// フェードイン
        /// </summary>
        FadeIn,
        
        /// <summary>
        /// スライドイン
        /// </summary>
        SlideIn,
        
        /// <summary>
        /// タイピング
        /// </summary>
        Typing
    }
    
    /// <summary>
    /// オーバーレイ効果列挙型
    /// </summary>
    public enum OverlayEffect
    {
        /// <summary>
        /// なし
        /// </summary>
        None,
        
        /// <summary>
        /// ぼかし
        /// </summary>
        Blur,
        
        /// <summary>
        /// 影
        /// </summary>
        Shadow,
        
        /// <summary>
        /// グロー
        /// </summary>
        Glow
    }
    
    /// <summary>
    /// ログレベル列挙型
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 詳細
        /// </summary>
        Trace,
        
        /// <summary>
        /// デバッグ
        /// </summary>
        Debug,
        
        /// <summary>
        /// 情報
        /// </summary>
        Information,
        
        /// <summary>
        /// 警告
        /// </summary>
        Warning,
        
        /// <summary>
        /// エラー
        /// </summary>
        Error,
        
        /// <summary>
        /// 致命的
        /// </summary>
        Critical
    }
}
```

## 設定永続化システム実装例
```csharp
namespace Baketa.Core.Settings
{
    /// <summary>
    /// 設定サービス実装クラス
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFolder;
        private readonly string _settingsFile;
        private readonly string _profilesFolder;
        private readonly IDefaultSettingsProvider _defaultSettingsProvider;
        private readonly ISettingsValidator _settingsValidator;
        private readonly ILogger? _logger;
        private AppSettings _currentSettings;
        private string _currentProfileName = "Default";
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        
        /// <summary>
        /// 新しい設定サービスを初期化します
        /// </summary>
        /// <param name="defaultSettingsProvider">デフォルト設定プロバイダー</param>
        /// <param name="settingsValidator">設定バリデーター</param>
        /// <param name="logger">ロガー</param>
        public SettingsService(
            IDefaultSettingsProvider defaultSettingsProvider,
            ISettingsValidator settingsValidator,
            ILogger? logger = null)
        {
            _defaultSettingsProvider = defaultSettingsProvider ?? throw new ArgumentNullException(nameof(defaultSettingsProvider));
            _settingsValidator = settingsValidator ?? throw new ArgumentNullException(nameof(settingsValidator));
            _logger = logger;
            
            // 設定ファイルのパスを設定
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "Baketa");
            _settingsFolder = Path.Combine(appFolder, "Settings");
            _settingsFile = Path.Combine(_settingsFolder, "settings.json");
            _profilesFolder = Path.Combine(_settingsFolder, "Profiles");
            
            // JSON設定
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            // デフォルト設定をコピー
            _currentSettings = _defaultSettingsProvider.GetDefaultSettings();
            
            // フォルダが存在しない場合は作成
            EnsureDirectoriesExist();
            
            _logger?.LogInformation("設定サービスが初期化されました。設定ファイル: {SettingsFile}", _settingsFile);
        }
        
        /// <inheritdoc />
        public AppSettings CurrentSettings => _currentSettings;
        
        /// <inheritdoc />
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        
        /// <inheritdoc />
        public async Task<bool> LoadSettingsAsync()
        {
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                string profileSettingsFile = GetProfileSettingsFilePath(_currentProfileName);
                string fileToLoad = File.Exists(profileSettingsFile) ? profileSettingsFile : _settingsFile;
                
                if (!File.Exists(fileToLoad))
                {
                    _logger?.LogInformation("設定ファイルが見つからないため、デフォルト設定を使用します: {File}", fileToLoad);
                    _currentSettings = _defaultSettingsProvider.GetDefaultSettings();
                    return true;
                }
                
                try
                {
                    // ファイルから設定を読み込み
                    string json = await File.ReadAllTextAsync(fileToLoad);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    
                    if (loadedSettings == null)
                    {
                        _logger?.LogWarning("設定ファイルを解析できませんでした: {File}", fileToLoad);
                        _currentSettings = _defaultSettingsProvider.GetDefaultSettings();
                        return false;
                    }
                    
                    // デフォルト値で不足している項目を補完
                    MergeWithDefaultSettings(loadedSettings);
                    
                    // 設定を検証
                    var validationResult = ValidateSettings(loadedSettings);
                    if (!validationResult.IsValid)
                    {
                        _logger?.LogWarning("設定ファイルのバリデーションに失敗しました: {File}\n{Errors}",
                            fileToLoad, string.Join("\n", validationResult.Errors));
                    }
                    
                    // 現在の設定を更新
                    _currentSettings = loadedSettings;
                    
                    _logger?.LogInformation("設定を正常に読み込みました: {File}", fileToLoad);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "設定ファイルの読み込み中にエラーが発生しました: {File}", fileToLoad);
                    _currentSettings = _defaultSettingsProvider.GetDefaultSettings();
                    return false;
                }
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<T?> LoadSettingsAsync<T>(string category) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("カテゴリ名が空です。", nameof(category));
                
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                string categoryFile = Path.Combine(_settingsFolder, $"{category}.json");
                string profileCategoryFile = Path.Combine(_profilesFolder, _currentProfileName, $"{category}.json");
                string fileToLoad = File.Exists(profileCategoryFile) ? profileCategoryFile : categoryFile;
                
                if (!File.Exists(fileToLoad))
                {
                    _logger?.LogInformation("カテゴリ設定ファイルが見つかりません: {File}", fileToLoad);
                    return null;
                }
                
                try
                {
                    // ファイルから設定を読み込み
                    string json = await File.ReadAllTextAsync(fileToLoad);
                    var settings = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                    
                    if (settings == null)
                    {
                        _logger?.LogWarning("カテゴリ設定ファイルを解析できませんでした: {File}", fileToLoad);
                        return new T();
                    }
                    
                    _logger?.LogInformation("カテゴリ設定を正常に読み込みました: {Category} from {File}", category, fileToLoad);
                    return settings;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "カテゴリ設定ファイルの読み込み中にエラーが発生しました: {File}", fileToLoad);
                    return new T();
                }
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> SaveSettingsAsync()
        {
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                string profileSettingsFile = GetProfileSettingsFilePath(_currentProfileName);
                string fileToSave = _currentProfileName == "Default" ? _settingsFile : profileSettingsFile;
                
                try
                {
                    // ディレクトリが存在するか確認
                    string? directory = Path.GetDirectoryName(fileToSave);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // 設定をJSONに変換
                    string json = JsonSerializer.Serialize(_currentSettings, _jsonOptions);
                    
                    // 一時ファイルに書き込み
                    string tempFile = fileToSave + ".tmp";
                    await File.WriteAllTextAsync(tempFile, json);
                    
                    // 既存のファイルをバックアップ
                    if (File.Exists(fileToSave))
                    {
                        string backupFile = fileToSave + ".bak";
                        if (File.Exists(backupFile))
                        {
                            File.Delete(backupFile);
                        }
                        File.Move(fileToSave, backupFile);
                    }
                    
                    // 一時ファイルを本来のファイルにリネーム
                    File.Move(tempFile, fileToSave);
                    
                    _logger?.LogInformation("設定を正常に保存しました: {File}", fileToSave);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "設定ファイルの保存中にエラーが発生しました: {File}", fileToSave);
                    return false;
                }
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> SaveSettingsAsync<T>(string category, T settings) where T : class
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("カテゴリ名が空です。", nameof(category));
                
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                string categoryFile = GetCategorySettingsFilePath(category, _currentProfileName);
                
                try
                {
                    // ディレクトリが存在するか確認
                    string? directory = Path.GetDirectoryName(categoryFile);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // 設定をJSONに変換
                    string json = JsonSerializer.Serialize(settings, _jsonOptions);
                    
                    // ファイルに書き込み
                    await File.WriteAllTextAsync(categoryFile, json);
                    
                    _logger?.LogInformation("カテゴリ設定を正常に保存しました: {Category} to {File}", category, categoryFile);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "カテゴリ設定ファイルの保存中にエラーが発生しました: {File}", categoryFile);
                    return false;
                }
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> UpdateSettingsAsync(AppSettings settings, bool raiseEvent = true)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            // 設定を検証
            var validationResult = ValidateSettings(settings);
            if (!validationResult.IsValid)
            {
                _logger?.LogWarning("設定の更新に失敗しました: バリデーションエラー\n{Errors}",
                    string.Join("\n", validationResult.Errors));
                return false;
            }
            
            // 設定を更新
            var oldSettings = _currentSettings;
            _currentSettings = settings;
            
            // 変更イベントを発生
            if (raiseEvent)
            {
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(oldSettings, settings));
            }
            
            // 設定を保存
            return await SaveSettingsAsync();
        }
        
        /// <inheritdoc />
        public ValidationResult ValidateSettings(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            return _settingsValidator.Validate(settings);
        }
        
        /// <inheritdoc />
        public ValidationResult ValidateSettings<T>(string category, T settings) where T : class
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("カテゴリ名が空です。", nameof(category));
                
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            return _settingsValidator.Validate(category, settings);
        }
        
        /// <inheritdoc />
        public async Task<bool> ResetToDefaultsAsync()
        {
            var defaultSettings = _defaultSettingsProvider.GetDefaultSettings();
            return await UpdateSettingsAsync(defaultSettings);
        }
        
        /// <inheritdoc />
        public async Task<bool> ResetToDefaultsAsync(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("カテゴリ名が空です。", nameof(category));
                
            // デフォルト設定オブジェクトからカテゴリを取得
            var defaultSettings = _defaultSettingsProvider.GetDefaultSettings();
            var categoryProperty = typeof(AppSettings).GetProperty(category);
            
            if (categoryProperty == null)
            {
                _logger?.LogWarning("指定されたカテゴリが見つかりません: {Category}", category);
                return false;
            }
            
            // デフォルト設定からカテゴリ設定を取得
            var defaultCategorySettings = categoryProperty.GetValue(defaultSettings);
            if (defaultCategorySettings == null)
            {
                _logger?.LogWarning("デフォルト設定からカテゴリ設定を取得できませんでした: {Category}", category);
                return false;
            }
            
            // 現在の設定のカテゴリを更新
            categoryProperty.SetValue(_currentSettings, defaultCategorySettings);
            
            // 変更イベントを発生
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(_currentSettings, _currentSettings, category));
            
            // 設定を保存
            return await SaveSettingsAsync();
        }
        
        /// <inheritdoc />
        public async Task<bool> BackupSettingsAsync(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                throw new ArgumentException("バックアップパスが空です。", nameof(backupPath));
                
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                try
                {
                    // ディレクトリが存在するか確認
                    string? directory = Path.GetDirectoryName(backupPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // 設定をJSONに変換
                    string json = JsonSerializer.Serialize(_currentSettings, _jsonOptions);
                    
                    // バックアップファイルに書き込み
                    await File.WriteAllTextAsync(backupPath, json);
                    
                    _logger?.LogInformation("設定のバックアップを作成しました: {File}", backupPath);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "設定のバックアップ作成中にエラーが発生しました: {File}", backupPath);
                    return false;
                }
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> RestoreSettingsAsync(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                throw new ArgumentException("バックアップパスが空です。", nameof(backupPath));
                
            if (!File.Exists(backupPath))
            {
                _logger?.LogWarning("バックアップファイルが見つかりません: {File}", backupPath);
                return false;
            }
            
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                try
                {
                    // バックアップファイルから設定を読み込み
                    string json = await File.ReadAllTextAsync(backupPath);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    
                    if (loadedSettings == null)
                    {
                        _logger?.LogWarning("バックアップファイルを解析できませんでした: {File}", backupPath);
                        return false;
                    }
                    
                    // デフォルト値で不足している項目を補完
                    MergeWithDefaultSettings(loadedSettings);
                    
                    // 設定を検証
                    var validationResult = ValidateSettings(loadedSettings);
                    if (!validationResult.IsValid)
                    {
                        _logger?.LogWarning("バックアップファイルのバリデーションに失敗しました: {File}\n{Errors}",
                            backupPath, string.Join("\n", validationResult.Errors));
                    }
                    
                    // 現在の設定を更新
                    var oldSettings = _currentSettings;
                    _currentSettings = loadedSettings;
                    
                    // 変更イベントを発生
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(oldSettings, loadedSettings));
                    
                    // 設定を保存
                    await SaveSettingsAsync();
                    
                    _logger?.LogInformation("バックアップから設定を復元しました: {File}", backupPath);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "バックアップからの設定復元中にエラーが発生しました: {File}", backupPath);
                    return false;
                }
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        /// <inheritdoc />
        public string GetCurrentProfileName()
        {
            return _currentProfileName;
        }
        
        /// <inheritdoc />
        public async Task<bool> SwitchProfileAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ArgumentException("プロファイル名が空です。", nameof(profileName));
                
            if (_currentProfileName == profileName)
                return true;
                
            // 現在の設定を保存
            await SaveSettingsAsync();
            
            // プロファイルを切り替え
            _currentProfileName = profileName;
            
            // 新しいプロファイルの設定を読み込み
            return await LoadSettingsAsync();
        }
        
        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetAvailableProfilesAsync()
        {
            var profiles = new List<string> { "Default" };
            
            // プロファイルフォルダが存在しない場合は作成
            if (!Directory.Exists(_profilesFolder))
            {
                Directory.CreateDirectory(_profilesFolder);
                return profiles;
            }
            
            try
            {
                // プロファイルフォルダ内のサブディレクトリを取得
                var directories = Directory.GetDirectories(_profilesFolder);
                
                foreach (var directory in directories)
                {
                    string profileName = Path.GetFileName(directory);
                    if (!string.IsNullOrEmpty(profileName) && !profiles.Contains(profileName))
                    {
                        profiles.Add(profileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "プロファイル一覧の取得中にエラーが発生しました。");
            }
            
            return profiles;
        }
        
        /// <summary>
        /// 必要なディレクトリが存在することを確認します
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                {
                    Directory.CreateDirectory(_settingsFolder);
                }
                
                if (!Directory.Exists(_profilesFolder))
                {
                    Directory.CreateDirectory(_profilesFolder);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "設定ディレクトリの作成中にエラーが発生しました。");
            }
        }
        
        /// <summary>
        /// プロファイルの設定ファイルパスを取得します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>設定ファイルパス</returns>
        private string GetProfileSettingsFilePath(string profileName)
        {
            if (profileName == "Default")
            {
                return _settingsFile;
            }
            
            return Path.Combine(_profilesFolder, profileName, "settings.json");
        }
        
        /// <summary>
        /// カテゴリの設定ファイルパスを取得します
        /// </summary>
        /// <param name="category">カテゴリ名</param>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>設定ファイルパス</returns>
        private string GetCategorySettingsFilePath(string category, string profileName)
        {
            if (profileName == "Default")
            {
                return Path.Combine(_settingsFolder, $"{category}.json");
            }
            
            return Path.Combine(_profilesFolder, profileName, $"{category}.json");
        }
        
        /// <summary>
        /// 読み込んだ設定にデフォルト値が不足している場合は補完します
        /// </summary>
        /// <param name="settings">補完する設定</param>
        private void MergeWithDefaultSettings(AppSettings settings)
        {
            var defaultSettings = _defaultSettingsProvider.GetDefaultSettings();
            
            // 不足している項目にデフォルト値を設定
            // 実装は省略
        }
    }
    
    /// <summary>
    /// 設定変更イベント引数
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 変更前の設定
        /// </summary>
        public AppSettings OldSettings { get; }
        
        /// <summary>
        /// 変更後の設定
        /// </summary>
        public AppSettings NewSettings { get; }
        
        /// <summary>
        /// 変更されたカテゴリ（特定できる場合）
        /// </summary>
        public string? ChangedCategory { get; }
        
        /// <summary>
        /// 新しい設定変更イベント引数を初期化します
        /// </summary>
        /// <param name="oldSettings">変更前の設定</param>
        /// <param name="newSettings">変更後の設定</param>
        /// <param name="changedCategory">変更されたカテゴリ</param>
        public SettingsChangedEventArgs(
            AppSettings oldSettings,
            AppSettings newSettings,
            string? changedCategory = null)
        {
            OldSettings = oldSettings;
            NewSettings = newSettings;
            ChangedCategory = changedCategory;
        }
    }
    
    /// <summary>
    /// 検証結果クラス
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 検証が成功したかどうか
        /// </summary>
        public bool IsValid => Errors.Count == 0;
        
        /// <summary>
        /// エラーメッセージのリスト
        /// </summary>
        public List<string> Errors { get; } = new List<string>();
        
        /// <summary>
        /// エラーを追加します
        /// </summary>
        /// <param name="error">エラーメッセージ</param>
        public void AddError(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Errors.Add(error);
            }
        }
    }
}
```

## 実装上の注意点
- 設定のシリアライズ/デシリアライズ処理の適切なエラーハンドリング
- ファイルI/O操作の安全な実装と競合状態の回避
- 設定構造の将来的な変更に対する柔軟性と後方互換性の確保
- 設定変更通知のパフォーマンス最適化と効率的なイベント発行
- パスワードやAPIキーなどの機密情報の適切な保護
- 設定の検証ロジックの適切な分離と拡張性の確保
- 複数のプロファイル間での設定共有と上書きのメカニズム
- 大量の設定データを効率的に処理するためのメモリ最適化

## 関連Issue/参考
- 親Issue: #12 設定画面
- 関連Issue: #13 OCR設定UIとプロファイル管理
- 関連Issue: #10-5 テーマと国際化対応の基盤実装
- 参照: E:\dev\Baketa\docs\3-architecture\data\settings-model.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.3 Task.Runの適切な使用)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (2.1 Null許容性の明示的な宣言)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: medium`
- `component: ui`

# Issue 12-3: 設定検証と適用システムの実装

## 概要
アプリケーション設定の値を検証し、それらをアプリケーション全体に適用するシステムを実装します。これにより、不適切な設定値によるアプリケーションの不安定化を防ぎ、設定変更の即時反映を可能にします。

## 目的・理由
設定検証と適用システムは以下の理由で重要です：

1. 不適切または無効な設定値がアプリケーションに適用されるのを防止する
2. 設定変更が適切にアプリケーション全体に伝播されるようにする
3. 設定変更のロールバックや取り消し機能を提供する
4. 設定の依存関係を管理し、一貫性を確保する

## 詳細
- 設定値の検証ルールとバリデーターの実装
- 設定変更イベントのサブスクリプション管理
- サービスごとの設定適用ハンドラーの実装
- 設定変更のロールバックメカニズムの実装

## タスク分解
- [ ] 設定検証システム
  - [ ] `ISettingsValidator`インターフェースの完全実装
  - [ ] 基本検証ルールの実装（数値範囲、文字列長、必須値など）
  - [ ] カスタム検証ルールの実装（依存関係、互換性チェックなど）
  - [ ] 検証結果モデルの拡張（フィールド単位エラー、警告レベルなど）
- [ ] イベント通知メカニズム
  - [ ] `SettingsChangedEvent`クラスの強化
  - [ ] 粒度の細かい変更通知の実装（プロパティ単位の変更追跡）
  - [ ] 遅延通知（バッチ処理）と即時通知のサポート
  - [ ] 優先順位ベースのイベント処理システム
- [ ] 設定適用ハンドラー
  - [ ] `ISettingsHandler`インターフェースの設計と実装
  - [ ] コアサブシステム（キャプチャ、OCR、翻訳など）向けハンドラーの実装
  - [ ] UIサブシステム向けハンドラーの実装
  - [ ] 設定変更に対する動的応答機能
- [ ] 変更適用システム
  - [ ] 設定変更の検出と差分計算
  - [ ] 各サブシステムへの変更適用ロジック
  - [ ] 依存関係ベースの適用順序の管理
  - [ ] エラー発生時のロールバックと回復メカニズム
- [ ] 設定変更記録
  - [ ] 変更履歴の記録と管理
  - [ ] 過去の設定状態の復元機能
  - [ ] 設定変更のログ記録とトレーサビリティ
- [ ] 単体テスト
  - [ ] バリデーターのテスト
  - [ ] イベント通知のテスト
  - [ ] 適用ハンドラーのテスト
  - [ ] リカバリーとロールバックのテスト

## インターフェース設計案
```csharp
namespace Baketa.Core.Settings
{
    /// <summary>
    /// 設定バリデーターインターフェース
    /// </summary>
    public interface ISettingsValidator
    {
        /// <summary>
        /// 設定を検証します
        /// </summary>
        /// <param name="settings">検証する設定</param>
        /// <returns>検証結果</returns>
        ValidationResult Validate(AppSettings settings);
        
        /// <summary>
        /// 特定のカテゴリの設定を検証します
        /// </summary>
        /// <typeparam name="T">設定の型</typeparam>
        /// <param name="category">カテゴリ名</param>
        /// <param name="settings">検証する設定</param>
        /// <returns>検証結果</returns>
        ValidationResult Validate<T>(string category, T settings) where T : class;
        
        /// <summary>
        /// 検証ルールを追加します
        /// </summary>
        /// <param name="rule">検証ルール</param>
        void AddRule(IValidationRule rule);
        
        /// <summary>
        /// 特定のカテゴリに対する検証ルールを追加します
        /// </summary>
        /// <param name="category">カテゴリ名</param>
        /// <param name="rule">検証ルール</param>
        void AddRule(string category, IValidationRule rule);
    }
    
    /// <summary>
    /// 検証ルールインターフェース
    /// </summary>
    public interface IValidationRule
    {
        /// <summary>
        /// ルールの対象となるプロパティパス
        /// </summary>
        string PropertyPath { get; }
        
        /// <summary>
        /// ルールの優先度
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// 値を検証します
        /// </summary>
        /// <param name="value">検証する値</param>
        /// <param name="context">検証コンテキスト</param>
        /// <returns>検証結果</returns>
        ValidationResult Validate(object? value, ValidationContext context);
    }
    
    /// <summary>
    /// 検証コンテキストクラス
    /// </summary>
    public class ValidationContext
    {
        /// <summary>
        /// 全体の設定オブジェクト
        /// </summary>
        public AppSettings Settings { get; }
        
        /// <summary>
        /// カテゴリ名
        /// </summary>
        public string Category { get; }
        
        /// <summary>
        /// プロパティパス
        /// </summary>
        public string PropertyPath { get; }
        
        /// <summary>
        /// 検証の深さ
        /// </summary>
        public int Depth { get; internal set; }
        
        /// <summary>
        /// 親コンテキスト
        /// </summary>
        public ValidationContext? Parent { get; }
        
        // コンストラクタや他のメソッド
    }
    
    /// <summary>
    /// 検証エラーレベル列挙型
    /// </summary>
    public enum ValidationErrorLevel
    {
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
        Error
    }
    
    /// <summary>
    /// 検証エラーアイテムクラス
    /// </summary>
    public class ValidationError
    {
        /// <summary>
        /// エラーレベル
        /// </summary>
        public ValidationErrorLevel Level { get; }
        
        /// <summary>
        /// プロパティパス
        /// </summary>
        public string PropertyPath { get; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string Message { get; }
        
        /// <summary>
        /// エラーコード
        /// </summary>
        public string? ErrorCode { get; }
        
        // コンストラクタや他のメソッド
    }
    
    /// <summary>
    /// 設定ハンドラーインターフェース
    /// </summary>
    public interface ISettingsHandler
    {
        /// <summary>
        /// ハンドラーの優先度
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// 担当するカテゴリ
        /// </summary>
        IReadOnlyList<string> HandledCategories { get; }
        
        /// <summary>
        /// 設定変更を適用します
        /// </summary>
        /// <param name="oldSettings">変更前の設定</param>
        /// <param name="newSettings">変更後の設定</param>
        /// <param name="changedCategory">変更されたカテゴリ</param>
        /// <returns>適用が成功したかどうか</returns>
        Task<bool> ApplySettingsAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null);
        
        /// <summary>
        /// 設定変更をロールバックします
        /// </summary>
        /// <param name="currentSettings">現在の設定</param>
        /// <param name="previousSettings">前の設定</param>
        /// <param name="changedCategory">変更されたカテゴリ</param>
        /// <returns>ロールバックが成功したかどうか</returns>
        Task<bool> RollbackSettingsAsync(AppSettings currentSettings, AppSettings previousSettings, string? changedCategory = null);
    }
}
```

## 設定バリデーター実装例
```csharp
namespace Baketa.Core.Settings
{
    /// <summary>
    /// 設定バリデーター実装クラス
    /// </summary>
    public class SettingsValidator : ISettingsValidator
    {
        private readonly Dictionary<string, List<IValidationRule>> _globalRules = new Dictionary<string, List<IValidationRule>>();
        private readonly Dictionary<string, Dictionary<string, List<IValidationRule>>> _categoryRules = new Dictionary<string, Dictionary<string, List<IValidationRule>>>();
        private readonly ILogger? _logger;
        
        /// <summary>
        /// 新しい設定バリデーターを初期化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public SettingsValidator(ILogger? logger = null)
        {
            _logger = logger;
            
            // 基本ルールの登録
            RegisterDefaultRules();
            
            _logger?.LogInformation("設定バリデーターが初期化されました。");
        }
        
        /// <inheritdoc />
        public ValidationResult Validate(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            var result = new ValidationResult();
            var context = new ValidationContext(settings, string.Empty, string.Empty);
            
            // グローバルルールの適用
            ApplyGlobalRules(settings, result, context);
            
            // 各カテゴリのルールを適用
            ValidateCategory("General", settings.General, result, context);
            ValidateCategory("Theme", settings.Theme, result, context);
            ValidateCategory("Localization", settings.Localization, result, context);
            ValidateCategory("Capture", settings.Capture, result, context);
            ValidateCategory("Ocr", settings.Ocr, result, context);
            ValidateCategory("Translation", settings.Translation, result, context);
            ValidateCategory("Overlay", settings.Overlay, result, context);
            ValidateCategory("Hotkeys", settings.Hotkeys, result, context);
            ValidateCategory("Advanced", settings.Advanced, result, context);
            
            // プロファイル設定の検証
            foreach (var profileEntry in settings.GameProfiles)
            {
                ValidateGameProfile(profileEntry.Key, profileEntry.Value, result, context);
            }
            
            return result;
        }
        
        /// <inheritdoc />
        public ValidationResult Validate<T>(string category, T settings) where T : class
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("カテゴリ名が空です。", nameof(category));
                
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            var result = new ValidationResult();
            
            // カテゴリに対応するダミーのAppSettingsを作成
            var dummySettings = CreateDummySettings(category, settings);
            var context = new ValidationContext(dummySettings, category, category);
            
            // カテゴリ固有のルールを適用
            ValidateCategory(category, settings, result, context);
            
            return result;
        }
        
        /// <inheritdoc />
        public void AddRule(IValidationRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
                
            string path = rule.PropertyPath;
            
            if (!_globalRules.TryGetValue(path, out var rules))
            {
                rules = new List<IValidationRule>();
                _globalRules[path] = rules;
            }
            
            rules.Add(rule);
            SortRules(rules);
            
            _logger?.LogDebug("グローバル検証ルールが追加されました: {Path}", path);
        }
        
        /// <inheritdoc />
        public void AddRule(string category, IValidationRule rule)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("カテゴリ名が空です。", nameof(category));
                
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
                
            if (!_categoryRules.TryGetValue(category, out var categoryRuleDict))
            {
                categoryRuleDict = new Dictionary<string, List<IValidationRule>>();
                _categoryRules[category] = categoryRuleDict;
            }
            
            string path = rule.PropertyPath;
            
            if (!categoryRuleDict.TryGetValue(path, out var rules))
            {
                rules = new List<IValidationRule>();
                categoryRuleDict[path] = rules;
            }
            
            rules.Add(rule);
            SortRules(rules);
            
            _logger?.LogDebug("カテゴリ固有の検証ルールが追加されました: {Category}.{Path}", category, path);
        }
        
        // 実装の詳細は省略
    }
}
```

## 実装上の注意点
- 検証ルールは拡張可能な設計にする（既存ルールの変更やカスタムルールの追加が容易）
- 検証エラーメッセージはローカライズ可能な形式で提供する
- 検証コンテキストをスレッドセーフに設計し、並列検証をサポートする
- 設定変更通知は必要最小限に抑え、パフォーマンスの低下を防ぐ
- 設定適用ハンドラーはエラー耐性を持ち、部分的な適用失敗を適切に処理する
- 設定変更のロールバックは安全かつ確実に行える設計にする
- 大規模な設定変更の場合はバッチ処理を考慮し、不要な処理の重複を避ける
- 設定の依存関係グラフを明示的に管理し、循環依存を防止する

## 関連Issue/参考
- 親Issue: #12 設定画面
- 依存Issue: #12-1 設定データモデルと永続化システムの実装
- 関連Issue: #12-2 設定UI画面の設計と実装
- 関連Issue: #4 イベント集約機構の構築
- 参照: E:\dev\Baketa\docs\3-architecture\core\settings\validation.md
- 参照: E:\dev\Baketa\docs\3-architecture\core\settings\settings-handlers.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (2.2 Requiredプロパティの使用)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.3 最新の例外処理パターン)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: medium`
- `component: ui`

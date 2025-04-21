# Issue 12-4: 設定インポート・エクスポート機能の実装

## 概要
アプリケーション設定をエクスポート（保存）したり、外部ファイルからインポート（読み込み）したりする機能を実装します。これにより、ユーザーは自分の設定をバックアップしたり、他のインストール環境と共有したり、以前の設定状態に復元したりすることが可能になります。

## 目的・理由
設定インポート・エクスポート機能は以下の理由で重要です：

1. ユーザーが設定のバックアップを作成でき、アプリケーションの再インストール時にも設定を維持できる
2. 複数のデバイスやインストール環境間で設定を共有できる
3. 異なるプロファイルやゲーム設定を簡単に交換できる
4. トラブルシューティングの際に、特定の設定状態への復元が容易になる
5. コミュニティでの設定共有が可能になり、ユーザーエクスペリエンスが向上する

## 詳細
- 設定の完全エクスポート/インポート機能の実装
- カテゴリ別やプロファイル別のエクスポート/インポート機能
- 設定互換性検証と安全なインポート機能
- 設定マイグレーション機能（旧バージョン設定の変換）

## タスク分解
- [ ] 設定エクスポート機能
  - [ ] 全設定のエクスポートメソッドの実装
  - [ ] カテゴリ別・プロファイル別エクスポートの実装
  - [ ] エクスポート形式（JSON, XML, バイナリ）の選択サポート
  - [ ] エクスポートファイルのメタデータ付与（バージョン、日時など）
  - [ ] エクスポートファイルの暗号化オプション（機密情報保護用）
- [ ] 設定インポート機能
  - [ ] 全設定のインポートメソッドの実装
  - [ ] 部分的インポート（カテゴリ別・プロファイル別）の実装
  - [ ] インポート前の設定互換性検証の実装
  - [ ] 暗号化されたエクスポートファイルの復号化処理
  - [ ] インポート失敗時のロールバック機能
- [ ] 設定マイグレーション機能
  - [ ] 設定バージョン管理メカニズムの実装
  - [ ] バージョン別マイグレーションルールの実装
  - [ ] 互換性のない設定の自動修正機能
  - [ ] マイグレーションログの実装
- [ ] 設定差分検出
  - [ ] 現在の設定とインポート設定の差分表示
  - [ ] マージオプション（特定の設定のみ適用）
  - [ ] 競合解決インターフェースの実装
- [ ] 設定共有機能
  - [ ] プロファイル定義の標準形式設計
  - [ ] プロファイル検証と安全性チェック機能
  - [ ] カスタムプロファイルのインストール機能
- [ ] UI統合
  - [ ] インポート・エクスポートダイアログの実装
  - [ ] 進行状況と結果のフィードバック表示
  - [ ] エラー・警告メッセージの表示
  - [ ] 差分表示と選択UIの実装
- [ ] 単体テスト
  - [ ] エクスポート・インポート機能のテスト
  - [ ] マイグレーション機能のテスト
  - [ ] 差分検出のテスト
  - [ ] エラー処理のテスト

## 主要インターフェース設計案

```csharp
namespace Baketa.Core.Settings
{
    /// <summary>
    /// 設定インポート・エクスポートインターフェース
    /// </summary>
    public interface ISettingsImportExport
    {
        /// <summary>
        /// 設定をファイルにエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <param name="settings">エクスポートする設定</param>
        /// <param name="options">エクスポートオプション</param>
        /// <returns>エクスポートが成功したかどうか</returns>
        Task<bool> ExportSettingsAsync(string filePath, AppSettings settings, ExportOptions? options = null);
        
        /// <summary>
        /// 特定のカテゴリの設定をファイルにエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <param name="category">カテゴリ名</param>
        /// <param name="settings">エクスポートする設定</param>
        /// <param name="options">エクスポートオプション</param>
        /// <returns>エクスポートが成功したかどうか</returns>
        Task<bool> ExportCategorySettingsAsync(string filePath, string category, object settings, ExportOptions? options = null);
        
        /// <summary>
        /// プロファイル設定をファイルにエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <param name="profileName">プロファイル名</param>
        /// <param name="options">エクスポートオプション</param>
        /// <returns>エクスポートが成功したかどうか</returns>
        Task<bool> ExportProfileSettingsAsync(string filePath, string profileName, ExportOptions? options = null);
        
        /// <summary>
        /// ファイルから設定をインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="options">インポートオプション</param>
        /// <returns>インポート結果</returns>
        Task<ImportResult> ImportSettingsAsync(string filePath, ImportOptions? options = null);
        
        /// <summary>
        /// ファイルから特定のカテゴリの設定をインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="category">カテゴリ名</param>
        /// <param name="options">インポートオプション</param>
        /// <returns>インポート結果</returns>
        Task<ImportResult> ImportCategorySettingsAsync(string filePath, string category, ImportOptions? options = null);
        
        /// <summary>
        /// ファイルからプロファイル設定をインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="profileName">プロファイル名</param>
        /// <param name="options">インポートオプション</param>
        /// <returns>インポート結果</returns>
        Task<ImportResult> ImportProfileSettingsAsync(string filePath, string profileName, ImportOptions? options = null);
        
        /// <summary>
        /// ファイルの設定情報を検証します
        /// </summary>
        /// <param name="filePath">検証するファイルパス</param>
        /// <returns>検証結果</returns>
        Task<ValidationResult> ValidateSettingsFileAsync(string filePath);
        
        /// <summary>
        /// インポート前の設定の差分を取得します
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="currentSettings">現在の設定</param>
        /// <returns>設定の差分</returns>
        Task<SettingsDiff> GetSettingsDiffAsync(string filePath, AppSettings currentSettings);
    }
    
    /// <summary>
    /// エクスポートオプションクラス
    /// </summary>
    public class ExportOptions
    {
        /// <summary>
        /// エクスポート形式
        /// </summary>
        public ExportFormat Format { get; set; } = ExportFormat.Json;
        
        /// <summary>
        /// エクスポートするカテゴリのリスト（nullの場合はすべてのカテゴリ）
        /// </summary>
        public IReadOnlyList<string>? Categories { get; set; }
        
        /// <summary>
        /// 機密情報を暗号化するかどうか
        /// </summary>
        public bool EncryptSensitiveData { get; set; } = true;
        
        /// <summary>
        /// パスワード（暗号化する場合）
        /// </summary>
        public string? Password { get; set; }
        
        /// <summary>
        /// 出力を整形するかどうか
        /// </summary>
        public bool PrettyPrint { get; set; } = true;
        
        /// <summary>
        /// メタデータを含めるかどうか
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;
        
        /// <summary>
        /// コメントを含めるかどうか
        /// </summary>
        public bool IncludeComments { get; set; } = true;
    }
    
    /// <summary>
    /// インポートオプションクラス
    /// </summary>
    public class ImportOptions
    {
        /// <summary>
        /// バリデーションを実施するかどうか
        /// </summary>
        public bool Validate { get; set; } = true;
        
        /// <summary>
        /// インポートするカテゴリのリスト（nullの場合はすべてのカテゴリ）
        /// </summary>
        public IReadOnlyList<string>? Categories { get; set; }
        
        /// <summary>
        /// マージモード
        /// </summary>
        public MergeMode MergeMode { get; set; } = MergeMode.OverwriteAll;
        
        /// <summary>
        /// パスワード（暗号化されている場合）
        /// </summary>
        public string? Password { get; set; }
        
        /// <summary>
        /// バージョンの互換性を検証するかどうか
        /// </summary>
        public bool ValidateVersion { get; set; } = true;
        
        /// <summary>
        /// エラー発生時にロールバックするかどうか
        /// </summary>
        public bool RollbackOnError { get; set; } = true;
        
        /// <summary>
        /// 競合解決ハンドラー
        /// </summary>
        public IConflictResolver? ConflictResolver { get; set; }
    }
    
    /// <summary>
    /// エクスポート形式列挙型
    /// </summary>
    public enum ExportFormat
    {
        /// <summary>
        /// JSON形式
        /// </summary>
        Json,
        
        /// <summary>
        /// XML形式
        /// </summary>
        Xml,
        
        /// <summary>
        /// バイナリ形式
        /// </summary>
        Binary,
        
        /// <summary>
        /// YAML形式
        /// </summary>
        Yaml
    }
    
    /// <summary>
    /// マージモード列挙型
    /// </summary>
    public enum MergeMode
    {
        /// <summary>
        /// すべて上書き
        /// </summary>
        OverwriteAll,
        
        /// <summary>
        /// 存在しない項目のみ追加
        /// </summary>
        AddMissingOnly,
        
        /// <summary>
        /// 選択的マージ（差分UIで選択）
        /// </summary>
        Selective,
        
        /// <summary>
        /// 競合解決ハンドラーを使用
        /// </summary>
        UseResolver
    }
    
    /// <summary>
    /// インポート結果クラス
    /// </summary>
    public class ImportResult
    {
        /// <summary>
        /// インポートが成功したかどうか
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// インポートされた設定（インポートが成功した場合）
        /// </summary>
        public AppSettings? ImportedSettings { get; set; }
        
        /// <summary>
        /// エラーメッセージ（インポートが失敗した場合）
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// 詳細なエラー情報（インポートが失敗した場合）
        /// </summary>
        public IReadOnlyList<string> DetailedErrors { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// 警告メッセージ
        /// </summary>
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// インポート前の設定（ロールバック用）
        /// </summary>
        public AppSettings? PreviousSettings { get; set; }
        
        /// <summary>
        /// インポートされたカテゴリのリスト
        /// </summary>
        public IReadOnlyList<string> ImportedCategories { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// 設定ファイルのメタデータ
        /// </summary>
        public SettingsMetadata? Metadata { get; set; }
    }
    
    /// <summary>
    /// 設定メタデータクラス
    /// </summary>
    public class SettingsMetadata
    {
        /// <summary>
        /// アプリケーションバージョン
        /// </summary>
        public string AppVersion { get; set; } = string.Empty;
        
        /// <summary>
        /// 設定スキーマバージョン
        /// </summary>
        public string SchemaVersion { get; set; } = string.Empty;
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 作成者名
        /// </summary>
        public string? CreatedBy { get; set; }
        
        /// <summary>
        /// 設定の説明
        /// </summary>
        public string? Description { get; set; }
        
        /// <summary>
        /// 含まれるカテゴリのリスト
        /// </summary>
        public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// カスタムプロパティ
        /// </summary>
        public Dictionary<string, string> CustomProperties { get; set; } = new Dictionary<string, string>();
    }
    
    /// <summary>
    /// 設定差分クラス
    /// </summary>
    public class SettingsDiff
    {
        /// <summary>
        /// カテゴリ別の差分情報
        /// </summary>
        public Dictionary<string, CategoryDiff> Categories { get; set; } = new Dictionary<string, CategoryDiff>();
        
        /// <summary>
        /// 差分のあるカテゴリ数
        /// </summary>
        public int ChangedCategoriesCount => Categories.Count;
        
        /// <summary>
        /// 差分の総数
        /// </summary>
        public int TotalChangesCount => Categories.Values.Sum(c => c.ChangesCount);
        
        /// <summary>
        /// 差分がない（同一内容）かどうか
        /// </summary>
        public bool IsIdentical => TotalChangesCount == 0;
    }
    
    /// <summary>
    /// カテゴリ差分クラス
    /// </summary>
    public class CategoryDiff
    {
        /// <summary>
        /// カテゴリ名
        /// </summary>
        public string Category { get; set; } = string.Empty;
        
        /// <summary>
        /// 追加された設定
        /// </summary>
        public List<PropertyDiff> Added { get; set; } = new List<PropertyDiff>();
        
        /// <summary>
        /// 変更された設定
        /// </summary>
        public List<PropertyDiff> Modified { get; set; } = new List<PropertyDiff>();
        
        /// <summary>
        /// 削除された設定
        /// </summary>
        public List<PropertyDiff> Removed { get; set; } = new List<PropertyDiff>();
        
        /// <summary>
        /// 差分の総数
        /// </summary>
        public int ChangesCount => Added.Count + Modified.Count + Removed.Count;
    }
    
    /// <summary>
    /// プロパティ差分クラス
    /// </summary>
    public class PropertyDiff
    {
        /// <summary>
        /// プロパティパス
        /// </summary>
        public string Path { get; set; } = string.Empty;
        
        /// <summary>
        /// 古い値
        /// </summary>
        public object? OldValue { get; set; }
        
        /// <summary>
        /// 新しい値
        /// </summary>
        public object? NewValue { get; set; }
        
        /// <summary>
        /// 差分タイプ
        /// </summary>
        public DiffType Type { get; set; }
        
        /// <summary>
        /// 推奨アクション
        /// </summary>
        public DiffAction RecommendedAction { get; set; } = DiffAction.Accept;
    }
    
    /// <summary>
    /// 差分タイプ列挙型
    /// </summary>
    public enum DiffType
    {
        /// <summary>
        /// 追加
        /// </summary>
        Added,
        
        /// <summary>
        /// 変更
        /// </summary>
        Modified,
        
        /// <summary>
        /// 削除
        /// </summary>
        Removed
    }
    
    /// <summary>
    /// 差分アクション列挙型
    /// </summary>
    public enum DiffAction
    {
        /// <summary>
        /// 受け入れる
        /// </summary>
        Accept,
        
        /// <summary>
        /// 拒否する
        /// </summary>
        Reject,
        
        /// <summary>
        /// マージする
        /// </summary>
        Merge
    }
    
    /// <summary>
    /// 競合解決インターフェース
    /// </summary>
    public interface IConflictResolver
    {
        /// <summary>
        /// 競合を解決します
        /// </summary>
        /// <param name="conflict">競合情報</param>
        /// <returns>解決結果</returns>
        Task<ConflictResolution> ResolveConflictAsync(PropertyDiff conflict);
    }
    
    /// <summary>
    /// 競合解決列挙型
    /// </summary>
    public enum ConflictResolution
    {
        /// <summary>
        /// 新しい値を使用
        /// </summary>
        UseNew,
        
        /// <summary>
        /// 現在の値を保持
        /// </summary>
        KeepCurrent,
        
        /// <summary>
        /// カスタム値を使用
        /// </summary>
        UseCustom,
        
        /// <summary>
        /// すべての競合で新しい値を使用
        /// </summary>
        UseNewForAll,
        
        /// <summary>
        /// すべての競合で現在の値を保持
        /// </summary>
        KeepCurrentForAll,
        
        /// <summary>
        /// キャンセル
        /// </summary>
        Cancel
    }
}
```

## 実装例: 設定エクスポート機能
```csharp
namespace Baketa.Core.Settings
{
    /// <summary>
    /// 設定インポート・エクスポート実装クラス
    /// </summary>
    public class SettingsImportExport : ISettingsImportExport
    {
        private readonly ISettingsService _settingsService;
        private readonly ISettingsValidator _settingsValidator;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        
        /// <summary>
        /// 新しい設定インポート・エクスポートを初期化します
        /// </summary>
        /// <param name="settingsService">設定サービス</param>
        /// <param name="settingsValidator">設定バリデーター</param>
        /// <param name="encryptionService">暗号化サービス</param>
        /// <param name="logger">ロガー</param>
        public SettingsImportExport(
            ISettingsService settingsService,
            ISettingsValidator settingsValidator,
            IEncryptionService encryptionService,
            ILogger? logger = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _settingsValidator = settingsValidator ?? throw new ArgumentNullException(nameof(settingsValidator));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _logger = logger;
            
            _logger?.LogInformation("設定インポート・エクスポートが初期化されました。");
        }
        
        /// <inheritdoc />
        public async Task<bool> ExportSettingsAsync(string filePath, AppSettings settings, ExportOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("ファイルパスが空です。", nameof(filePath));
                
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            options ??= new ExportOptions();
            
            // セマフォを取得
            await _ioLock.WaitAsync();
            
            try
            {
                // 設定をクローン（機密情報処理のため）
                var settingsToExport = CloneSettings(settings);
                
                // 機密情報の処理
                if (options.EncryptSensitiveData)
                {
                    ProcessSensitiveData(settingsToExport, true, options.Password);
                }
                
                // メタデータの作成
                var metadata = CreateMetadata(settingsToExport, options);
                
                // シリアライズ
                string serialized = SerializeSettings(settingsToExport, metadata, options);
                
                // ファイル書き込み
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // 一時ファイルに書き込み
                string tempFile = filePath + ".tmp";
                await File.WriteAllTextAsync(tempFile, serialized);
                
                // 既存のファイルをバックアップ
                if (File.Exists(filePath))
                {
                    string backupFile = filePath + ".bak";
                    if (File.Exists(backupFile))
                    {
                        File.Delete(backupFile);
                    }
                    File.Move(filePath, backupFile);
                }
                
                // 一時ファイルを本来のファイルにリネーム
                File.Move(tempFile, filePath);
                
                _logger?.LogInformation("設定をエクスポートしました: {FilePath}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "設定のエクスポート中にエラーが発生しました: {FilePath}", filePath);
                return false;
            }
            finally
            {
                // セマフォを解放
                _ioLock.Release();
            }
        }
        
        // 他のメソッドの実装は省略
    }
}
```

## 実装上の注意点
- ファイルI/O操作は適切なエラーハンドリングと競合状態の回避を行う
- 機密情報（APIキー、パスワードなど）は適切に暗号化し、安全に保管する
- エクスポートファイルには適切なバージョン情報を含め、将来の互換性を確保する
- インポート前には必ず設定の検証を行い、不正な値や互換性のない設定が適用されないようにする
- 部分的インポート（特定のカテゴリやプロファイルのみ）のサポートにより柔軟性を高める
- 設定差分の視覚的表示により、ユーザーが何が変更されるかを事前に理解できるようにする
- インポート失敗時には適切なロールバックメカニズムを提供し、設定の整合性を維持する
- 大量の設定データを効率的に処理するためのパフォーマンス最適化を行う
- 多様なフォーマット（JSON, XML, バイナリなど）のサポートによりユーザーの好みに対応する
- 設定共有のためのプロファイル標準形式を明確に定義し、ドキュメント化する

## 関連Issue/参考
- 親Issue: #12 設定画面
- 依存Issue: #12-1 設定データモデルと永続化システムの実装
- 依存Issue: #12-3 設定検証と適用システムの実装
- 関連Issue: #12-2 設定UI画面の設計と実装
- 関連Issue: #13 OCR設定UIとプロファイル管理
- 参照: E:\dev\Baketa\docs\3-architecture\core\settings\import-export.md
- 参照: E:\dev\Baketa\docs\3-architecture\core\settings\settings-migration.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.1 アプリケーション固有の例外)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (6.1 構造化ログ記録)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: medium`
- `component: ui`

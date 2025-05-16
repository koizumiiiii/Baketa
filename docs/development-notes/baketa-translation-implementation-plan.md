# Baketa翻訳基盤実装計画（更新版）

## 1. プロジェクト概要

Baketaプロジェクトの翻訳基盤は、ゲームプレイ中にリアルタイムでテキストを翻訳するための高性能かつ柔軟なシステムです。当初はWebAPIベースの翻訳エンジン（Google翻訳、DeepL）を中心に計画されていましたが、仕様変更に伴い、クラウドAI翻訳（Google Gemini）とローカルモデル翻訳（Helsinki-NLP OPUS-MT ONNX）に焦点が移行しました。

### 1.1 対象Issue概要

本計画では、以下のIssueを実装します：

#### 基盤システム（当初計画）
- **#63: 翻訳エンジン基盤の実装**
  - 翻訳処理を行うエンジン基盤の実装
  - 複数の翻訳方式を統一的に扱うためのインターフェース実装

- **#64: 翻訳結果管理システムの実装**
  - 翻訳結果の保存、取得、更新、連携のための基盤実装
  - コンテキスト対応の翻訳結果管理機能

- **#65: 翻訳イベントシステムの実装**
  - 翻訳処理の各段階と層間を疎結合に連携するイベントシステム
  - UI更新に反映させる通知機能の提供

- **#53: 翻訳キャッシュシステム**
  - 翻訳結果をリアルタイムでキャッシュするシステム
  - メモリベースと永続化型の2種類のキャッシュ実装

#### 拡張システム（仕様変更後の追加計画）
- **#78: クラウドAI翻訳処理系の実装**
  - Google Gemini APIを活用した高品質翻訳の実現
  - 文脈理解、複数文章の連続処理などの高度な翻訳機能

- **#79: ローカル翻訳モデルの実装**
  - Helsinki-NLP OPUS-MT ONNXモデルの組み込み
  - 効率的かつ高速なモデル管理とメモリ最適化

## 2. アーキテクチャと名前空間構造

Baketaプロジェクトはクリーンアーキテクチャを採用し、以下の層構造で実装します：

```
Baketa.Translation                         // 翻訳機能のルート名前空間
├── Baketa.Core.Translation                // コアレイヤー
│   ├── Abstractions                       // 基本インターフェース
│   │   ├── ITranslationEngine            // 基本翻訳エンジンIF
│   │   ├── ICloudTranslationEngine       // クラウド翻訳エンジンIF 
│   │   └── ILocalTranslationEngine       // ローカル翻訳エンジンIF
│   ├── Events                             // イベント定義
│   ├── Models                             // データモデル
│   └── Services                           // サービスインターフェース
│
├── Baketa.Infrastructure.Translation      // インフラレイヤー
│   ├── Engines                            // 翻訳エンジン実装
│   │   ├── Cloud                          // クラウドAI実装（Gemini）
│   │   └── Local                          // ローカル実装（ONNX）
│   ├── Cache                              // キャッシュシステム実装
│   ├── Repository                         // データ永続化実装
│   └── Services                           // サービス実装
│
├── Baketa.Application.Translation         // アプリケーションレイヤー
│   ├── Services                           // 翻訳アプリケーションサービス
│   ├── Handlers                           // イベントハンドラー
│   └── Configuration                      // 翻訳設定
│
└── Baketa.UI.Translation                  // UIレイヤー
    ├── ViewModels                         // 翻訳関連のビューモデル
    └── Views                              // 翻訳関連のビュー
```

## 3. 主要インターフェース設計

### 3.1 翻訳エンジン基本インターフェース

```csharp
/// <summary>
/// 翻訳エンジンの基本インターフェース
/// </summary>
public interface ITranslationEngine : IDisposable
{
    /// <summary>
    /// 翻訳エンジン名
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 翻訳エンジンの説明
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// ネットワーク接続が必要かどうか
    /// </summary>
    bool RequiresNetwork { get; }
    
    /// <summary>
    /// テキストを翻訳します
    /// </summary>
    Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// 複数のテキストをバッチ翻訳します
    /// </summary>
    Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// サポートされている言語ペアを取得します
    /// </summary>
    Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();
    
    /// <summary>
    /// 言語ペアがサポートされているか確認します
    /// </summary>
    Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair);
    
    /// <summary>
    /// 翻訳エンジンが準備完了しているか確認します
    /// </summary>
    Task<bool> IsReadyAsync();
    
    /// <summary>
    /// 翻訳エンジンを初期化します
    /// </summary>
    Task<bool> InitializeAsync();
}
```

### 3.2 クラウドAI翻訳エンジンインターフェース

```csharp
/// <summary>
/// クラウドAI翻訳エンジンのインターフェース
/// </summary>
public interface ICloudTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// APIのベースURL
    /// </summary>
    Uri ApiBaseUrl { get; }
    
    /// <summary>
    /// APIキーが設定されているかどうか
    /// </summary>
    bool HasApiKey { get; }
    
    /// <summary>
    /// クラウドプロバイダーの種類
    /// </summary>
    CloudProviderType ProviderType { get; }
    
    /// <summary>
    /// 高度な翻訳機能の実行
    /// </summary>
    Task<AdvancedTranslationResponse> TranslateAdvancedAsync(
        AdvancedTranslationRequest request, 
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// APIのステータスを確認します
    /// </summary>
    Task<ApiStatusInfo> CheckApiStatusAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// テキストの言語を自動検出します
    /// </summary>
    Task<LanguageDetectionResult> DetectLanguageAsync(
        string text, 
        CancellationToken cancellationToken = default);
}
```

### 3.3 ローカル翻訳エンジンインターフェース

```csharp
/// <summary>
/// ローカル翻訳エンジンのインターフェース
/// </summary>
public interface ILocalTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// モデルのパス
    /// </summary>
    string ModelPath { get; }
    
    /// <summary>
    /// 使用中のデバイス
    /// </summary>
    ComputeDevice Device { get; }
    
    /// <summary>
    /// モデルのメモリ使用量
    /// </summary>
    long MemoryUsage { get; }
    
    /// <summary>
    /// モデルローダーの取得
    /// </summary>
    IModelLoader GetModelLoader();
    
    /// <summary>
    /// トークナイザーの取得
    /// </summary>
    ITokenizer GetTokenizer();
    
    /// <summary>
    /// モデルを指定デバイスにロード
    /// </summary>
    Task<bool> LoadModelToDeviceAsync(ComputeDevice device);
    
    /// <summary>
    /// モデルをアンロード
    /// </summary>
    Task<bool> UnloadModelAsync();
}
```

### 3.4 翻訳キャッシュインターフェース

```csharp
/// <summary>
/// 翻訳キャッシュインターフェース
/// </summary>
public interface ITranslationCache
{
    /// <summary>
    /// キャッシュからエントリを取得します
    /// </summary>
    Task<TranslationCacheEntry?> GetAsync(string key);
    
    /// <summary>
    /// 複数のキャッシュエントリを取得します
    /// </summary>
    Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(IEnumerable<string> keys);
    
    /// <summary>
    /// キャッシュにエントリを保存します
    /// </summary>
    Task<bool> SetAsync(string key, TranslationCacheEntry entry, TimeSpan? expiration = null);
    
    /// <summary>
    /// 複数のキャッシュエントリを保存します
    /// </summary>
    Task<bool> SetManyAsync(IDictionary<string, TranslationCacheEntry> entries, TimeSpan? expiration = null);
    
    /// <summary>
    /// キャッシュからエントリを削除します
    /// </summary>
    Task<bool> RemoveAsync(string key);
    
    /// <summary>
    /// キャッシュをクリアします
    /// </summary>
    Task<bool> ClearAsync();
    
    /// <summary>
    /// キャッシュの統計情報を取得します
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync();
}
```

### 3.5 翻訳イベントインターフェース

```csharp
/// <summary>
/// 翻訳イベントの基本インターフェース
/// </summary>
public interface ITranslationEvent : IEvent
{
    /// <summary>
    /// 翻訳元テキスト
    /// </summary>
    string SourceText { get; }
    
    /// <summary>
    /// 翻訳元言語
    /// </summary>
    Language SourceLanguage { get; }
    
    /// <summary>
    /// 翻訳先言語
    /// </summary>
    Language TargetLanguage { get; }
}

/// <summary>
/// イベントハンドラーインターフェース
/// </summary>
public interface IEventHandler<TEvent> where TEvent : IEvent
{
    /// <summary>
    /// イベントを処理します
    /// </summary>
    Task HandleAsync(TEvent @event);
}

/// <summary>
/// イベント集約インターフェース
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// イベントを発行します
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
    
    /// <summary>
    /// イベントハンドラーを登録します
    /// </summary>
    void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    
    /// <summary>
    /// イベントハンドラーの登録を解除します
    /// </summary>
    void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
}
```

## 4. 主要データモデル

### 4.1 基本データモデル

```csharp
/// <summary>
/// 翻訳リクエスト
/// </summary>
public class TranslationRequest
{
    /// <summary>
    /// 翻訳元テキスト
    /// </summary>
    public required string SourceText { get; set; }
    
    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public required Language SourceLanguage { get; set; }
    
    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public required Language TargetLanguage { get; set; }
    
    /// <summary>
    /// 翻訳コンテキスト
    /// </summary>
    public TranslationContext? Context { get; set; }
    
    /// <summary>
    /// オプション
    /// </summary>
    public Dictionary<string, object?> Options { get; } = new();
    
    /// <summary>
    /// リクエストID
    /// </summary>
    public Guid RequestId { get; } = Guid.NewGuid();
    
    /// <summary>
    /// タイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 言語ペア
    /// </summary>
    public LanguagePair LanguagePair => new() { 
        SourceLanguage = SourceLanguage, 
        TargetLanguage = TargetLanguage 
    };
}

/// <summary>
/// 翻訳レスポンス
/// </summary>
public class TranslationResponse
{
    /// <summary>
    /// リクエストID
    /// </summary>
    public required Guid RequestId { get; set; }
    
    /// <summary>
    /// 翻訳元テキスト
    /// </summary>
    public required string SourceText { get; set; }
    
    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    public string? TranslatedText { get; set; }
    
    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public required Language SourceLanguage { get; set; }
    
    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public required Language TargetLanguage { get; set; }
    
    /// <summary>
    /// 翻訳エンジン名
    /// </summary>
    public required string EngineName { get; set; }
    
    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }
    
    /// <summary>
    /// 翻訳が成功したかどうか
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// エラー情報
    /// </summary>
    public TranslationError? Error { get; set; }
}
```

### 4.2 拡張データモデル

```csharp
/// <summary>
/// 高度な翻訳リクエスト（クラウドAI向け）
/// </summary>
public class AdvancedTranslationRequest : TranslationRequest
{
    /// <summary>
    /// 品質レベル（0～5）
    /// </summary>
    public int QualityLevel { get; set; } = 3;
    
    /// <summary>
    /// トークン上限
    /// </summary>
    public int MaxTokens { get; set; } = 500;
    
    /// <summary>
    /// プロンプトテンプレート
    /// </summary>
    public string? PromptTemplate { get; set; }
    
    /// <summary>
    /// 追加コンテキスト
    /// </summary>
    public List<string> AdditionalContexts { get; } = new();
}

/// <summary>
/// ローカルモデル翻訳オプション
/// </summary>
public class LocalModelOptions
{
    /// <summary>
    /// モデルタイプ
    /// </summary>
    public string ModelType { get; set; } = "ONNX";
    
    /// <summary>
    /// ビームサイズ
    /// </summary>
    public int BeamSize { get; set; } = 4;
    
    /// <summary>
    /// 最大出力長
    /// </summary>
    public int MaxOutputLength { get; set; } = 256;
    
    /// <summary>
    /// バッチサイズ
    /// </summary>
    public int BatchSize { get; set; } = 1;
    
    /// <summary>
    /// トークナイザーオプション
    /// </summary>
    public Dictionary<string, object> TokenizerOptions { get; } = new();
}

/// <summary>
/// コンピュートデバイス情報
/// </summary>
public class ComputeDevice
{
    /// <summary>
    /// デバイスID
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// デバイス名
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// デバイスタイプ
    /// </summary>
    public ComputeDeviceType DeviceType { get; set; }
    
    /// <summary>
    /// メモリ容量
    /// </summary>
    public long MemoryCapacity { get; set; }
    
    /// <summary>
    /// 利用可能かどうか
    /// </summary>
    public bool IsAvailable { get; set; }
}
```

## 5. 実装戦略

### 5.1 フェーズ分けとタスクチェックリスト

実装は以下のフェーズに分けて進めます。各タスクの進行状況をチェックできるようにしています。

#### フェーズ1: 基盤システム実装（#63, #64, #65, #53）

##### 1. 基礎設計フェーズ (1週目)
- [x] 共通インターフェースの設計（`ITranslationEngine`, `ITranslationCache` 等）
- [x] データモデルの詳細設計（`TranslationRequest`, `TranslationResponse`, `Language` 等）
- [x] 例外クラス階層の設計（`TranslationExceptions.cs`）
- [x] 単体テスト計画の策定
- [x] モック実装（`MockTranslationEngine`, `DummyEngine`）

##### 2. コア実装フェーズ (2-3週目)

**#63: 翻訳エンジン基盤の実装**
- [x] `TranslationEngineBase` 抽象クラスの実装
- [x] `WebApiTranslationEngineBase` 基底クラスの実装
- [x] テスト用の `SimpleEngine` の実装
- [x] エンジンファクトリ（`DefaultTranslationEngineFactory`）の実装
- [x] キャンセレーション対応

**#64: 翻訳結果管理システムの実装**
- [x] `ITranslationManager` インターフェースの実装
- [x] `ITranslationRepository` インターフェースの実装
- [x] `InMemoryTranslationManager` の実装
- [x] `InMemoryTranslationRepository` の実装
- [x] 翻訳レコード、検索クエリ、統計モデルの実装

**#53: 翻訳キャッシュシステムの実装**
- [x] `ITranslationCache` インターフェースの実装
- [x] `MemoryTranslationCache` 実装
- [x] `DummyPersistentCache` 実装
- [x] キャッシュキー生成機模の実装

**#65: 翻訳イベントシステムの実装**
- [x] イベント定義（`TranslationEvents.cs`）
- [x] イベントハンドラインターフェース（`ITranslationEventHandler`）
- [x] イベント集約器（`DefaultEventAggregator`）
- [x] ロギングハンドラ（`LoggingTranslationEventHandler`）

##### 3. エッジケース実装と最適化 (3-4週目)
- [x] 翻訳コンテキストを考慮した検索機能の実装
- [x] 言語コードの大文字小文字処理の強化
- [x] タグ検索とゲームプロファイルIDフィルターの実装
- [x] 翻訳レコード間の衝突解決機能の実装と例外処理強化
- [x] 統計機能の拡張と分析機能実装
- [x] パフォーマンステストと最適化

##### 4. 統合フェーズ (4週目)
- [✅] 依存性注入の設定とモジュール登録の最適化
- [✅] 各コンポーネントの統合テスト
- [✅] 標準翻訳パイプラインの実装とテスト
- [✅] トランザクション処理と一貫性確保の実装
- [✅] 言語検出機能の実装と統合

##### 5. ドキュメント作成と名前空間統一 (5週目)
- [ ] 名前空間の統一（`Baketa.Core.Models.Translation` と `Baketa.Core.Translation.Models` の統合）
- [ ] APIドキュメント生成
- [ ] アーキテクチャ図の更新
- [ ] ユーザーガイドの作成

#### フェーズ2: 拡張システム実装（#78, #79）
※基盤システム完了後に実施

##### 1. クラウドAI翻訳実装 (#78)
- [ ] Gemini API連携基盤クラスの実装
- [ ] APIキー管理と認証システム
- [ ] 翻訳用プロンプトテンプレートの作成
- [ ] レイテンシおよびコスト最適化
- [ ] 文脈認識と複数文章の連続処理

##### 2. ローカル翻訳モデル実装 (#79)
- [ ] ONNXランタイム連携
- [ ] Helsinki-NLPモデルのロードと実行
- [ ] SentencePieceトークナイザーの実装
- [ ] モデルキャッシュとメモリ管理
- [ ] GPU加速対応

##### 3. 拡張機能の実装と統合
- [ ] 翻訳品質向上機能（後処理、コンテキスト考慮）
- [ ] 自動モード選択（ローカル/クラウド切り替え）
- [ ] 翻訳品質評価メカニズム
- [ ] 拡張UI組み込み

##### 4. 最終テストとアップデートメカニズム
- [ ] 最終統合テスト
- [ ] モデルアップデートメカニズム
- [ ] ドキュメント更新
- [ ] 実運用設定の最適化

### 次のタスク

現在の優先讓位タスク（✅: 完了、⭕: 進行中、⏸: 保留）：

1. ✅ **パフォーマンステストと最適化** - ボトルネックの特定と改善
2. ⭕ **名前空間の統一化** - 次フェーズの主要タスクとして進行中
3. ✅ **依存性注入の設定最適化** - 各コンポーネントの連携を強化
4. ✅ **統合テストの実施** - 各コンポーネントの連携テスト
5. ✅ **標準翻訳パイプラインの実装** - 実装完了、テスト済み
6. ✅ **言語検出機能の実装とテスト** - 実装完了、動作確認済み
7. ⭕ **APIドキュメントの生成** - ドキュメントフェーズの一環として進行中
8. ⭕ **アーキテクチャ図の更新** - 新しい実装を反映した図面の更新作業中
9. ⏸ **クラウドAI翻訳処理系の実装準備** - 次フェーズ完了後に着手予定

### 5.2 開発アプローチ

- **インターフェース駆動開発**：
  - インターフェース定義を先行して確定
  - 依存コンポーネントがなくても並行実装可能
  - モックを活用した分離テスト

- **段階的統合**：
  - 依存関係の少ないコンポーネントから統合
  - 各ステップで機能テストを実施
  - DIコンテナの設定は一元管理

- **コード品質確保**：
  - 静的解析警告の対応
  - 名前空間競合の回避
  - 適切なログ記録と例外処理

### 5.3 修正ガイドライン

コード品質向上のための修正ガイドライン：

1. **名前空間の整理**：
   - `Baketa.Core.Models.Translation` と `Baketa.Core.Translation.Models` の重複を解消
   - 一貫した命名規則の適用

2. **非同期パターンの適用**：
   - `ConfigureAwait(false)` の一貫した使用
   - キャンセレーショントークンの適切な伝播
   - async void の回避

3. **例外処理の強化**：
   - 具体的な例外タイプの使用
   - 一貫した例外階層
   - スロー ヘルパーの活用

4. **Null安全性の確保**：
   - Nullable参照型の適切な使用
   - null チェックの追加
   - パブリックメソッドの引数検証

5. **文字列操作の最適化**：
   - `StringComparison` パラメータの指定
   - 言語固有の比較処理

## 6. タイムライン

### 6.1 基盤システム（フェーズ1）

| 週 | 主要タスク |
|----|-----------|
| 1  | インターフェース設計と共通モデル定義、モック実装、テスト計画 |
| 2  | 翻訳エンジン(#63)と結果管理(#64)の基本実装、キャッシュ(#53)の基本実装 |
| 3  | イベントシステム(#65)の実装、各コンポーネントのユニットテスト |
| 4  | 段階的統合と機能テスト、DIとサービス登録 |
| 5  | パフォーマンス測定と最適化、ドキュメント作成 |

### 6.2 拡張システム（フェーズ2）

注：フェーズ1完了後に着手

| 週 | 主要タスク |
|----|-----------|
| 1  | クラウドAI翻訳(#78)の基本実装、プロンプト設計 |
| 2  | ローカル翻訳モデル(#79)の基本実装、ONNX連携 |
| 3  | 高度な機能の実装、モデル管理システム |
| 4  | 両エンジンの統合と最適化、テスト |

## 7. 実装時の留意点

### 7.1 名前空間競合への対応

`Baketa.Core.Models.Translation` と `Baketa.Core.Translation.Models` の重複が問題となっています。

**短期的解決策**：
- 名前空間エイリアスの使用
  ```csharp
  using CoreModels = Baketa.Core.Models.Translation;
  using TransModels = Baketa.Core.Translation.Models;
  ```

**長期的解決策**：
- `Baketa.Core.Translation.Models` への統一
- 段階的な移行

### 7.2 パフォーマンス最適化

- **差分検出の効率化**：
  - サンプリングベースの高速検出
  - 変更箇所のみの処理で負荷軽減

- **メモリ使用量の適正化**：
  - キャッシュサイズの適切な設定
  - ONNXモデルのリソース管理
  - トークナイザーの最適化

- **非同期処理の効率化**：
  - リクエストのバッチ処理
  - キャンセレーション処理

### 7.3 コード品質の確保

- **コード分析警告への対応**：
  - CA1051: フィールドのprivate化
  - CA1510: スローヘルパーの使用
  - CA1031: 一般的な例外のキャッチ改善
  - CA2000: 破棄可能オブジェクトの明示的破棄
  - CA1062: nullチェックの追加
  - CA1307: 文字列比較でのStringComparison指定

- **テスト容易性の確保**：
  - インターフェースを介したモック
  - 依存関係の明確な分離

## 8. 今後の実装方針について

現在、基盤システム（#63, #64, #65, #53）の実装を進めている段階です。これらは翻訳システム全体の基盤となる部分であり、最初に完成させることが重要です。

新たに追加された#78および#79は、この基盤の上に構築される拡張機能として位置づけられます。これらは**基盤システム完了後に実装する計画**とし、以下の理由から段階的な実装アプローチを採用します：

1. **基盤の安定性確保**：基盤コンポーネントが安定してから拡張機能を追加することで、全体の安定性が向上します
2. **リスク分散**：範囲を広げすぎずに確実にリリース可能な状態を維持できます
3. **依存関係の明確化**：拡張システムは基盤システムに依存するため、基盤部分を先に完成させることが論理的です

ただし、基盤システム実装の段階でも、将来の拡張（#78, #79）を見据えたインターフェース設計を行うことで、スムーズな拡張が可能となるよう配慮します。具体的には：

- **ITranslationEngine**を基底インターフェースとし、**ICloudTranslationEngine**と**ILocalTranslationEngine**を派生インターフェースとして設計
- 共通の**TranslationContext**を拡張可能な形で実装
- **イベントシステム**が異なる翻訳エンジン間の連携をサポート

この段階的アプローチにより、各機能の品質を確保しながら、全体の開発を効率的に進めることができます。

## 9. 追加実装事項

先行して実装した機能と現在の追加実装状況：

### 9.1 言語検出機能の実装

#### 主な成果物

- `CoreModels.LanguageDetectionResult` クラスの実装
  - 言語検出結果と信頼度の管理
  - 代替言語候補のリストサポート

- `ITranslationEngine` インターフェースの強化
  - `DetectLanguageAsync` メソッドの追加
  - 全ての翻訳エンジンに共通の言語検出機能の標準化

- スタブ実装の提供
  - `TranslationEngineBase`にデフォルト実装を追加
  - `TranslationEngineAdapter`に移植レイヤーを実装

#### 今後の計画

- 本格的な言語検出アルゴリズムの実装
  - 統計ベースの単純検出モデル
  - 複雑な中国語/日本語/韓国語の検出の改善

- 検出結果の統合利用
  - 翻訳エンジン選択の最適化
  - 自動言語切り替えユーザーインターフェースの実装

### 9.2 エラー処理の強化

#### 主な成果物

- `TranslationErrorType` 列挙型の実装
  - 様々なエラー種別を定義（Network、Authentication、QuotaExceeded等）
  - エラー処理の標準化

- `TranslationError` クラスの改善
  - `ErrorType` プロパティの追加
  - コンストラクタと `Clone` メソッドの更新

#### 今後の計画

- 統一的なエラーハンドリングメカニズムの実装
  - エラーに基づく自動リトライ機能
  - エラーに応じた代替エンジンの選択

- UIレベルでのエラー表示の改善
  - エラー種別に基づくユーザーフレンドリーなメッセージ
  - リカバリーをサポートするアクションの案内

### 9.3 名前空間とリファクタリング

名前空間の統一化は重要なタスクとして残っていますが、現在は以下のアプローチで一時的に対応しています：

- 名前空間エイリアスの一貫した使用
  ```csharp
  using CoreModels = Baketa.Core.Models.Translation;
  using TransModels = Baketa.Core.Translation.Models;
  ```

- 今後の計画として、`Baketa.Core.Translation.Models`に全てを統合予定
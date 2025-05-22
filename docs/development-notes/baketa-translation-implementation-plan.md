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
│   ├── Models                             // データモデル（統一済み）
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
    
    /// <summary>
    /// テキストの言語を自動検出します
    /// </summary>
    Task<LanguageDetectionResult> DetectLanguageAsync(
        string text,
        CancellationToken cancellationToken = default);
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
- [x] 依存性注入の設定とモジュール登録の最適化
- [x] 各コンポーネントの統合テスト
- [x] 標準翻訳パイプラインの実装とテスト
- [x] トランザクション処理と一貫性確保の実装
- [x] 言語検出機能の実装と統合

##### 5. ドキュメント作成と名前空間統一 (5週目)
- [x] 名前空間の統一（`Baketa.Core.Translation.Models` に統一）
- [x] APIドキュメント生成
- [x] アーキテクチャ図の更新
- [x] ユーザーガイドの作成

#### フェーズ2: 拡張システム実装（#78, #79）
※基盤システム完了後に実施

##### 1. クラウドAI翻訳実装 (#78)
- [x] クラウドAI翻訳エンジンインターフェース(`ICloudTranslationEngine`)の設計
- [x] 高度な翻訳リクエスト/レスポンスモデルの実装
- [x] Gemini API連携基盤クラスの実装
- [x] 翻訳用プロンプトテンプレートの作成
- [x] APIステータス確認機能の実装
- [x] エラー処理の強化
- [x] コード品質の改善（警告解消）
- [ ] APIキー管理と認証システムの強化
- [ ] レイテンシおよびコスト最適化
- [ ] 文脈認識と複数文章の連続処理

##### 2. ローカル翻訳モデル実装 (#79)
- [x] ローカル翻訳エンジンインターフェース(`ILocalTranslationEngine`)の設計
- [x] コンピュートデバイスモデルの実装
- [x] モデルローダーインターフェースの設計
- [x] トークナイザーインターフェースの設計
- [x] ONNX翻訳エンジンの基本実装
- [x] エラー処理と例外処理の強化
- [x] コード品質の改善（警告解消）
- [ ] ONNXランタイム連携実装
- [ ] Helsinki-NLPモデルのロードと実行
- [ ] SentencePieceトークナイザーの実装
- [ ] モデルキャッシュとメモリ管理
- [ ] GPU加速対応

##### 3. 拡張機能の実装と統合
- [x] DI設定とサービス登録の実装
- [x] 拡張機能の基本テスト
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

現在の優先順位タスク（✅: 完了、⭕: 進行中、⏸: 保留）：

1. ✅ **パフォーマンステストと最適化** - ボトルネックの特定と改善
2. ✅ **名前空間の統一化** - 翻訳モデルの名前空間を`Baketa.Core.Translation.Models`に統一完了
3. ✅ **依存性注入の設定最適化** - 各コンポーネントの連携を強化
4. ✅ **統合テストの実施** - 各コンポーネントの連携テスト
5. ✅ **標準翻訳パイプラインの実装** - 実装完了、テスト済み
6. ✅ **言語検出機能の実装とテスト** - 実装完了、動作確認済み
7. ✅ **APIドキュメントの生成** - ドキュメントフェーズの一環として完了
8. ✅ **アーキテクチャ図の更新** - 新しい実装を反映した図面の更新完了
9. ✅ **クラウドAI翻訳処理系の実装** - `ICloudTranslationEngine`インターフェース実装、`GeminiTranslationEngine`基本実装完了
10. ✅ **ローカル翻訳モデルの実装** - `ILocalTranslationEngine`インターフェース実装、`OnnxTranslationEngine`基本実装完了
11. ✅ **DIサービス登録の実装** - `TranslationModule`の実装とDI拡張メソッドの追加
12. ✅ **コード品質向上とエラー修正** - コンパイルエラーの解決、警告の修正

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

### 5.3 名前空間競合の解決

名前空間競合問題は`Baketa.Core.Translation.Models`への統一によって解決されました。この作業により：

1. **名前空間の統一**：
   - すべての翻訳モデルが`Baketa.Core.Translation.Models`名前空間に集約
   - 型参照の曖昧さが完全に排除
   - 名前空間エイリアスの必要性が解消

2. **モデルの機能強化**：
   - `Language`クラス：地域コードの表現を`Code="zh-CN"`形式に統一
   - `TranslationRequest`/`TranslationResponse`の機能強化
   - エラー処理の強化とクローニング機能の追加

3. **コード品質の向上**：
   - IDE0300, IDE0301などの警告解消
   - テストカバレッジの維持と改善
   - メンテナンス性の向上

今後も名前空間の一貫性を維持するため、翻訳関連のモデルはすべて`Baketa.Core.Translation.Models`名前空間に追加する方針です。

## 6. タイムライン

### 6.1 基盤システム（フェーズ1）

| 週 | 主要タスク | 状態 |
|----|-----------|------|
| 1  | インターフェース設計と共通モデル定義、モック実装、テスト計画 | 完了 ✅ |
| 2  | 翻訳エンジン(#63)と結果管理(#64)の基本実装、キャッシュ(#53)の基本実装 | 完了 ✅ |
| 3  | イベントシステム(#65)の実装、各コンポーネントのユニットテスト | 完了 ✅ |
| 4  | 段階的統合と機能テスト、DIとサービス登録 | 完了 ✅ |
| 5  | 名前空間統一と最終化、パフォーマンス最適化、ドキュメント作成 | 完了 ✅ |

### 6.2 拡張システム（フェーズ2）

フェーズ1完了を受けて実装中：

| 週 | 主要タスク | 状態 |
|----|-----------|------|
| 1  | クラウドAI翻訳(#78)の基本実装、プロンプト設計 | 完了 ✅ |
| 1  | ローカル翻訳モデル(#79)の基本実装、ONNX連携 | 完了 ✅ |
| 2  | クラウドAI翻訳API統合、DI設定、テスト | 完了 ✅ |
| 2  | コンパイルエラーの修正と警告の解消 | 完了 ✅ |
| 3  | ONNXローカルモデル連携、特定モデル実装 | 予定 📅 |
| 3  | 高度な機能の実装、モデル管理システム | 予定 📅 |
| 4  | 両エンジンの統合と最適化、テスト | 予定 📅 |

## 7. 実装時の留意点

### 7.1 名前空間統一の完了と移行

`Baketa.Core.Models.Translation`と`Baketa.Core.Translation.Models`の競合問題は完全に解決されました。

**統一名前空間**:
- 標準名前空間として`Baketa.Core.Translation.Models`を採用
- すべてのクラスが統合され、機能が強化
- Obsolete属性で移行期間を経て完全に統一

**統一の効果**:
- コード内での型参照の曖昧さを排除
- 名前空間エイリアスが不要に
- モデルクラスの機能強化と標準化

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
  - `ConfigureAwait(false)`の適切な使用

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

フェーズ2（拡張システム）の基本実装が完了しました。次はONNXモデルの実装と詳細な機能の追加に取り組みます。フェーズ2では、クラウドAI翻訳処理系（#78）とローカル翻訳モデル（#79）の実装を同時に進めています。

基本インターフェース、主要クラス、DIサービス登録が実装され、コンパイルエラーも解消されたため、今後は以下の点に注力します：

1. **ONNX実行環境の構築**：
   - ONNXランタイムの統合
   - SentencePieceトークナイザーの実装
   - GPU支援の有効化

2. **Gemini API連携の最適化**：
   - プロンプト最適化
   - レート制限およびコスト管理
   - キャッシュ戦略の実装

3. **翻訳品質の向上**：
   - 後処理の実装
   - コンテキスト保持機能
   - 専門用語の一貫性確保

## 9. フェーズ2のフォーカス

フェーズ2では以下の点に重点を置いて開発を進めます：

### 9.1 クラウドAI翻訳処理系の実装 (#78)

Gemini APIを活用した高品質翻訳の実現に向けて：

- **Gemini APIクライアントの実装**
  - 認証と接続管理
  - エラーハンドリングとリトライ
  - レート制限への対応

- **プロンプトエンジニアリング**
  - 翻訳品質の最適化
  - 専門用語への対応
  - コンテキスト認識の強化

- **コスト最適化**
  - 効率的なトークン使用
  - バッチ処理による最適化
  - キャッシュ戦略

### 9.2 ローカル翻訳モデルの実装 (#79)

Helsinki-NLP OPUS-MT ONNXモデルの統合：

- **ONNXランタイム連携**
  - モデル読み込みと初期化
  - 推論エンジンの実装
  - バッチ処理の最適化

- **SentencePieceトークナイザー**
  - トークン化処理の実装
  - サブワード処理の最適化
  - マルチ言語対応

- **モデル管理**
  - 効率的なリソース利用
  - 動的モデルロード
  - モデルの更新メカニズム

### 9.3 統合と品質向上

両翻訳エンジンの効果的な連携と品質向上：

- **エンジン選択ロジック**
  - 最適なエンジンの自動選択
  - フォールバックメカニズム
  - ユーザー設定による制御

- **翻訳品質向上**
  - 後処理の最適化
  - コンテキスト保持
  - 専門用語の一貫性確保

- **ヒューマンフィードバックの統合**
  - 翻訳品質のフィードバック
  - 学習データへの反映
  - カスタマイズオプション

これらの取り組みにより、Baketaが提供する翻訳機能の品質と応答性を大幅に向上させ、ユーザー体験の改善につなげていきます。
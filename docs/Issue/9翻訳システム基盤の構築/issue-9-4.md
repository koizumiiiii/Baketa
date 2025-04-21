# Issue 9-4: 翻訳イベントシステムの実装

## 概要
翻訳処理の結果や状態をシステム全体に通知するためのイベントシステムを設計・実装します。これにより、翻訳サブシステムと他のコンポーネント（UI、OCR等）間の疎結合な連携が可能になります。

## 目的・理由
翻訳イベントシステムは以下の理由で必要です：

1. 翻訳サブシステムと他のコンポーネント間の疎結合を実現する
2. 翻訳結果をリアルタイムでUI層に伝達する手段を提供する
3. 翻訳状態の変更（開始、完了、エラー等）を適切に通知する
4. プラグイン開発や機能拡張の基盤となる

## 詳細
- 翻訳関連イベントの設計と実装
- イベント発行と購読の仕組みの実装
- イベントデータモデルの設計と実装
- イベント処理の最適化

## タスク分解
- [ ] 翻訳イベント型の定義
  - [ ] `TranslationCompletedEvent`クラスの設計と実装
  - [ ] `TranslationStartedEvent`クラスの設計と実装
  - [ ] `TranslationErrorEvent`クラスの設計と実装
  - [ ] `TranslationProgressEvent`クラスの設計と実装
  - [ ] `TranslationCacheEvent`クラスの設計と実装
- [ ] イベントハンドラーの実装
  - [ ] `ITranslationEventHandler<TEvent>`インターフェースの設計
  - [ ] 基本ハンドラー実装
- [ ] イベント発行機能の実装
  - [ ] `TranslationService`へのイベント発行機能の統合
  - [ ] `TranslationManager`へのイベント発行機能の統合
- [ ] イベント購読機能の実装
  - [ ] イベント購読メカニズムの実装
  - [ ] 購読フィルターの実装
- [ ] UIコンポーネントとの連携
  - [ ] UI更新ハンドラーの実装
  - [ ] オーバーレイ更新連携の実装
- [ ] パフォーマンス最適化
  - [ ] イベントバッファリングの実装
  - [ ] イベントスロットリングの実装
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.Translation.Events
{
    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    public class TranslationCompletedEvent : IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid EventId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
        
        /// <summary>
        /// 翻訳リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }
        
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public required string TranslatedText { get; set; }
        
        /// <summary>
        /// 翻訳元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <summary>
        /// 使用された翻訳エンジン名
        /// </summary>
        public required string EngineName { get; set; }
        
        /// <summary>
        /// 翻訳処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// キャッシュヒットフラグ
        /// </summary>
        public bool IsCacheHit { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト
        /// </summary>
        public TranslationContext? Context { get; set; }
        
        /// <summary>
        /// 信頼度スコア
        /// </summary>
        public float ConfidenceScore { get; set; }
        
        /// <summary>
        /// 関連する画面領域
        /// </summary>
        public Rectangle? ScreenRegion { get; set; }
        
        /// <summary>
        /// 追加メタデータ
        /// </summary>
        public Dictionary<string, object?> Metadata { get; } = new();
    }
    
    /// <summary>
    /// 翻訳開始イベント
    /// </summary>
    public class TranslationStartedEvent : IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid EventId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
        
        /// <summary>
        /// 翻訳リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }
        
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
        /// 使用される翻訳エンジン名
        /// </summary>
        public required string EngineName { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト
        /// </summary>
        public TranslationContext? Context { get; set; }
        
        /// <summary>
        /// 関連する画面領域
        /// </summary>
        public Rectangle? ScreenRegion { get; set; }
    }
    
    /// <summary>
    /// 翻訳エラーイベント
    /// </summary>
    public class TranslationErrorEvent : IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid EventId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
        
        /// <summary>
        /// 翻訳リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }
        
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
        /// 使用された翻訳エンジン名
        /// </summary>
        public required string EngineName { get; set; }
        
        /// <summary>
        /// エラーコード
        /// </summary>
        public required string ErrorCode { get; set; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public required string ErrorMessage { get; set; }
        
        /// <summary>
        /// 詳細なエラー情報
        /// </summary>
        public string? ErrorDetails { get; set; }
        
        /// <summary>
        /// エラーの原因となった例外
        /// </summary>
        public Exception? Exception { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト
        /// </summary>
        public TranslationContext? Context { get; set; }
        
        /// <summary>
        /// リトライ可能かどうか
        /// </summary>
        public bool IsRetryable { get; set; }
        
        /// <summary>
        /// リトライ回数
        /// </summary>
        public int RetryCount { get; set; }
    }
    
    /// <summary>
    /// 翻訳進捗イベント
    /// </summary>
    public class TranslationProgressEvent : IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid EventId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
        
        /// <summary>
        /// 翻訳リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }
        
        /// <summary>
        /// 進捗率（0～100）
        /// </summary>
        public float ProgressPercentage { get; set; }
        
        /// <summary>
        /// 進捗状態メッセージ
        /// </summary>
        public string? StatusMessage { get; set; }
        
        /// <summary>
        /// バッチ処理の場合の現在項目インデックス
        /// </summary>
        public int? CurrentItemIndex { get; set; }
        
        /// <summary>
        /// バッチ処理の場合の合計項目数
        /// </summary>
        public int? TotalItems { get; set; }
        
        /// <summary>
        /// 残り時間の推定（秒）
        /// </summary>
        public double? EstimatedRemainingSeconds { get; set; }
    }
    
    /// <summary>
    /// 翻訳キャッシュイベント
    /// </summary>
    public class TranslationCacheEvent : IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid EventId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
        
        /// <summary>
        /// キャッシュ操作タイプ
        /// </summary>
        public CacheOperationType OperationType { get; set; }
        
        /// <summary>
        /// 対象レコードID
        /// </summary>
        public Guid? RecordId { get; set; }
        
        /// <summary>
        /// 影響を受けたレコード数
        /// </summary>
        public int AffectedRecordsCount { get; set; }
        
        /// <summary>
        /// キャッシュヒット率（%）
        /// </summary>
        public float? CacheHitRate { get; set; }
        
        /// <summary>
        /// キャッシュサイズ（レコード数）
        /// </summary>
        public int? CacheSize { get; set; }
        
        /// <summary>
        /// 操作の詳細
        /// </summary>
        public string? OperationDetails { get; set; }
    }
    
    /// <summary>
    /// キャッシュ操作タイプ
    /// </summary>
    public enum CacheOperationType
    {
        /// <summary>
        /// キャッシュ追加
        /// </summary>
        Add,
        
        /// <summary>
        /// キャッシュ更新
        /// </summary>
        Update,
        
        /// <summary>
        /// キャッシュ削除
        /// </summary>
        Remove,
        
        /// <summary>
        /// キャッシュヒット
        /// </summary>
        Hit,
        
        /// <summary>
        /// キャッシュミス
        /// </summary>
        Miss,
        
        /// <summary>
        /// キャッシュクリア
        /// </summary>
        Clear,
        
        /// <summary>
        /// キャッシュ統計
        /// </summary>
        Statistics
    }
    
    /// <summary>
    /// 翻訳イベントハンドラーインターフェース
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    public interface ITranslationEventHandler<TEvent> : IEventHandler<TEvent>
        where TEvent : IEvent
    {
        /// <summary>
        /// ハンドラーの優先度
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// イベントをハンドルします
        /// </summary>
        /// <param name="event">イベント</param>
        /// <returns>ハンドリング結果</returns>
        new Task<bool> HandleAsync(TEvent @event);
    }
    
    /// <summary>
    /// 翻訳イベント発行インターフェース
    /// </summary>
    public interface ITranslationEventPublisher
    {
        /// <summary>
        /// イベントを発行します
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="event">発行するイベント</param>
        /// <returns>発行結果</returns>
        Task<bool> PublishEventAsync<TEvent>(TEvent @event) where TEvent : IEvent;
        
        /// <summary>
        /// 翻訳完了イベントを発行します
        /// </summary>
        /// <param name="response">翻訳レスポンス</param>
        /// <param name="context">翻訳コンテキスト</param>
        /// <param name="screenRegion">画面領域</param>
        /// <returns>発行結果</returns>
        Task<bool> PublishTranslationCompletedAsync(
            TranslationResponse response, 
            TranslationContext? context = null, 
            Rectangle? screenRegion = null);
            
        /// <summary>
        /// 翻訳開始イベントを発行します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="engineName">エンジン名</param>
        /// <param name="screenRegion">画面領域</param>
        /// <returns>発行結果</returns>
        Task<bool> PublishTranslationStartedAsync(
            TranslationRequest request, 
            string engineName, 
            Rectangle? screenRegion = null);
            
        /// <summary>
        /// 翻訳エラーイベントを発行します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="engineName">エンジン名</param>
        /// <param name="error">エラー情報</param>
        /// <param name="isRetryable">リトライ可能かどうか</param>
        /// <param name="retryCount">リトライ回数</param>
        /// <returns>発行結果</returns>
        Task<bool> PublishTranslationErrorAsync(
            TranslationRequest request, 
            string engineName, 
            TranslationError error, 
            bool isRetryable = false, 
            int retryCount = 0);
    }
}
```

## 実装上の注意点
- イベントハンドラーは非同期で実行し、イベント発行元をブロックしないようにする
- イベントオブジェクトはイミュータブルにし、スレッド安全性を確保する
- イベントのバッファリングやスロットリングを実装し、高頻度イベントの処理を最適化する
- メモリリークを防ぐため、イベントハンドラーの登録と解除のライフサイクルを適切に管理する
- 例外発生時にもイベントシステム全体が機能停止しないよう、ハンドラー内の例外を適切に処理する
- イベントのシリアライズ/デシリアライズを効率的に行える設計にする

## 関連Issue/参考
- 親Issue: #9 翻訳システム基盤の構築
- 依存Issue: #4 イベント集約機構の構築
- 依存Issue: #9-1 翻訳エンジンインターフェースの設計と実装
- 依存Issue: #9-3 翻訳結果モデルと管理システムの実装
- 参照: E:\dev\Baketa\docs\3-architecture\core\event-system.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3. 非同期プログラミング)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: translation`

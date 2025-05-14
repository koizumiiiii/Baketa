# 翻訳イベントシステムのインターフェース設計

Baketaプロジェクトの翻訳イベントシステムは、翻訳処理の各段階で発生するイベントを管理し、
関連コンポーネント間の疎結合な通信を実現するためのものです。

## 1. 基本イベントインターフェース

```csharp
namespace Baketa.Core.Events
{
    /// <summary>
    /// すべてのイベントの基本インターフェース
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// イベントの一意識別子
        /// </summary>
        Guid EventId { get; }
        
        /// <summary>
        /// イベントが発生した時刻
        /// </summary>
        DateTimeOffset Timestamp { get; }
    }
    
    /// <summary>
    /// イベントハンドラーのインターフェース
    /// </summary>
    /// <typeparam name="TEvent">処理対象のイベントタイプ</typeparam>
    public interface IEventHandler<TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// イベント処理メソッド
        /// </summary>
        /// <param name="event">処理対象のイベント</param>
        /// <returns>非同期タスク</returns>
        Task HandleAsync(TEvent @event);
    }
}
```

## 2. 翻訳特化イベント

```csharp
namespace Baketa.Core.Events.Translation
{
    /// <summary>
    /// 翻訳関連イベントの基本インターフェース
    /// </summary>
    public interface ITranslationEvent : IEvent
    {
        /// <summary>
        /// 翻訳対象のテキスト
        /// </summary>
        string SourceText { get; }
        
        /// <summary>
        /// 翻訳元言語
        /// </summary>
        string SourceLanguage { get; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        string TargetLanguage { get; }
    }
    
    /// <summary>
    /// 翻訳リクエスト開始イベント
    /// </summary>
    public interface ITranslationRequestedEvent : ITranslationEvent
    {
        /// <summary>
        /// リクエスト識別子
        /// </summary>
        string RequestId { get; }
        
        /// <summary>
        /// 翻訳元のコンテキスト情報
        /// </summary>
        ITranslationContext Context { get; }
    }
    
    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    public interface ITranslationCompletedEvent : ITranslationEvent
    {
        /// <summary>
        /// 元のリクエスト識別子
        /// </summary>
        string RequestId { get; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        string TranslatedText { get; }
        
        /// <summary>
        /// 翻訳元のコンテキスト情報
        /// </summary>
        ITranslationContext Context { get; }
        
        /// <summary>
        /// 翻訳にかかった時間（ミリ秒）
        /// </summary>
        long ProcessingTimeMs { get; }
        
        /// <summary>
        /// 使用された翻訳エンジン
        /// </summary>
        string TranslationEngine { get; }
    }
    
    /// <summary>
    /// 翻訳失敗イベント
    /// </summary>
    public interface ITranslationFailedEvent : ITranslationEvent
    {
        /// <summary>
        /// 元のリクエスト識別子
        /// </summary>
        string RequestId { get; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        string ErrorMessage { get; }
        
        /// <summary>
        /// エラータイプ
        /// </summary>
        TranslationErrorType ErrorType { get; }
    }
    
    /// <summary>
    /// キャッシュヒットイベント
    /// </summary>
    public interface ITranslationCacheHitEvent : ITranslationEvent
    {
        /// <summary>
        /// 元のリクエスト識別子
        /// </summary>
        string RequestId { get; }
        
        /// <summary>
        /// キャッシュから取得された翻訳結果
        /// </summary>
        string TranslatedText { get; }
        
        /// <summary>
        /// キャッシュエントリの有効期限
        /// </summary>
        DateTimeOffset? ExpiresAt { get; }
    }
}
```

## 3. 翻訳コンテキストインターフェース

```csharp
namespace Baketa.Core.Translation
{
    /// <summary>
    /// 翻訳コンテキスト情報のインターフェース
    /// </summary>
    public interface ITranslationContext
    {
        /// <summary>
        /// 翻訳対象のアプリケーション名
        /// </summary>
        string ApplicationName { get; }
        
        /// <summary>
        /// 翻訳が行われた画面またはコンテキスト
        /// </summary>
        string ScreenContext { get; }
        
        /// <summary>
        /// テキストが検出された画面上の位置
        /// </summary>
        Rectangle TextRegion { get; }
        
        /// <summary>
        /// 追加のコンテキスト情報（キー・バリューペア）
        /// </summary>
        IReadOnlyDictionary<string, string> AdditionalContext { get; }
    }
}
```

## 4. イベント発行と購読のサンプル実装

```csharp
// 翻訳サービスでのイベント発行例
public class TranslationService : ITranslationService
{
    private readonly IEventAggregator _eventAggregator;
    
    public TranslationService(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }
    
    public async Task<string> TranslateAsync(string sourceText, string sourceLanguage, string targetLanguage)
    {
        // 翻訳リクエストイベントの発行
        var requestEvent = new TranslationRequestedEvent(sourceText, sourceLanguage, targetLanguage);
        await _eventAggregator.PublishAsync(requestEvent);
        
        try
        {
            // 翻訳処理
            string result = await PerformTranslation(sourceText, sourceLanguage, targetLanguage);
            
            // 翻訳完了イベントの発行
            var completedEvent = new TranslationCompletedEvent(requestEvent.RequestId, sourceText, 
                result, sourceLanguage, targetLanguage);
            await _eventAggregator.PublishAsync(completedEvent);
            
            return result;
        }
        catch (Exception ex)
        {
            // 翻訳失敗イベントの発行
            var failedEvent = new TranslationFailedEvent(requestEvent.RequestId, sourceText,
                sourceLanguage, targetLanguage, ex.Message);
            await _eventAggregator.PublishAsync(failedEvent);
            
            throw;
        }
    }
}

// 統計収集のためのイベントハンドラー例
public class TranslationStatisticsCollector : IEventHandler<ITranslationCompletedEvent>, IEventHandler<ITranslationFailedEvent>
{
    private readonly IStatisticsRepository _statisticsRepository;
    
    public TranslationStatisticsCollector(IStatisticsRepository statisticsRepository)
    {
        _statisticsRepository = statisticsRepository;
    }
    
    public async Task HandleAsync(ITranslationCompletedEvent @event)
    {
        await _statisticsRepository.RecordSuccessfulTranslation(
            @event.SourceLanguage,
            @event.TargetLanguage,
            @event.ProcessingTimeMs,
            @event.TranslationEngine);
    }
    
    public async Task HandleAsync(ITranslationFailedEvent @event)
    {
        await _statisticsRepository.RecordFailedTranslation(
            @event.SourceLanguage,
            @event.TargetLanguage,
            @event.ErrorType.ToString());
    }
}
```
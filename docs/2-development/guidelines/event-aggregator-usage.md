# イベント集約機構 使用ガイド

*作成: 2025年5月3日*

このドキュメントでは、Baketaプロジェクトにおけるイベント集約機構（Event Aggregator）の使用方法と推奨パターンについて説明します。

## 1. イベント集約機構の概要

イベント集約機構は、モジュール間の疎結合なコミュニケーションを実現するための核となるパターンです。この機構により、発行者（Publisher）と購読者（Subscriber）が互いを直接参照することなくイベントを通じて通信できます。

### 1.1 主要なインターフェース

- **IEvent**: すべてのイベントの基底インターフェース
- **IEventProcessor\<TEvent\>**: イベント処理を定義するインターフェース
- **IEventAggregator**: イベントの発行と購読を管理するインターフェース

### 1.2 イベント集約機構の利点

- **疎結合**: コンポーネント間の直接的な依存関係を減らします
- **拡張性**: 新機能追加時に既存コードを変更せず、新しいイベントハンドラーを追加するだけで拡張できます
- **テスト容易性**: 各コンポーネントが独立しているため、単体テストが容易になります
- **並行処理**: イベント処理を効率的に並列化できます

## 2. 基本的な使用方法

### 2.1 イベントの定義

イベントは `IEvent` インターフェースを実装する必要があります：

```csharp
// 基本イベントクラス
public abstract class EventBase : IEvent
{
    protected EventBase()
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }
    
    public Guid Id { get; }
    public DateTime Timestamp { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
}

// 具体的なイベント
public class TextDetectedEvent : EventBase
{
    public TextDetectedEvent(string detectedText, Rectangle region)
    {
        DetectedText = detectedText;
        Region = region;
    }
    
    public string DetectedText { get; }
    public Rectangle Region { get; }
    
    public override string Name => "TextDetected";
    public override string Category => "OCR";
}
```

### 2.2 イベントプロセッサの実装

`IEventProcessor<TEvent>` インターフェースを実装します：

```csharp
public class TranslationProcessor : IEventProcessor<TextDetectedEvent>
{
    private readonly ITranslationService _translationService;
    private readonly ILogger<TranslationProcessor> _logger;
    
    public TranslationProcessor(
        ITranslationService translationService,
        ILogger<TranslationProcessor> logger)
    {
        _translationService = translationService;
        _logger = logger;
    }
    
    public async Task HandleAsync(TextDetectedEvent eventData)
    {
        try
        {
            _logger.LogInformation("テキスト検出イベントを処理: {Text}", eventData.DetectedText);
            
            // 検出されたテキストを翻訳
            var translatedText = await _translationService.TranslateAsync(
                eventData.DetectedText,
                SourceLanguage.Japanese,
                TargetLanguage.English);
                
            _logger.LogInformation("翻訳完了: {OriginalText} -> {TranslatedText}", 
                eventData.DetectedText, translatedText);
                
            // 翻訳結果の処理...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト翻訳中にエラーが発生しました: {Text}", eventData.DetectedText);
            throw; // エラーを上位レイヤーに伝播
        }
    }
}
```

### 2.3 イベントプロセッサの登録

```csharp
// コンストラクタインジェクション
public class TranslationService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly TranslationProcessor _translationProcessor;
    
    public TranslationService(
        IEventAggregator eventAggregator,
        TranslationProcessor translationProcessor)
    {
        _eventAggregator = eventAggregator;
        _translationProcessor = translationProcessor;
        
        // イベントプロセッサを登録
        _eventAggregator.Subscribe(_translationProcessor);
    }
    
    // サービスの終了時に登録解除することを忘れないでください（必要に応じて）
    public void Dispose()
    {
        _eventAggregator.Unsubscribe(_translationProcessor);
    }
}
```

### 2.4 イベントの発行

```csharp
public class OcrService
{
    private readonly IEventAggregator _eventAggregator;
    
    public OcrService(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }
    
    public async Task ProcessImageAsync(IImage image)
    {
        // OCR処理...
        var detectedTexts = await PerformOcrAsync(image);
        
        // 検出されたテキストごとにイベントを発行
        foreach (var textResult in detectedTexts)
        {
            await _eventAggregator.PublishAsync(
                new TextDetectedEvent(textResult.Text, textResult.Region));
        }
    }
}
```

## 3. 推奨パターン

### 3.1 イベント設計のベストプラクティス

1. **単一責任の原則**: 各イベントは明確に定義された単一の通知を表現するべきです
2. **不変性**: イベントはイミュータブルに設計します（読み取り専用プロパティのみ）
3. **カテゴリ分類**: 関連するイベントは同じカテゴリにまとめます
4. **必要最小限のデータ**: イベントに含めるデータは必要最小限に抑えます

### 3.2 イベントプロセッサの設計

1. **単一責任**: 各プロセッサは明確に定義された単一の責任を持つべきです
2. **適切なロギング**: 処理開始、完了、エラー時のログ出力を行います
3. **例外処理**: 適切なエラーハンドリングを実装します
4. **短い処理時間**: 長時間実行される処理は避け、必要に応じてバックグラウンドタスクに移行します

### 3.3 イベント発行のベストプラクティス

1. **適切なタイミング**: イベントの発行は適切なタイミングで行います（処理の完了後など）
2. **頻度の考慮**: 高頻度でのイベント発行は避けます（例：毎フレームなど）
3. **キャンセレーション対応**: 長時間実行される処理ではキャンセレーショントークンを使用します
4. **エラー処理**: イベント発行中のエラーを適切に処理します

### 3.4 イベントカテゴリの標準化

Baketaプロジェクトでは以下のカテゴリを標準として使用します：

| カテゴリ名 | 用途 |
|------------|------|
| `Capture` | 画面キャプチャ関連イベント |
| `OCR` | テキスト検出・認識関連イベント |
| `Translation` | 翻訳処理関連イベント |
| `UI` | ユーザーインターフェース関連イベント |
| `System` | アプリケーション全体やシステム関連イベント |
| `Settings` | 設定変更関連イベント |

## 4. DIでの登録と使用

### 4.1 基本的な登録

```csharp
// プログラム起動時（例：Program.cs）
services.AddSingleton<IEventAggregator, EventAggregator>();

// イベントプロセッサの登録
services.AddTransient<TextDetectedProcessor>();
services.AddTransient<CaptureCompletedProcessor>();
```

### 4.2 自動サブスクリプション

複数のプロセッサを自動的に登録するヘルパーサービスの例：

```csharp
public class EventSubscriptionService
{
    private readonly IEventAggregator _eventAggregator;
    
    public EventSubscriptionService(
        IEventAggregator eventAggregator,
        IEnumerable<IEventProcessor<TextDetectedEvent>> textDetectedProcessors,
        IEnumerable<IEventProcessor<CaptureCompletedEvent>> captureCompletedProcessors)
    {
        _eventAggregator = eventAggregator;
        
        // すべてのテキスト検出イベントプロセッサを登録
        foreach (var processor in textDetectedProcessors)
        {
            _eventAggregator.Subscribe(processor);
        }
        
        // すべてのキャプチャ完了イベントプロセッサを登録
        foreach (var processor in captureCompletedProcessors)
        {
            _eventAggregator.Subscribe(processor);
        }
    }
}

// DIでの登録
services.AddSingleton<EventSubscriptionService>();
services.AddHostedService<EventSubscriptionHostedService>();
```

## 5. 実装例

### 5.1 キャプチャ完了から翻訳までのフロー例

```csharp
// 1. キャプチャ完了イベント
public class CaptureCompletedEvent : EventBase
{
    public CaptureCompletedEvent(IImage capturedImage)
    {
        CapturedImage = capturedImage;
    }
    
    public IImage CapturedImage { get; }
    
    public override string Name => "CaptureCompleted";
    public override string Category => "Capture";
}

// 2. OCRプロセッサ（キャプチャ完了イベントを処理）
public class OcrProcessor : IEventProcessor<CaptureCompletedEvent>
{
    private readonly IOcrService _ocrService;
    private readonly IEventAggregator _eventAggregator;
    
    public OcrProcessor(
        IOcrService ocrService,
        IEventAggregator eventAggregator)
    {
        _ocrService = ocrService;
        _eventAggregator = eventAggregator;
    }
    
    public async Task HandleAsync(CaptureCompletedEvent eventData)
    {
        // キャプチャ画像からテキストを検出
        var ocrResults = await _ocrService.RecognizeTextAsync(eventData.CapturedImage);
        
        // OCR完了イベントを発行
        await _eventAggregator.PublishAsync(
            new OcrCompletedEvent(eventData.CapturedImage, ocrResults));
    }
}

// 3. OCR完了イベント
public class OcrCompletedEvent : EventBase
{
    public OcrCompletedEvent(IImage sourceImage, IEnumerable<OcrTextResult> detectedTexts)
    {
        SourceImage = sourceImage;
        DetectedTexts = detectedTexts;
    }
    
    public IImage SourceImage { get; }
    public IEnumerable<OcrTextResult> DetectedTexts { get; }
    
    public override string Name => "OcrCompleted";
    public override string Category => "OCR";
}

// 4. 翻訳プロセッサ（OCR完了イベントを処理）
public class TranslationProcessor : IEventProcessor<OcrCompletedEvent>
{
    private readonly ITranslationService _translationService;
    private readonly IEventAggregator _eventAggregator;
    
    public TranslationProcessor(
        ITranslationService translationService,
        IEventAggregator eventAggregator)
    {
        _translationService = translationService;
        _eventAggregator = eventAggregator;
    }
    
    public async Task HandleAsync(OcrCompletedEvent eventData)
    {
        // 検出された各テキストを翻訳
        var translationTasks = eventData.DetectedTexts.Select(async text => 
        {
            var translatedText = await _translationService.TranslateAsync(
                text.Text, 
                SourceLanguage.Japanese, 
                TargetLanguage.English);
                
            return new TranslationResult
            {
                OriginalText = text.Text,
                TranslatedText = translatedText,
                Region = text.Region
            };
        });
        
        var translationResults = await Task.WhenAll(translationTasks);
        
        // 翻訳完了イベントを発行
        await _eventAggregator.PublishAsync(
            new TranslationCompletedEvent(eventData.SourceImage, translationResults));
    }
}
```

### 5.2 キャンセレーション対応の例

```csharp
public class LongRunningProcessor : IEventProcessor<StartLongProcessingEvent>
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<LongRunningProcessor> _logger;
    
    public LongRunningProcessor(
        IEventAggregator eventAggregator,
        ILogger<LongRunningProcessor> logger)
    {
        _eventAggregator = eventAggregator;
        _logger = logger;
    }
    
    public async Task HandleAsync(StartLongProcessingEvent eventData)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30秒タイムアウト
        
        try
        {
            // 長時間実行される処理
            await _eventAggregator.PublishAsync(
                new LongRunningProcessEvent(eventData.Data),
                cts.Token);
                
            _logger.LogInformation("長時間処理が完了しました");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("長時間処理がキャンセルされました");
            
            // キャンセル時の処理...
        }
    }
}
```

## 6. リファレンス

- [イベントシステム概要](../../3-architecture/event-system/event-system-overview.md)
- [イベント実装ガイド](../../3-architecture/event-system/event-implementation-guide.md)
- [イベント集約機構の設計](../../3-architecture/improved-architecture.md#7-イベント集約機構の設計)
- [Issue #24: イベント集約機構の構築](../../.github/issues/issue_24.md)
- [Issue #27: イベント集約管理機能の実装](../../.github/issues/issue_27.md)
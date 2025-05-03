# イベント集約機構 概要

## 1. 概要

イベント集約機構（Event Aggregator）は、Baketaアプリケーションにおけるコンポーネント間の疎結合な通信を実現するための中核機能です。この機構により、モジュール間の直接的な依存関係を減らし、メンテナンス性・テスト容易性の高いシステムを構築します。

## 2. 主要コンポーネント

イベント集約機構は以下の主要コンポーネントから構成されています：

### 2.1 基本インターフェース

- **IEvent**: すべてのイベントの基底インターフェース
- **IEventProcessor\<TEvent\>**: イベント処理を定義するインターフェース
- **IEventAggregator**: イベントの発行と購読を管理するインターフェース

### 2.2 実装クラス

- **EventAggregator**: イベント集約機構の中核となる実装
- **EventProcessorMetrics**: イベント処理のパフォーマンス測定機能
- **EventAggregatorServiceExtensions**: DI登録用拡張メソッド

## 3. 主要機能

### 3.1 イベント発行（Publish）

```csharp
// イベント発行の例
await eventAggregator.PublishAsync(new CaptureCompletedEvent(image));
```

- 非同期イベント発行（`PublishAsync<TEvent>`）
- キャンセレーショントークン対応の発行
- ハンドラー呼び出しの並列実行
- イベント処理のエラーハンドリング

### 3.2 イベント購読（Subscribe）

```csharp
// イベント購読の例
eventAggregator.Subscribe(captureCompletedProcessor);
```

- タイプセーフなイベント購読
- 重複登録の自動回避
- スレッドセーフな実装

### 3.3 イベント購読解除（Unsubscribe）

```csharp
// イベント購読解除の例
eventAggregator.Unsubscribe(captureCompletedProcessor);
```

- タイプセーフな購読解除
- リソースリークの防止

### 3.4 パフォーマンス測定

- イベント処理時間の測定
- プロセッサ別の成功率と失敗率の記録
- 詳細なパフォーマンスレポート生成

## 4. 設計のメリット

### 4.1 疎結合化

モジュール間が直接的な参照ではなく、イベントを介して通信することで疎結合なシステムを実現します。例えば、キャプチャモジュールはOCRモジュールを直接参照する必要がなく、キャプチャ完了イベントを発行するだけで済みます。

### 4.2 拡張性

新しい機能や動作を追加する際に、既存のコードを変更せずにイベントの購読者を追加するだけで実現できます。例えば、デバッグログ記録機能を追加したい場合は、既存コードを変更せずに新しいイベントハンドラーを追加するだけです。

### 4.3 テスト容易性

各コンポーネントが疎結合になることで、単体テストが容易になります。モックやスタブを使用して、イベントの発行や処理をテストできます。

### 4.4 並行処理のサポート

イベント処理を並列に実行可能で、パフォーマンスを向上させながらも安全なスレッド処理を実現しています。

## 5. 利用パターン

### 5.1 基本パターン

```csharp
// 1. イベントを定義
public class TextDetectedEvent : IEvent
{
    public TextDetectedEvent(string detectedText, Rectangle region)
    {
        DetectedText = detectedText;
        Region = region;
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }
    
    public string DetectedText { get; }
    public Rectangle Region { get; }
    public Guid Id { get; }
    public DateTime Timestamp { get; }
    public string Name => "TextDetected";
    public string Category => "OCR";
}

// 2. イベントプロセッサを実装
public class TranslationProcessor : IEventProcessor<TextDetectedEvent>
{
    private readonly ITranslationService _translationService;
    
    public TranslationProcessor(ITranslationService translationService)
    {
        _translationService = translationService;
    }
    
    public async Task HandleAsync(TextDetectedEvent eventData)
    {
        // 検出されたテキストを翻訳
        var translatedText = await _translationService.TranslateAsync(
            eventData.DetectedText,
            SourceLanguage.Japanese,
            TargetLanguage.English);
            
        // 翻訳結果の処理...
    }
}

// 3. イベントプロセッサを登録
eventAggregator.Subscribe(translationProcessor);

// 4. イベントを発行
await eventAggregator.PublishAsync(new TextDetectedEvent("検出テキスト", new Rectangle(10, 10, 100, 20)));
```

### 5.2 DIとの統合

アプリケーション起動時にイベント集約機構とプロセッサを登録します：

```csharp
// サービス登録の例
services.AddEventAggregator(); // IEventAggregatorを登録
services.AddTransient<TranslationProcessor>(); // 各プロセッサを登録

// 自動購読のためのヘルパーサービス
services.AddSingleton<EventSubscriptionService>();
```

```csharp
// 自動イベント購読サービスの例
public class EventSubscriptionService
{
    private readonly IEventAggregator _eventAggregator;
    
    public EventSubscriptionService(
        IEventAggregator eventAggregator,
        IEnumerable<TranslationProcessor> translationProcessors,
        IEnumerable<OcrProcessor> ocrProcessors)
    {
        _eventAggregator = eventAggregator;
        
        // すべてのプロセッサを自動登録
        foreach (var processor in translationProcessors)
        {
            _eventAggregator.Subscribe(processor);
        }
        
        foreach (var processor in ocrProcessors)
        {
            _eventAggregator.Subscribe(processor);
        }
    }
}
```

## 6. 既知の制限事項

- 現在のイベント処理は非同期ですが、イベントの順序保証はありません。
- イベントはメモリ内で処理され、永続化されません。
- 大量のイベントを短時間に処理する場合、メモリ使用量に注意が必要です。

## 7. 将来の拡張ポイント

- イベント履歴の記録と再生機能
- イベントの優先順位付け
- 条件付きイベント購読（フィルタリング機能）
- イベントの遅延実行とスケジューリング

## 8. 参考資料

- [Improved Architecture Documentation](../improved-architecture.md#7-イベント集約機構の設計)
- [Event Aggregator Pattern](https://martinfowler.com/eaaDev/EventAggregator.html)
- [Issue #24: イベント集約機構の構築](../../.github/issues/issue_24.md)
- [Issue #27: イベント集約管理機能の実装](../../.github/issues/issue_27.md)
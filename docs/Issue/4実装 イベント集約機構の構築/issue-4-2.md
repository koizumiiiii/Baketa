# 実装: イベント型とハンドラーの実装

## 概要
アプリケーションで使用する具体的なイベント型とそのハンドラーを設計・実装します。

## 目的・理由
アプリケーションの主要機能間の連携をイベントベースで実現するために、適切なイベント型の定義が必要です。また、これらのイベントに対応するハンドラーを実装することで、機能間の疎結合なコミュニケーションを実現します。

## 詳細
- 基本イベント型の定義と実装
- 機能領域別のイベント型の設計
- イベントハンドラーの実装パターン確立
- イベントデータの最適化

## タスク分解
- [ ] 基本イベント抽象クラスの実装
  - [ ] `EventBase`抽象クラスの実装
  - [ ] イベント共通プロパティの実装
- [ ] キャプチャ関連イベントの実装
  - [ ] `CaptureCompletedEvent`の実装
  - [ ] `CaptureFailedEvent`の実装
- [ ] OCR関連イベントの実装
  - [ ] `OcrCompletedEvent`の実装
  - [ ] `OcrProgressEvent`の実装
  - [ ] `OcrFailedEvent`の実装
- [ ] 翻訳関連イベントの実装
  - [ ] `TranslationCompletedEvent`の実装
  - [ ] `TranslationFailedEvent`の実装
- [ ] UI通知イベントの実装
  - [ ] `NotificationEvent`の実装
  - [ ] `OverlayUpdateEvent`の実装
- [ ] システムイベントの実装
  - [ ] `ApplicationStartEvent`の実装
  - [ ] `ApplicationShutdownEvent`の実装
- [ ] サンプルイベントハンドラーの実装
  - [ ] 各イベント型のハンドラー実装例
  - [ ] エラーハンドリングパターンの確立

## イベント設計の注意点
- イベントデータは不変（Immutable）にする
- イベント間の階層構造を適切に設計する
- データサイズを考慮し、必要最小限の情報のみ含める
- イベント名は「～Completed」「～Failed」などの動詞過去形で統一する

## イベント実装例
```csharp
/// <summary>
/// イベント基底クラス
/// </summary>
public abstract class EventBase : IEvent
{
    public Guid Id { get; }
    public DateTime Timestamp { get; }
    
    protected EventBase()
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// キャプチャ完了イベント
/// </summary>
public class CaptureCompletedEvent : EventBase
{
    public IImage CapturedImage { get; }
    public Rectangle CaptureRegion { get; }
    public TimeSpan CaptureTime { get; }
    
    public CaptureCompletedEvent(IImage capturedImage, Rectangle captureRegion, TimeSpan captureTime)
    {
        CapturedImage = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));
        CaptureRegion = captureRegion;
        CaptureTime = captureTime;
    }
}

/// <summary>
/// OCR完了イベント
/// </summary>
public class OcrCompletedEvent : EventBase
{
    public IImage SourceImage { get; }
    public IReadOnlyList<OcrResult> Results { get; }
    public TimeSpan ProcessingTime { get; }
    
    public OcrCompletedEvent(IImage sourceImage, IReadOnlyList<OcrResult> results, TimeSpan processingTime)
    {
        SourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
        Results = results ?? throw new ArgumentNullException(nameof(results));
        ProcessingTime = processingTime;
    }
}
```

## 関連Issue/参考
- 親Issue: #4 実装: イベント集約機構の構築
- 依存: #4.1 実装: イベント関連インターフェースの設計と実装
- 関連: #6 実装: キャプチャサブシステムの実装
- 関連: #7 実装: PaddleOCRの統合
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (7. イベント集約機構の設計)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (5.1 メソッドの静的化)

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: feature`
- `priority: high`
- `component: core`

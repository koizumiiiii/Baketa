# Issue #163: シングルショットモードのコア機能実装

**優先度**: 🔴 Critical
**所要時間**: 2-3日
**Epic**: シングルショット翻訳モード
**ラベル**: `priority: critical`, `epic: singleshot`, `type: feature`, `layer: core`, `layer: application`

---

## 概要

ユーザーがボタンを押したタイミングでのみ翻訳を実行する「シングルショットモード」のコア機能を実装します。従来の常時監視型「Live翻訳」に加えて、能動的な翻訳タイミング制御を可能にすることで、ユーザービリティを向上させます。

---

## 背景・目的

### 現状の課題
- 常時監視型のLive翻訳のみで、ユーザーが翻訳タイミングを制御できない
- 翻訳が不要なシーンでも常に実行され、リソースを消費する

### 目指す状態
- ユーザーがボタンを押したときのみ翻訳が実行される「シングルショットモード」を提供
- Live翻訳とシングルショットモードを切り替え可能にする
- 翻訳結果の表示・非表示をユーザーがコントロールできる

---

## ⚠️ アーキテクチャの真実（2025-11-19 Ultrathink分析結果）

### 重要な発見: 既存のイベント駆動アーキテクチャ

**誤った想定** (初期ドキュメント):
- `ICaptureService`に`StartAsync()`/`StopAsync()`メソッドが存在する
- これらのメソッドを拡張してSingleshotを実装する

**実際のアーキテクチャ**:
```csharp
// ❌ ICaptureServiceにStartAsync/StopAsyncは存在しない
// ✅ 現実: ICaptureServiceは単発キャプチャメソッドのみ提供
public interface ICaptureService {
    Task<IImage> CaptureWindowAsync(IntPtr windowHandle);  // 既存
    Task<IImage> CaptureScreenAsync();                     // 既存
    // ... 他の単発キャプチャメソッド
}
```

**Live翻訳の実際の仕組み** (`MainOverlayViewModel.cs:870-1016`):
```csharp
private async Task StartTranslationAsync() {
    // ✅ イベント駆動: StartTranslationRequestEventを発行
    var startTranslationEvent = new StartTranslationRequestEvent(selectedWindow);
    await PublishEventAsync(startTranslationEvent).ConfigureAwait(false);
    // ICaptureService.StartAsync()は呼ばれていない
}
```

### 実装方針の修正

**修正前の方針** (誤り):
1. ICaptureServiceに`CaptureSingleShotAsync()`を追加
2. MainOverlayViewModel.StartTranslationAsync()に`SwitchToLiveMode()`を追加

**修正後の方針** (正しい):
1. **ICaptureServiceは変更不要** - 既存メソッドを再利用
2. **新規イベント定義**: `ExecuteSingleshotRequestEvent`
3. **イベントプロセッサ**: `SingleshotEventProcessor`で処理
4. **MainOverlayViewModelは既存メソッド変更なし** - 新規メソッド`ExecuteSingleshotAsync()`のみ追加

---

## スコープ

### 実装タスク

#### 1. アーキテクチャ設計
- [ ] **`ITranslationModeService` インターフェース定義**（Baketa.Core）
  - 現在の翻訳モード取得・設定
  - モード変更イベント
  - モードに応じた翻訳実行メソッド

- [ ] **State Pattern実装**（Baketa.Application）
  - `LiveTranslationMode` クラス: 常時監視モード
  - `SingleshotTranslationMode` クラス: 単発実行モード
  - `TranslationModeService` クラス: モード管理サービス

#### 2. イベント駆動統合
- [ ] **新規イベント定義**（Baketa.Core）
  - `ExecuteSingleshotRequestEvent` クラス: Singleshot実行イベント
  - 既存の `StartTranslationRequestEvent` / `StopTranslationRequestEvent` と同様の構造

- [ ] **イベントプロセッサ実装**（Baketa.Application）
  - `SingleshotEventProcessor` クラス: Singleshot実行イベントの処理
  - `ITranslationModeService` を使用してモード管理

#### 3. UI統合（最小限）
- [ ] **MainOverlayViewModel 拡張**
  - `ITranslationModeService` をDI注入
  - `ExecuteSingleshotCommand` 追加: シングルショット実行コマンド
  - `ExecuteSingleshotAsync()` メソッド実装: イベント発行処理

- [ ] **MainOverlayView.axaml 修正**
  - Singleshotボタン追加（アイコン・スタイルは#164で実装）
  - `ExecuteSingleshotCommand` へのバインディング

#### 4. オーバーレイ表示制御
- [ ] **翻訳結果の表示・非表示ロジック**
  - シングルショット実行後、翻訳オーバーレイを表示
  - もう一度Singleshotボタンを押すとオーバーレイを非表示
  - オーバーレイ表示中はボタンを赤色で表示

#### 5. 排他制御
- [ ] **Live翻訳とシングルショットの排他制御**
  - Live実行中はSingleshotボタンを無効化
  - Singleshot実行中はLiveボタンを無効化

#### 6. テスト実装
- [ ] **ユニットテスト**: `TranslationModeServiceTests` (xUnit + Moq)
  - モード切替テスト (10ケース)
  - 状態遷移テスト (5ケース)
  - イベント発行テスト (5ケース)

---

## 統合方針と実装戦略

### アーキテクチャアプローチ: ハイブリッド統合方式

**基本方針**:
1. **既存のLive翻訳は完全に維持** - `StartStopCommand`の動作は一切変更しない
2. **Singleshotは独立した新機能** - 新しい`ExecuteSingleshotCommand`を追加
3. **State PatternでMode管理** - `ITranslationModeService`で状態を一元管理
4. **UI変更は最小限** - 既存ボタンはそのまま、Singleshotボタンを1つ追加

### 既存Live翻訳との統合ポイント

#### MainOverlayViewModelへの変更（最小限）

**追加要素**（新規）:
```csharp
// 新規フィールド（DI注入）
private readonly ITranslationModeService _translationModeService;

// 新規プロパティ
public TranslationMode CurrentMode => _translationModeService.CurrentMode;
public bool IsSingleshotActive => _translationModeService.IsSingleshotActive;

// 新規コマンド
public ICommand ExecuteSingleshotCommand { get; private set; }
```

**既存メソッドへの変更**:
```csharp
// ❌ 既存のStartTranslationAsync()とStopTranslationAsync()は一切変更しない
// Live翻訳とSingleshotは完全に独立した機能として実装
// Live翻訳は既存のイベント駆動アーキテクチャ（StartTranslationRequestEvent）を継続使用
```

**新規メソッド**（完全新規実装）:
```csharp
private async Task ExecuteSingleshotAsync()
{
    Logger?.LogInformation("📸 Singleshot翻訳実行開始");

    // モード切替
    await _translationModeService.SwitchToSingleshotModeAsync().ConfigureAwait(false);

    // イベント発行（既存のLive翻訳と同様のイベント駆動パターン）
    var singleshotEvent = new ExecuteSingleshotRequestEvent(SelectedWindow);
    await PublishEventAsync(singleshotEvent).ConfigureAwait(false);

    Logger?.LogInformation("✅ Singleshot翻訳イベント発行完了");
}
```

### Live翻訳名称明示化の方針（追加要件）

**課題**: 既存の翻訳処理が「Live翻訳」であることをコード上で明確にする必要がある。

**影響範囲分析**:
- **MainOverlayViewModel**: `StartTranslationAsync()`, `StopTranslationAsync()` など26箇所
- **イベントクラス**: `StartTranslationRequestEvent`, `StopTranslationRequestEvent` など4クラス
- **イベントハンドラ**: `TranslationRequestHandler`, `StopTranslationRequestEventHandler` など5クラス
- **ログメッセージ**: "翻訳開始", "翻訳停止" など100+箇所

**採用アプローチ: 段階的リネーミング（2フェーズ）**

#### Phase 1: 最小限の変更（Issue #163実装時）

1. **新規メソッド名を明確化**:
   ```csharp
   // ✅ 新規メソッド - 明確にLive専用（将来的な統一に備える）
   private async Task StartLiveTranslationAsync() { ... }
   private async Task StopLiveTranslationAsync() { ... }

   // ✅ 新規メソッド - 明確にSingleshot専用
   private async Task ExecuteSingleshotAsync() { ... }
   ```

2. **既存メソッドは維持 + XMLドキュメントコメント追加**:
   ```csharp
   /// <summary>
   /// Live翻訳を開始します（連続的な画面キャプチャ→OCR→翻訳ループ）
   /// </summary>
   /// <remarks>
   /// このメソッドは将来的に StartLiveTranslationAsync() にリネームされる予定です。
   /// </remarks>
   private async Task StartTranslationAsync() { ... }

   /// <summary>
   /// Live翻訳を停止します
   /// </summary>
   /// <remarks>
   /// このメソッドは将来的に StopLiveTranslationAsync() にリネームされる予定です。
   /// </remarks>
   private async Task StopTranslationAsync() { ... }
   ```

3. **ログメッセージに "Live" を追加**:
   ```csharp
   // Before: Logger?.LogDebug("🚀 StartTranslationAsync開始");
   // After:  Logger?.LogDebug("🚀 StartLiveTranslationAsync開始 (Live翻訳モード)");

   // Before: Logger?.LogInformation("翻訳ワークフローを開始");
   // After:  Logger?.LogInformation("🚀 Live翻訳ワークフローを開始");
   ```

#### Phase 2: 完全リネーミング（別Issue、Issue #163完了後）

**対象**: MainWindowViewModel, イベントクラス, イベントハンドラ

**変更内容**:
1. `StartTranslationAsync()` → `StartLiveTranslationAsync()` への完全移行
2. `StopTranslationAsync()` → `StopLiveTranslationAsync()` への完全移行
3. イベント名変更（検討中）:
   - `StartTranslationRequestEvent` → `StartLiveTranslationRequestEvent`?
   - `StopTranslationRequestEvent` → そのまま（Live/Singleshot共通）?
4. 全テストケース更新（100+件）

**注意**: Phase 2は別Issueとして切り出し、Issue #163完了後に実施する。

### リスク評価（更新版）

| 項目 | リスクレベル | 対策 |
|------|------------|------|
| 既存Live翻訳への影響 | **低** | 変更は3行のみ、既存フロー維持 |
| Live翻訳名称明示化の影響 | **中** | Phase 1で最小限、Phase 2で完全対応 |
| UI/UX混乱 | **低** | ボタン1つ追加、既存ボタンは不変 |
| メモリリーク | **低** | ArrayPool<byte>最適化済み |
| テストカバレッジ | **中** | 20テストケース + 既存1,588ケース維持 |

### 実装フェーズ詳細

#### Phase 1: Core Layer（依存なし）
1. `Baketa.Core/Abstractions/Services/TranslationMode.cs` - Enum定義
2. `Baketa.Core/Abstractions/Services/ITranslationModeService.cs` - Interface定義

#### Phase 2: Application Layer（State Pattern実装）
1. `Baketa.Application/Services/TranslationMode/TranslationModeBase.cs` - 抽象基底クラス
2. `Baketa.Application/Services/TranslationMode/LiveTranslationMode.cs` - Live実装
3. `Baketa.Application/Services/TranslationMode/SingleshotTranslationMode.cs` - Singleshot実装
4. `Baketa.Application/Services/TranslationMode/TranslationModeService.cs` - サービス実装

#### Phase 3: Infrastructure拡張
1. `Baketa.Core/Abstractions/Services/ICaptureService.cs` - `CaptureSingleShotAsync()` 追加
2. `Baketa.Infrastructure.Platform/Windows/Capture/WindowsCaptureService.cs` - ArrayPool<byte>実装

#### Phase 4: UI Integration
1. `Baketa.UI/ViewModels/MainOverlayViewModel.cs`:
   - コンストラクタに `ITranslationModeService` 注入（1行追加）
   - `StartTranslationAsync()`内に`SwitchToLiveModeAsync()`追加（1行）
   - `StopTranslationAsync()`内にモードリセット追加（1行）
   - `ExecuteSingleshotAsync()`新規メソッド追加
   - XMLドキュメントコメント追加（Live翻訳であることを明記）
   - ログメッセージに "Live" 追加
2. `Baketa.UI/Views/MainWindow.axaml` - Singleshotボタン追加

#### Phase 5: DI Registration
1. `Baketa.Application/DI/Modules/ApplicationModule.cs` - `ITranslationModeService`登録

#### Phase 6: Testing
1. `tests/Baketa.Application.Tests/Services/TranslationMode/TranslationModeServiceTests.cs` - 20テストケース
2. 既存1,588テストケースの回帰テスト実施

### 既存コードへの影響まとめ

**変更箇所**:
- MainOverlayViewModel: **5箇所の追加のみ**（既存コードの変更は3行のみ）
  1. コンストラクタ: `_translationModeService`フィールド追加
  2. `StartTranslationAsync()`: `SwitchToLiveModeAsync()`呼び出し1行追加
  3. `StopTranslationAsync()`: モードリセット1行追加
  4. `ExecuteSingleshotAsync()`: 新規メソッド追加（既存コードに影響なし）
  5. XMLドキュメントコメント・ログメッセージ更新（動作に影響なし）

**影響を受けないコード**:
- 既存のイベントクラス（変更なし）
- 既存のイベントハンドラ（変更なし）
- 既存のテストケース（1,588ケース全て動作保証）

---

## 技術仕様

### 新規インターフェース: `ITranslationModeService`

```csharp
namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 翻訳モード（Live/Singleshot）の状態管理サービス
/// </summary>
public interface ITranslationModeService
{
    /// <summary>現在の翻訳モード</summary>
    TranslationMode CurrentMode { get; }

    /// <summary>シングルショット実行中（オーバーレイ表示中）か</summary>
    bool IsSingleshotActive { get; }

    /// <summary>モード変更イベント</summary>
    event EventHandler<TranslationModeChangedEventArgs> ModeChanged;

    /// <summary>Live翻訳モードに切り替え</summary>
    Task SwitchToLiveModeAsync(CancellationToken cancellationToken = default);

    /// <summary>シングルショットモードに切り替え</summary>
    Task SwitchToSingleshotModeAsync(CancellationToken cancellationToken = default);

    /// <summary>シングルショット実行（1回だけキャプチャ→翻訳）</summary>
    Task ExecuteSingleshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// シングルショットのオーバーレイを非表示
    /// Note: Geminiレビュー指摘 - ExecuteAsyncがトグル動作を行うため、このメソッドの
    /// 明確なユースケースが不明。実装時に必要性を再検討すること。
    /// </summary>
    Task HideSingleshotOverlayAsync();
}

/// <summary>翻訳モード</summary>
public enum TranslationMode
{
    /// <summary>モード未設定</summary>
    None,
    /// <summary>Live翻訳（常時監視）</summary>
    Live,
    /// <summary>シングルショット（単発実行）</summary>
    Singleshot
}
```

---

### State Pattern実装

```csharp
namespace Baketa.Application.Services.TranslationMode;

/// <summary>翻訳モードの抽象基底クラス</summary>
public abstract class TranslationModeBase
{
    protected readonly ICaptureService CaptureService;
    protected readonly IOverlayManager OverlayManager;

    protected TranslationModeBase(
        ICaptureService captureService,
        IOverlayManager overlayManager)
    {
        CaptureService = captureService;
        OverlayManager = overlayManager;
    }

    public abstract Task EnterAsync(CancellationToken cancellationToken = default);
    public abstract Task ExitAsync();
    public abstract Task ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Live翻訳モード</summary>
public class LiveTranslationMode : TranslationModeBase
{
    public override async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await CaptureService.StartAsync(cancellationToken);
    }

    public override async Task ExitAsync()
    {
        await CaptureService.StopAsync();
    }

    public override Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Live翻訳は常時監視のため、明示的な実行は不要
        return Task.CompletedTask;
    }
}

/// <summary>シングルショットモード</summary>
public class SingleshotTranslationMode : TranslationModeBase
{
    private bool _isOverlayVisible;

    public override Task EnterAsync(CancellationToken cancellationToken = default)
    {
        // シングルショットモードに入るだけでは何もしない
        return Task.CompletedTask;
    }

    public override async Task ExitAsync()
    {
        // オーバーレイが表示されていれば非表示
        if (_isOverlayVisible)
        {
            await OverlayManager.HideAllAsync();
            _isOverlayVisible = false;
        }
    }

    public override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_isOverlayVisible)
        {
            // 既にオーバーレイ表示中 → 非表示にする
            await OverlayManager.HideAllAsync();
            _isOverlayVisible = false;
        }
        else
        {
            // オーバーレイ非表示 → キャプチャ→翻訳を実行
            await CaptureService.CaptureSingleShotAsync(cancellationToken);
            _isOverlayVisible = true;
        }
    }

    public bool IsOverlayVisible => _isOverlayVisible;
}
```

---

### イベント定義（新規）

#### ExecuteSingleshotRequestEvent

```csharp
namespace Baketa.Core.Events;

/// <summary>
/// Singleshot翻訳実行要求イベント
/// 既存のStartTranslationRequestEventと同様のパターンで実装
/// </summary>
public sealed class ExecuteSingleshotRequestEvent : EventBase
{
    /// <summary>
    /// 翻訳対象ウィンドウ情報
    /// </summary>
    public required WindowInfo TargetWindow { get; init; }

    public ExecuteSingleshotRequestEvent(WindowInfo targetWindow)
    {
        TargetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));
    }
}
```

#### SingleshotEventProcessor

```csharp
namespace Baketa.Application.EventProcessors;

/// <summary>
/// Singleshot翻訳イベントプロセッサ
/// ExecuteSingleshotRequestEventを処理し、単発のキャプチャ→OCR→翻訳を実行
/// </summary>
public sealed class SingleshotEventProcessor : IEventProcessor<ExecuteSingleshotRequestEvent>
{
    private readonly ICaptureService _captureService;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITranslationService _translationService;
    private readonly IOverlayManager _overlayManager;
    private readonly ILogger<SingleshotEventProcessor> _logger;

    public int Priority => 100;
    public bool SynchronousExecution => true; // 確実に実行完了を保証

    public async Task HandleAsync(ExecuteSingleshotRequestEvent eventData)
    {
        _logger.LogInformation("📸 Singleshot翻訳処理開始: {WindowTitle}", eventData.TargetWindow.Title);

        // 1. キャプチャ（既存のCaptureWindowAsyncを使用）
        var image = await _captureService.CaptureWindowAsync(eventData.TargetWindow.Handle).ConfigureAwait(false);

        // 2. OCR処理
        var ocrResult = await _ocrEngine.RecognizeAsync(image).ConfigureAwait(false);

        // 3. 翻訳処理
        var translationResult = await _translationService.TranslateAsync(ocrResult.Text).ConfigureAwait(false);

        // 4. オーバーレイ表示
        await _overlayManager.ShowTranslationAsync(translationResult).ConfigureAwait(false);

        _logger.LogInformation("✅ Singleshot翻訳完了");
    }
}
```

**重要な設計原則**:
- ICaptureServiceは既存のまま（変更不要）
- 既存の`CaptureWindowAsync()`などを再利用
- イベント駆動アーキテクチャに完全統合

---

## 動作確認基準

### 必須動作確認項目

- [ ] **シングルショット実行**: Singleshotボタンを押すと1回だけキャプチャ→翻訳が実行される
- [ ] **オーバーレイ表示**: 翻訳結果がオーバーレイとして画面に表示される
- [ ] **オーバーレイ非表示**: もう一度Singleshotボタンを押すとオーバーレイが消える
- [ ] **ボタン状態（赤色維持）**: オーバーレイ表示中はSingleshotボタンが赤色のまま
- [ ] **Live翻訳との排他制御**: Live実行中はSingleshotボタンが無効化（グレーアウト）される
- [ ] **Singleshot実行中の排他制御**: Singleshot実行中（オーバーレイ表示中）はLiveボタンが無効化される

### テスト実行基準

- [ ] `TranslationModeServiceTests`: 全20ケースが成功
- [ ] 既存テスト（1,588ケース）がすべて成功（リグレッションなし）

---

## 依存関係

### Blocked by（先行して完了すべきissue）
なし（最優先で着手可能）

### Blocks（このissue完了後に着手可能なissue）
- #164: シングルショットモードのUI/UX改善（ボタンアイコン、カラースキーム）
- #171: メインウィンドウUI刷新（全体的なレイアウト調整）

---

## 変更ファイル

### 新規作成
- `Baketa.Core/Abstractions/Services/ITranslationModeService.cs`
- `Baketa.Core/Abstractions/Services/TranslationMode.cs` (enum)
- `Baketa.Application/Services/TranslationMode/TranslationModeBase.cs`
- `Baketa.Application/Services/TranslationMode/LiveTranslationMode.cs`
- `Baketa.Application/Services/TranslationMode/SingleshotTranslationMode.cs`
- `Baketa.Application/Services/TranslationMode/TranslationModeService.cs`
- `tests/Baketa.Application.Tests/Services/TranslationMode/TranslationModeServiceTests.cs`

### 新規作成（イベント関連）
- `Baketa.Core/Events/ExecuteSingleshotRequestEvent.cs`
- `Baketa.Application/EventProcessors/SingleshotEventProcessor.cs`

### 修正
- `Baketa.Application/DI/Modules/ApplicationModule.cs` (ITranslationModeService登録、イベントプロセッサ登録)
- `Baketa.UI/ViewModels/MainOverlayViewModel.cs` (+1フィールド、+1プロパティ、+1コマンド、+1メソッド)
- `Baketa.UI/Views/MainOverlayView.axaml` (Singleshotボタン追加)
- `Baketa.UI/Views/MainWindow.axaml` (+1ボタン)

---

## 実装ガイドライン

### Clean Architecture遵守
- `ITranslationModeService` はBaketa.Coreで定義（依存関係逆転）
- `TranslationModeService` はBaketa.Applicationで実装
- UI層（Baketa.UI）はインターフェースのみに依存

### State Patternのメリット
- モード追加時の拡張性（例: "Auto"モード、"Schedule"モードなど）
- 各モードのロジックが独立し、テストしやすい
- モード切替時の状態遷移が明確

### メモリ管理（重要）

画面キャプチャは大きなバイト配列を生成（例: 1920x1080x4byte = 約8MB）するため、`ArrayPool<byte>`を使用してGC圧力を削減します。

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Capture;

public class WindowsCaptureService : ICaptureService
{
    private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;

    public async Task<CaptureResult> CaptureSingleShotAsync(
        IProgress<CaptureProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        byte[]? rentedBuffer = null;
        try
        {
            // 進行状況通知: キャプチャ開始
            progress?.Report(new CaptureProgress
            {
                Step = CaptureStep.Capturing,
                PercentComplete = 0,
                Message = "画面をキャプチャしています..."
            });

            // キャプチャサイズを取得
            var captureSize = _targetWindow.Width * _targetWindow.Height * 4; // BGRA
            rentedBuffer = _bytePool.Rent(captureSize);

            // キャプチャ実行
            var bytesRead = await CaptureToBufferAsync(rentedBuffer, cancellationToken);

            // 進行状況通知: OCR開始
            progress?.Report(new CaptureProgress
            {
                Step = CaptureStep.ProcessingOcr,
                PercentComplete = 30,
                Message = "テキストを認識しています..."
            });

            // OCR処理
            var ocrResult = await _ocrService.RecognizeAsync(
                rentedBuffer.AsMemory(0, bytesRead),
                cancellationToken);

            // 進行状況通知: 翻訳開始
            progress?.Report(new CaptureProgress
            {
                Step = CaptureStep.Translating,
                PercentComplete = 60,
                Message = "翻訳しています..."
            });

            // 翻訳処理
            var translationResult = await _translationService.TranslateAsync(
                ocrResult.Text,
                cancellationToken);

            // 進行状況通知: 完了
            progress?.Report(new CaptureProgress
            {
                Step = CaptureStep.Completed,
                PercentComplete = 100,
                Message = "完了しました"
            });

            return new CaptureResult
            {
                OriginalText = ocrResult.Text,
                TranslatedText = translationResult.Text
            };
        }
        finally
        {
            // 必ずバッファを返却
            if (rentedBuffer != null)
            {
                _bytePool.Return(rentedBuffer, clearArray: false);
            }
        }
    }
}
```

**ポイント**:
- `ArrayPool<byte>.Shared.Rent()` でバッファを借用
- `try-finally` で確実に `Return()` を呼び出し
- `clearArray: false` でパフォーマンス向上（セキュリティ要件がない場合）

### テスト方針
- `TranslationModeService` のモック不要（具象クラステスト）
- `ICaptureService` と `IOverlayManager` はMoqでモック化
- 状態遷移のすべてのパターンをテスト
- メモリリークテスト: 100回連続実行後のメモリ使用量を確認

---

## 備考

### UIスタイルについて
- ボタンのアイコン、カラースキーム、ホバー時の動作は#164で実装
- 本issueでは機能実装のみに集中

### ホットキー機能について
- #165（ホットキー統合）は対応しない方針
- 将来的にホットキーが必要になった場合は別issueで対応

### パフォーマンス考慮
- **メモリ管理**: 上記「実装ガイドライン > メモリ管理」を参照
- **進行状況通知**: UI応答性向上のため、`IProgress<CaptureProgress>`でステップごとに通知
- **キャンセル対応**: `CancellationToken`を各ステップで確認し、早期終了を可能にする

---

## Gemini 2.5 Pro設計レビュー結果

**レビュー日**: 2025-11-19
**レビュアー**: Gemini 2.5 Pro
**レビュー対象**: Issue #163統合方針全体

### ✅ 良好な点

1. **アーキテクチャの遵守**: クリーンアーキテクチャの依存関係ルールに完全に準拠。インターフェースをCore層、実装をApplication層に配置する構成は責務分離とテスト容易性を高める理想的な形。

2. **既存コードへの影響最小化**: `MainOverlayViewModel`への変更をわずか3行に留める統合方針は、リグレッションリスクを大幅に低減させる優れたアプローチ。既存のLive翻訳機能をブラックボックスとして扱えている点が評価できる。

3. **State Patternの適切な適用**: `LiveTranslationMode`と`SingleshotTranslationMode`の責務を明確に分離し、将来的なモード追加（例: "Auto"モード）にも対応可能な拡張性の高い設計。

4. **堅牢な非同期・メモリ管理**: `ArrayPool<byte>`の使用によるGC圧力の削減、`IProgress<T>`によるUI応答性の確保、`CancellationToken`によるキャンセル処理への対応など、パフォーマンスとユーザーエクスペリエンスに配慮した技術選定。

5. **現実的なリネーミング戦略**: 大規模なリファクタリングを2つのフェーズに分ける段階的アプローチは、開発スコープを適切に管理し、リスクをコントロールする上で現実的かつ賢明な戦略。

### ⚠️ 改善提案

1. **`SingleshotTranslationMode`の責務の単純化**: `ExecuteAsync`がオーバーレイ表示/非表示のトグル機能を持つのは、一つのメソッドが複数の責務（実行と状態反転）を担っており、やや複雑。将来的なメンテナンス性を考慮し、`ExecuteAsync`は実行のみに専念させ、表示状態の管理は`TranslationModeService`側で行うか、`ShowOverlayAsync`/`HideOverlayAsync`のようなより具体的なメソッドに分離することを検討。

2. **ViewModelテストへの影響の明記**: `MainOverlayViewModel`のコンストラクタに`ITranslationModeService`が追加されるため、既存のユニットテストのDI設定やモックのセットアップを修正する必要がある。「変更ファイル」や「実装タスク」のセクションにテストコードの修正も明記すると、作業の見積もり精度が向上。

3. **イベント駆動アーキテクチャとの一貫性**: 現在の設計はコマンドパターンとしてシンプルで有効だが、もしアプリケーション全体がイベント駆動を指向している場合、Singleshotの実行フローも`StartSingleshotRequestEvent`や`SingleshotCompletedEvent`といったイベントを発行する形に統一することを検討する価値がある。

### ❌ 潜在的リスク

1. **エラーハンドリングの設計欠如**:
   - **問題点**: `ExecuteSingleshotAsync`などの非同期処理で例外（例: キャプチャ失敗、OCRサービスへの接続エラー）が発生した場合に、ユーザーにどのようにフィードバックするかの設計が記載されていない。
   - **対策**: `MainOverlayViewModel`のコマンド実行処理内に`try-catch`ブロックを設け、例外を捕捉。捕捉した例外は、ユーザーにエラーメッセージを表示する専用のサービス（例: `INotificationService`）を通じて通知すべき。

2. **ドキュメント内の設計不整合**:
   - **問題点**: `MainOverlayViewModel`の`ExecuteSingleshotAsync`のコード例が、`_translationModeService.ExecuteSingleshotAsync()`を呼び出した直後に`_translationModeService.HideSingleshotOverlayAsync()`を呼び出す実装になっている。これは「もう一度ボタンを押すとオーバーレイが消える」というUX要件と矛盾しており、実行直後に結果が消えてしまう。
   - **対策**: `HideSingleshotOverlayAsync()`の呼び出しを削除し、`_translationModeService.ExecuteSingleshotAsync()`の呼び出しのみに修正。

### 🔍 確認事項

1. **`StopTranslationAsync`時のモード**: ドキュメントの`Note`にある通り、Live翻訳停止後のモードを`None`に戻すか、`Live`のまま維持するかの仕様を確定。

2. **`HideSingleshotOverlayAsync`の明確なユースケース**: このメソッドが`SingleshotTranslationMode`のトグル動作（`ExecuteAsync`）や状態遷移時のクリーンアップ（`ExitAsync`）と別に必要な理由を明確化。

3. **進捗表示のUI連携**: `IProgress<CaptureProgress>`のインターフェースは定義されているが、ViewModelが受け取った進捗情報をUI（View）でどのように表示するか（例：ボタン上にスピナーを表示、プログレスバーを表示など）の基本的な方針を確認。これは後続のUI Issue(#164)のスコープかもしれないが、技術的な実現可能性をこの段階で確認しておくとスムーズ。

4. **状態管理の一元化**: `SingleshotTranslationMode`が持つ`_isOverlayVisible`という内部状態は、`IOverlayManager`が管理するUIの表示状態と一致している必要がある。状態の不整合を防ぐため、`IOverlayManager`に`IsAnyOverlayVisible`のようなプロパティを持たせ、状態の信頼できる情報源（Single Source of Truth）を`IOverlayManager`に一元化できないか検討。

---

**作成日**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`

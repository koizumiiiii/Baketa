# Phase 5.2E: モデルプリウォーミング最適化 - 実装計画レビュー依頼

## 背景

ユーザーからの明確な要求:
> "その方式だとStartボタン押下後以降の処理が増えてユーザーの体感的に翻訳までの時間が長く感じるのではないか それであれば却下 むしろ全ての準備をStartボタン押下前に済ませておく方がいい"

**ユーザーの哲学**: Lazy Loading（遅延読み込み）を却下し、Pre-warming（事前準備）を採用。起動時間を犠牲にしても、翻訳実行時の体感速度を最優先する。

## 既存実装の調査結果

### ✅ 既に実装済みのコンポーネント (Issue #143対応)

1. **WarmupHostedService.cs**
   - IHostedService実装
   - アプリ起動2秒後にバックグラウンドでウォームアップ実行
   - 最大5分待機、進捗通知購読
   - **DI登録済み**: InfrastructureModule.cs Line 268

2. **BackgroundWarmupService.cs**
   - IWarmupService実装
   - 3フェーズウォームアップ:
     - Phase 1: GPU環境検出 (10%)
     - Phase 2: OCRエンジン初期化 + ウォームアップ (50%)
     - Phase 3: 翻訳エンジン初期化 + ウォームアップ (40%)
   - 進捗通知イベント: WarmupProgressChanged
   - スレッドセーフ実装 (SemaphoreSlim, ConcurrentDictionary)

3. **PaddleOcrEngine.WarmupAsync()**
   - 512x512ダミー画像でOCR実行
   - モデルをメモリにロード
   - 既に BackgroundWarmupService から呼び出し済み

### 問題点

**現状の動作**:
- 起動 → UI即表示 → Startボタン即座に有効化
- バックグラウンドでウォームアップ実行（2-6秒）
- ユーザーがウォームアップ完了前にStartを押せる
- 結果: 初回翻訳が遅延する（コールドスタート）

**問題の本質**: Startボタンが `IsOcrInitialized` のみで制御されており、`IsWarmupCompleted` をチェックしていない。

## 提案する修正方針

### Phase 5.2E: Startボタンのウォームアップ完了待機制御

**修正後の動作** (ユーザー要求に準拠):
```
起動 → UI即表示 (0秒)
       ↓
       Startボタン: 無効 (ツールチップ: "モデル読み込み中... 60%")
       ウィンドウ選択、設定変更は可能 ← ユーザーは準備できる
       ↓ (バックグラウンド 2-6秒)
       ウォームアップ完了
       ↓
       Startボタン: 有効化
       ユーザーがStartを押す → 即座に翻訳開始
```

### 実装詳細

#### Step 1: MainOverlayViewModel修正

**ファイル**: `Baketa.UI/ViewModels/MainOverlayViewModel.cs`

**変更内容**:

1. **IWarmupService依存追加**
```csharp
private readonly IWarmupService _warmupService;

public MainOverlayViewModel(
    IEventAggregator eventAggregator,
    ILogger<MainOverlayViewModel> logger,
    // ... 既存パラメータ ...
    IWarmupService warmupService)  // ← 追加
{
    _warmupService = warmupService ?? throw new ArgumentNullException(nameof(warmupService));

    // WarmupProgressChangedイベント購読
    _warmupService.WarmupProgressChanged += OnWarmupProgressChanged;

    // 既存初期化
    InitializeCommands();
    InitializeEventHandlers();
    InitializePropertyChangeHandlers();
}
```

2. **Startボタン制御条件追加**
```csharp
// StartCaptureCommand の canStartCapture メソッド修正
private bool canStartCapture() =>
    !IsTranslationActive &&
    IsOcrInitialized &&
    _warmupService.IsWarmupCompleted;  // ← 追加条件
```

3. **進捗通知ハンドラー追加（オプション）**
```csharp
private string _startButtonTooltip = "翻訳を開始";
public string StartButtonTooltip
{
    get => _startButtonTooltip;
    set => SetPropertySafe(ref _startButtonTooltip, value);
}

private void OnWarmupProgressChanged(object? sender, WarmupProgressEventArgs e)
{
    if (!_warmupService.IsWarmupCompleted)
    {
        StartButtonTooltip = $"モデル読み込み中... {e.Progress:P0}";
    }
    else
    {
        StartButtonTooltip = "翻訳を開始";
    }

    // Startボタンの CanExecute を再評価
    StartCaptureCommand?.RaiseCanExecuteChanged();
}
```

4. **Dispose処理追加**
```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _warmupService.WarmupProgressChanged -= OnWarmupProgressChanged;
    }
    base.Dispose(disposing);
}
```

#### Step 2: XAML修正（オプション）

**ファイル**: `Baketa.UI/Views/MainOverlayView.axaml`

```xml
<Button Command="{Binding StartCaptureCommand}"
        ToolTip.Tip="{Binding StartButtonTooltip}">
    <TextBlock Text="{Binding StartStopText}" />
</Button>
```

### 期待効果

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| **起動時Startボタン** | 即座に有効化 | 2-6秒無効化 |
| **初回翻訳遅延** | 0.5-2秒（コールドスタート） | **0秒（即座開始）** |
| **ユーザー体験** | 初回翻訳が遅い | **即座に翻訳表示** |
| **準備中の操作** | 可能 | **可能（ウィンドウ選択、設定変更）** |

### 既存実装の活用度

| コンポーネント | 修正要否 | 理由 |
|---------------|---------|------|
| WarmupHostedService | **変更不要** | 既に正常動作 |
| BackgroundWarmupService | **変更不要** | 既に完全実装 |
| PaddleOcrEngine.WarmupAsync | **変更不要** | 既に呼び出し済み |
| MainOverlayViewModel | **修正必要** | Startボタン制御のみ |

**新規実装**: 約50-80行（MainOverlayViewModelのみ）
**推定所要時間**: 1-2時間（テスト含む）

## レビュー依頼事項

1. **アーキテクチャ上の問題**
   - UI層（MainOverlayViewModel）からInfrastructure層（IWarmupService）への依存は適切か？
   - Clean Architecture原則に違反していないか？

2. **スレッドセーフティ**
   - WarmupProgressChangedイベントはバックグラウンドスレッドから発行される
   - UI更新（StartButtonTooltip、RaiseCanExecuteChanged）はUIスレッドで実行する必要がある
   - Dispatcher.UIThread.InvokeAsync() が必要か？

3. **タイミング問題**
   - MainOverlayViewModel初期化時点でWarmupHostedServiceが開始していない可能性は？
   - イベント購読のタイミングは適切か？

4. **エッジケース**
   - ウォームアップ失敗時の処理は？
   - タイムアウト時の処理は？
   - アプリ終了時のイベント購読解除は適切か？

5. **パフォーマンス影響**
   - 進捗通知イベントの頻度は？
   - UI更新のオーバーヘッドは？

6. **代替案の検討**
   - より良いアプローチはあるか？
   - 設計上の改善点は？

技術的安全性、Clean Architecture準拠、潜在的リスクについて評価してください。

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

#### 2. キャプチャサービス拡張
- [ ] **`ICaptureService` インターフェース拡張**
  - `CaptureSingleShotAsync()` メソッド追加: 1回だけキャプチャ実行
  - 既存の `StartAsync()` / `StopAsync()` はLive翻訳用として維持

- [ ] **実装クラス修正**
  - `ITranslationModeService` を注入
  - モードに応じた動作切替ロジック

#### 3. UI統合（最小限）
- [ ] **MainWindowViewModel 拡張**
  - `SwitchToLiveCommand` 追加: Live翻訳モードに切り替え
  - `SwitchToSingleshotCommand` 追加: シングルショットモードに切り替え
  - `ExecuteSingleshotCommand` 追加: シングルショット実行

- [ ] **MainWindow.axaml 修正**
  - Singleshotボタン追加（アイコン・スタイルは#164で実装）
  - ボタンクリック時のコマンドバインディング

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

    /// <summary>シングルショットのオーバーレイを非表示</summary>
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

### ICaptureService拡張

```csharp
namespace Baketa.Core.Abstractions.Services;

public interface ICaptureService
{
    // 既存メソッド（Live翻訳用）
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();

    // 新規追加（シングルショット用）
    /// <summary>1回だけキャプチャ→翻訳を実行</summary>
    /// <param name="progress">進行状況コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<CaptureResult> CaptureSingleShotAsync(
        IProgress<CaptureProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>キャプチャ進行状況</summary>
public record CaptureProgress
{
    /// <summary>現在のステップ</summary>
    public required CaptureStep Step { get; init; }

    /// <summary>進行率（0-100）</summary>
    public int PercentComplete { get; init; }

    /// <summary>ステップの説明</summary>
    public string? Message { get; init; }
}

/// <summary>キャプチャステップ</summary>
public enum CaptureStep
{
    /// <summary>画面キャプチャ中</summary>
    Capturing,

    /// <summary>OCR処理中</summary>
    ProcessingOcr,

    /// <summary>翻訳中</summary>
    Translating,

    /// <summary>オーバーレイ表示中</summary>
    DisplayingOverlay,

    /// <summary>完了</summary>
    Completed
}
```

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

### 修正
- `Baketa.Core/Abstractions/Services/ICaptureService.cs` (+1メソッド)
- `Baketa.Infrastructure.Platform/Windows/Capture/WindowsCaptureService.cs` (CaptureSingleShotAsync実装)
- `Baketa.Application/DI/Modules/ApplicationModule.cs` (DI登録)
- `Baketa.UI/ViewModels/MainWindowViewModel.cs` (+3コマンド)
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

**作成日**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`

# Issue #101 操作UI実装計画

## 📋 概要

**Issue**: #101 実装: 操作UI（自動/単発翻訳ボタン）  
**目標**: オーバーレイウィンドウ上の翻訳モード制御UI実装  
**アプローチ**: Phase別段階実装 + 各Phase完了時チェック

## 🎯 要件確認

### 主要機能
- ✅ **自動翻訳トグルスイッチ**: ICaptureService の StartContinuousCaptureAsync/StopCaptureAsync 制御
- ✅ **単発翻訳ボタン**: ICaptureService の CaptureOnceAsync 実行
- ✅ **割り込み処理**: 自動翻訳中の単発翻訳最優先実行
- ✅ **UI応答性**: 低遅延・直感的なユーザー体験

### 依存関係
- ✅ **#35**: ICaptureService（実装済み）
- ✅ **#72**: ISettingsService（実装済み）
- ✅ **#66**: オーバーレイウィンドウ（親Issue）

## 🏗️ 技術スタック

| 技術要素 | 選択技術 | バージョン |
|---------|---------|-----------|
| **言語** | C# 12 | .NET 8.0 |
| **UI** | Avalonia UI | 11.2.x |
| **MVVM** | ReactiveUI | 20.1.x |
| **DI** | Microsoft.Extensions.DI | 8.0.x |
| **ログ** | Microsoft.Extensions.Logging | 8.0.x |

## 📐 アーキテクチャ設計

### プロジェクト構成
```
Baketa.UI/                    # UI層
├── ViewModels/Controls/      # 操作UI ViewModel
├── Views/Controls/           # 操作UI View
└── Styles/                   # UI専用スタイル

Baketa.Application/           # アプリケーション層
├── Services/                 # 業務ロジックサービス
├── Events/                   # ドメインイベント
└── Models/                   # アプリケーションモデル
```

### 主要クラス設計
```csharp
// ViewModel
OperationalControlViewModel : ViewModelBase

// Service
TranslationOrchestrationService : ITranslationOrchestrationService

// Events
TranslationModeChangedEvent : IEvent
TranslationTriggeredEvent : IEvent

// Models
TranslationMode : enum
```

## 🚀 Phase別実装計画

---

## 📍 **Phase 1: ViewModel実装**

### 🎯 実装目標
操作UIのコア機能を担うViewModelの完全実装

### 📂 実装対象ファイル
```
Baketa.UI/ViewModels/Controls/
└── OperationalControlViewModel.cs

Baketa.Application/Models/
└── TranslationMode.cs

Baketa.Application/Events/
├── TranslationModeChangedEvent.cs
└── TranslationTriggeredEvent.cs
```

### ⚙️ 実装仕様

#### 1.1 TranslationMode enum
```csharp
public enum TranslationMode
{
    Manual,      // 手動（単発のみ）
    Automatic    // 自動（連続モード）
}
```

#### 1.2 イベント定義
```csharp
// モード変更イベント
public record TranslationModeChangedEvent(
    TranslationMode NewMode, 
    TranslationMode PreviousMode
) : IEvent;

// 翻訳実行イベント  
public record TranslationTriggeredEvent(
    TranslationMode Mode,
    DateTime TriggeredAt
) : IEvent;
```

#### 1.3 OperationalControlViewModel
```csharp
public class OperationalControlViewModel : ViewModelBase
{
    // プロパティ
    [Reactive] public bool IsAutomaticMode { get; set; }
    [Reactive] public bool IsTranslating { get; private set; }
    [Reactive] public bool CanToggleMode { get; private set; } = true;
    
    // コマンド
    public ReactiveCommand<Unit, Unit> ToggleAutomaticModeCommand { get; }
    public ReactiveCommand<Unit, Unit> TriggerSingleTranslationCommand { get; }
    
    // 依存サービス
    private readonly ICaptureService _captureService;
    private readonly ISettingsService _settingsService;
    
    // 割り込み処理用
    private CancellationTokenSource? _automaticModeCts;
    private Task? _automaticTranslationTask;
}
```

### 🔧 実装詳細

#### プロパティ連動ロジック
- **IsAutomaticMode変更** → **TranslationModeChangedEvent発行**
- **IsTranslating状態** → **コマンド実行可否制御**
- **割り込み処理** → **単発翻訳の最優先実行**

#### コマンド実装
- **ToggleAutomaticModeCommand**: 自動/手動モード切り替え
- **TriggerSingleTranslationCommand**: 単発翻訳実行

#### バリデーション規則
- モード切り替え中は操作無効化
- 翻訳実行中の適切な状態表示

### ✅ **Phase 1 完了チェック項目**

#### コード品質
- [x] CA警告 0件
- [x] C# 12構文活用（プライマリコンストラクター、パターンマッチング）
- [x] ReactiveUI規約準拠
- [x] Null安全性確保

#### 機能要件
- [x] 自動/手動モード状態管理
- [x] 割り込み処理ロジック
- [x] イベント発行機能
- [x] コマンド実行制御

#### アーキテクチャ準拠
- [x] ViewModelBase継承
- [x] 依存性注入対応
- [x] イベント集約機構活用
- [x] 適切な名前空間配置

#### テスト可能性
- [x] モック対応インターフェース使用
- [x] 単体テスト容易な設計
- [x] 副作用の分離

### 🔄 **Phase 1 → Phase 2 移行条件**
- ✅ 上記チェック項目全項目クリア
- ✅ ビルドエラー 0件
- ✅ 実装レビュー完了承認

---

## 📍 **Phase 2: View実装**

### 🎯 実装目標
操作UIの視覚的要素とユーザーインタラクションの実装

### 📂 実装対象ファイル
```
Baketa.UI/Views/Controls/
└── OperationalControl.axaml

Baketa.UI/Styles/
└── OperationalControlStyles.axaml
```

### ⚙️ 実装仕様

#### UI構成要素
- **トグルスイッチ**: 自動翻訳ON/OFF切り替え
- **単発ボタン**: 今すぐ翻訳実行
- **状態インジケーター**: 現在の翻訳状態表示
- **視覚的フィードバック**: ホバー・クリック・無効状態

#### バインディング設計
```xml
<!-- 自動翻訳トグル -->
<ToggleSwitch IsChecked="{Binding IsAutomaticMode}" 
              IsEnabled="{Binding CanToggleMode}" />

<!-- 単発翻訳ボタン -->
<Button Content="翻訳実行" 
        Command="{Binding TriggerSingleTranslationCommand}" />

<!-- 状態表示 -->
<TextBlock Text="{Binding CurrentStatus}" />
```

### ✅ **Phase 2 完了チェック項目**

#### UI/UX品質
- [ ] 直感的な操作性
- [ ] 適切な視覚的フィードバック
- [ ] アクセシビリティ対応
- [ ] レスポンシブデザイン

#### バインディング
- [ ] コンパイル済みバインディング使用
- [ ] 双方向バインディング適切性
- [ ] バインディングエラー 0件

### 🔄 **Phase 2 → Phase 3 移行条件**
- ✅ UI表示確認
- ✅ バインディング動作確認
- ✅ 視覚的品質承認

---

## 📍 **Phase 3: サービス統合**

### 🎯 実装目標
ICaptureService・ISettingsServiceとの完全統合

### 📂 実装対象ファイル
```
Baketa.Application/Services/
└── TranslationOrchestrationService.cs
```

### ⚙️ 実装仕様

#### TranslationOrchestrationService
```csharp
public interface ITranslationOrchestrationService
{
    Task StartAutomaticTranslationAsync(CancellationToken cancellationToken = default);
    Task StopAutomaticTranslationAsync();
    Task TriggerSingleTranslationAsync(CancellationToken cancellationToken = default);
    
    IObservable<TranslationResult> TranslationResults { get; }
    IObservable<TranslationStatus> StatusChanges { get; }
}
```

#### ICaptureService連携
- **StartContinuousCaptureAsync**: 自動翻訳開始
- **StopCaptureAsync**: 自動翻訳停止  
- **CaptureOnceAsync**: 単発翻訳実行

#### ISettingsService連携
- 単発翻訳表示時間設定取得
- UI設定の永続化

### ✅ **Phase 3 完了チェック項目**

#### サービス統合
- [ ] ICaptureService正常連携
- [ ] ISettingsService正常連携
- [ ] エラーハンドリング実装
- [ ] 非同期処理適切性

### 🔄 **Phase 3 → Phase 4 移行条件**
- ✅ サービス統合テスト完了
- ✅ エラーケース対応確認
- ✅ パフォーマンス確認

---

## 📍 **Phase 4: イベント統合**

### 🎯 実装目標
IEventAggregator経由の全システム統合

### ⚙️ 実装仕様

#### イベント統合項目
- **TranslationModeChangedEvent**: モード変更の全体通知
- **TranslationTriggeredEvent**: 翻訳実行の全体通知
- **UI更新イベント**: 翻訳結果表示制御

#### 割り込み処理完成
- 自動翻訳中の単発翻訳割り込み
- 単発翻訳完了後の自動復帰
- 状態整合性保証

### ✅ **Phase 4 完了チェック項目**

#### 統合テスト
- [ ] エンドツーエンド動作確認
- [ ] 割り込み処理動作確認
- [ ] イベント伝播確認
- [ ] UI応答性確認

#### 品質確認
- [ ] 全CA警告解消
- [ ] メモリリーク検証
- [ ] スレッドセーフティ確認

---

## 🎉 **最終完了条件**

### 機能要件100%達成
- ✅ 自動翻訳トグルスイッチ完全動作
- ✅ 単発翻訳ボタン完全動作  
- ✅ 割り込み処理完全動作
- ✅ UI応答性目標達成

### 技術品質達成
- ✅ CA警告 0件
- ✅ C# 12/.NET 8.0最新機能活用
- ✅ クリーンアーキテクチャ準拠
- ✅ テスト可能性確保

### ドキュメント完備
- ✅ 実装ドキュメント更新
- ✅ APIドキュメント作成
- ✅ ユーザーガイド更新

---

## 🔄 **進行管理**

### チェックポイント運用
1. **Phase完了時**: 上記チェック項目の確認依頼
2. **問題発見時**: 即座に修正→再チェック  
3. **承認後**: 次Phase移行
4. **最終確認**: 全Phase完了後の統合テスト

### 品質保証プロセス
- ビルドエラー 0件維持
- CA警告即時解消
- 機能要件100%達成
- アーキテクチャ整合性維持

---

**次のアクション**: Phase 1実装開始  
**完了予定**: 全Phase完了後、Issue #101クローズ
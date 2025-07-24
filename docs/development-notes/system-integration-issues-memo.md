# システム統合実装における課題メモ

## 📋 分析日時: 2025-01-24

## 🔍 発見された課題

### 1. DI登録の重複・競合問題

#### 🚨 重要課題: AdaptiveCaptureService重複登録
- **場所**: `Baketa.Application.DI.Modules.CaptureModule.cs` と `Baketa.Application.DI.Modules.ApplicationModule.cs`
- **問題**: 両方のモジュールで `AdaptiveCaptureService` が異なる方法で登録されている
- **影響**: DIコンテナでの競合、予期しない実装の選択

```csharp
// CaptureModule.cs (L51-85)
services.AddSingleton<AdaptiveCaptureService>(provider => { /* ファクトリー実装 */ });

// ApplicationModule.cs (L153)
services.AddSingleton<IAdaptiveCaptureService, AdaptiveCaptureService>();
```

### 2. モジュール依存関係の不整合

#### 📋 依存関係問題
- **CaptureModule**: 明示的に `PlatformModule` と `AdaptiveCaptureModule` を登録 (L33-37)
- **ApplicationModule**: `GetDependentModules()` では依存を宣言しているが実際の登録で競合

### 3. インターフェース実装の不一致

#### 🔗 ICaptureService実装の混乱
```csharp
// CaptureModule.cs (L118-139) - AdaptiveCaptureServiceAdapterを使用
services.AddSingleton<ICaptureService>(provider => adapter);

// ApplicationModule.cs (L156) - AdaptiveCaptureServiceAdapterを使用
services.AddSingleton<ICaptureService, AdaptiveCaptureServiceAdapter>();
```

### 4. TranslationOrchestrationService統合課題

#### 📍 現在の状況
- **実装済み**: `TranslationOrchestrationService` は完全実装済み
- **統合不足**: `AdvancedCaptureService` との連携が未完了
- **依存関係**: `ICaptureService` に依存（L34で注入）

## 🛠️ 修正アプローチ

### Phase 1: DI登録の統一
1. **CaptureModule.cs** を統合の中心とする
2. **ApplicationModule.cs** からキャプチャ関連登録を削除
3. インターフェース実装を一箇所に集約

### Phase 2: TranslationOrchestrationService統合
1. 既存の `ICaptureService` を `AdaptiveCaptureServiceAdapter` で提供
2. `TranslationOrchestrationService` は変更不要（依存注入で自動解決）
3. 統合テストで動作確認

### Phase 3: OCR診断システム連携
1. `IntelligentFallbackOcrEngine` の実装
2. `PPOCRv5DiagnosticService` の統合
3. エラーハンドリングの改善

## ⚠️ リスク評価

### 高リスク
- **DI競合**: アプリケーション起動失敗の可能性
- **インターフェース不整合**: 実行時例外の可能性

### 中リスク  
- **パフォーマンス**: 不適切なライフタイムスコープ
- **メモリリーク**: リソース管理の問題

### 低リスク
- **ログ出力**: デバッグ情報の過多（本番環境では問題なし）

## 📝 実装計画

### 即座実装
1. `CaptureModule.cs` の整理とクリーンアップ
2. `ApplicationModule.cs` の重複登録削除
3. インターフェース統一

### 次段階実装
1. OCR診断システムの統合
2. パフォーマンステスト実行
3. エラーハンドリング改善

---

**更新者**: Claude  
**更新日時**: 2025-01-24
# UltraThink Phase 12 完全分析結果とオーバーレイ重複表示問題解決戦略

## 発見された重大問題

### 1. 二重オーバーレイ生成の構造的欠陥
- **個別表示システム**: Y座標1000台、TranslationWithBoundsCompletedHandler経由
- **統合表示システム**: Y座標145、[OVERLAY_FIX]ログ付きルート経由
- **同一テキスト重複処理**: 1つのテキストが複数ChunkIDで処理される

### 2. Clean Architecture違反
- 複数サービスが同じ翻訳結果をオーバーレイ化する責務重複
- Single Responsibility Principle違反
- イベントフロー重複による依存関係混乱

### 3. Phase 11では解決不可能な根本問題
- Phase 11.5.3削除機能は正常動作（Rectangle.IntersectsWithで正確な領域判定）
- しかし生成速度 > 削除速度のため根本解決に至らず

## 提案する3段階解決戦略

### 短期対策（1-2時間、即時ユーザー問題軽減）
InPlaceTranslationOverlayManagerに重複防止フィルター実装:
- テキストハッシュベースの重複検出
- 2秒間の重複防止ウィンドウ
- 既存システムへの最小限変更

### 中期対策（1-2日、設計改善）
統合オーバーレイコーディネーター導入:
- IOverlayDisplayCoordinator で表示判定統一
- オーバーレイライフサイクル一元管理
- イベントフロー整理統合

### 長期対策（1-2週間、アーキテクチャリファクタリング）
Clean Architecture完全準拠システム:
- IOverlayOrchestrator（中央調整）
- IOverlayLifecycleManager（ライフサイクル管理）
- IOverlayCollisionDetector（衝突検出）
- SRP完全準拠設計

## レビュー依頼事項

1. 短期対策の妥当性とリスク評価
2. 中期対策の設計適切性
3. 長期対策のアーキテクチャ完全性
4. 実装優先順位の正しさ
5. パフォーマンスへの影響評価
6. より良い代替案の提案
7. 見落としているリスク要因の指摘

現状: Phase 11.5.3は正常動作するが、根本的なアーキテクチャ問題により重複表示が継続中
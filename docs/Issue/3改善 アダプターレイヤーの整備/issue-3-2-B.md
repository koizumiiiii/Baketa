# 改善: キャプチャアダプター実装完了

## 概要
Windows固有の画面キャプチャ実装と抽象化レイヤーの`ICaptureService`インターフェースの間のアダプターを完全に実装します。

## 目的・理由
設計済みのアダプターインターフェースに基づいて実際の機能を実装し、プラットフォーム依存コードと非依存コードの境界を明確に分離します。完全なアダプター実装により、テスト容易性と将来的な拡張性が向上します。

## 詳細
- `WindowsCaptureAdapter`クラスの完全実装
- Windows GDIキャプチャメソッドとの連携
- パフォーマンス最適化（差分検出機能など）
- キャプチャ領域指定機能の実装

## タスク分解
- [ ] `WindowsCaptureAdapter`クラスの実装
  - [ ] `ICaptureService`インターフェース実装
  - [ ] `IWindowsCaptureService`ラッパー実装
- [ ] 全画面キャプチャ機能の実装
  - [ ] `CaptureScreenAsync()`メソッドの実装
  - [ ] 結果の`IImage`への変換
- [ ] 領域指定キャプチャ機能の実装
  - [ ] `CaptureRegionAsync(Rectangle region)`メソッドの実装
  - [ ] 座標系変換の適切な処理
- [ ] キャプチャパフォーマンス最適化
  - [ ] 差分検出前処理の連携
  - [ ] キャプチャ間隔の最適化
- [ ] 例外処理とエラーハンドリングの実装
- [ ] 単体テストの作成

## 前提条件
- Issue #3-2-A（キャプチャアダプターインターフェース設計）が完了していること
- Issue #6（キャプチャサブシステムの実装）が完了していること

## 関連Issue/参考
- 親Issue: #3 改善: アダプターレイヤーの整備
- 先行: #3-2-A 改善: キャプチャアダプターインターフェース設計
- 関連: #6 実装: キャプチャサブシステムの実装
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (6.1 キャプチャサービス抽象化)
- 参照: E:\dev\Baketa\docs\3-architecture\platform\platform-abstraction.md
- 参照: 現在の画面キャプチャ関連実装

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: refactor`
- `priority: medium`
- `component: core`
- `phase: implementation`
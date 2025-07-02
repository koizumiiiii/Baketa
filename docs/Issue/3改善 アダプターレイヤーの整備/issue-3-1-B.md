# 改善: 画像アダプター実装完了

## 概要
Windows固有の画像実装と抽象化レイヤーの`IImage`インターフェースの間のアダプターを完全に実装します。

## 目的・理由
設計済みのアダプターインターフェースに基づいて実際の機能を実装し、プラットフォーム依存コードと非依存コードの境界を明確に分離します。完全なアダプター実装により、テスト容易性と将来的な拡張性が向上します。

## 詳細
- `WindowsImageAdapter`クラスの完全実装
- 双方向変換（`IImage` ⇔ `IWindowsImage`）の実装
- 画像処理機能のプラットフォーム実装への橋渡し
- パフォーマンスを考慮した変換の最適化

## タスク分解
- [ ] `WindowsImageAdapter`クラスの実装
  - [ ] `IImage`インターフェース実装
  - [ ] `IWindowsImage`ラッパー実装
- [ ] 基本プロパティ（幅・高さなど）の変換実装
- [ ] 画像処理メソッド（リサイズなど）の変換実装
- [ ] `Clone()`と`ToByteArrayAsync()`メソッドの実装
- [ ] メモリリソース管理の最適化（Dispose対応）
- [ ] 単体テストの作成
- [ ] パフォーマンステスト実施

## 前提条件
- Issue #3-1-A（画像アダプターインターフェース設計）が完了していること
- Issue #5-1（画像処理抽象化レイヤーのインターフェース設計）が完了していること

## 関連Issue/参考
- 親Issue: #3 改善: アダプターレイヤーの整備
- 先行: #3-1-A 改善: 画像アダプターインターフェース設計
- 関連: #5 実装: 画像処理抽象化レイヤーの拡張
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (5.4 アダプターの実装例)
- 参照: E:\dev\Baketa\docs\3-architecture\core\image-abstraction.md
- 参照: 現在の`Baketa.Infrastructure.Platform.Windows.Imaging`クラス

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: refactor`
- `priority: high`
- `component: core`
- `phase: implementation`
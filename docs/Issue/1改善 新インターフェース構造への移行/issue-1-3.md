# 改善: プラットフォームインターフェースの移行と拡張

## 概要
Windowsプラットフォーム固有のインターフェースを新名前空間に移行し、アダプターパターンに基づいて拡張します。

## 目的・理由
プラットフォーム依存コードと抽象化レイヤー間の明確な境界を確立することで、コードの保守性を高め、将来的な機能拡張をスムーズに行えるようにします。

## 詳細
- プラットフォーム抽象化インターフェースの名前空間を`Baketa.Core.Abstractions.Platform`に移行
- Windows固有実装用インターフェース群を`Baketa.Core.Abstractions.Platform.Windows`に配置
- プラットフォーム間の橋渡しとなるアダプターインターフェースの設計

## タスク分解
- [ ] プラットフォーム抽象化の基本インターフェース`IPlatform`の設計
- [ ] `IWindowsImage`インターフェースの移行と拡張
- [ ] `IWindowsCapturer`インターフェースの移行と拡張
- [ ] `IWindowManager`インターフェースの移行と拡張
- [ ] `IWindowsAdapter`インターフェース群の設計
- [ ] プラットフォーム検出インターフェース`IPlatformDetector`の設計
- [ ] 旧インターフェースの非推奨化

## 関連Issue/参考
- 親Issue: #1 改善: 新インターフェース構造への移行
- 関連: #3 改善: アダプターレイヤーの整備
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (5.2 Windows画像インターフェース)
- 参照: E:\dev\Baketa\docs\3-architecture\platform\platform-abstraction.md
- 参照: 現在の`Baketa.Infrastructure.Platform`プロジェクト

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: refactor`
- `priority: high`
- `component: core`

# 改善: その他のコアインターフェースの移行

## 概要
イベント、サービス、ファクトリーなど、その他のコアインターフェースを新名前空間に移行します。

## 目的・理由
コアシステムを構成する重要なインターフェースを整理し、一貫性のある名前空間構造に配置することで、開発の効率化と保守性の向上を図ります。

## 詳細
- サービス関連インターフェースを`Baketa.Core.Abstractions.Services`に移行
- ファクトリー関連インターフェースを`Baketa.Core.Abstractions.Factories`に移行
- イベント関連インターフェースを`Baketa.Core.Abstractions.Events`に移行
- その他のユーティリティインターフェースを適切な名前空間に移行

## タスク分解
- [ ] サービスインターフェースの移行と拡張
  - [ ] `ICaptureService`
  - [ ] `IOcrService`
  - [ ] `ITranslationService`
- [ ] ファクトリーインターフェースの移行と拡張
  - [ ] 共通ファクトリーパターンの設計
  - [ ] 現存するファクトリーの移行
- [ ] イベント関連インターフェースの移行と拡張
  - [ ] `IEvent`
  - [ ] `IEventHandler<TEvent>`
  - [ ] `IEventAggregator`
- [ ] その他ユーティリティインターフェースの移行
- [ ] 旧インターフェースの非推奨化

## 関連Issue/参考
- 親Issue: #1 改善: 新インターフェース構造への移行
- 関連: #4 実装: イベント集約機構の構築
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (7. イベント集約機構の設計)
- 参照: E:\dev\Baketa\docs\2-development\guidelines\namespace-migration.md (2.1 Baketa.Core)
- 参照: 現在の`Baketa.Core.Interfaces`名前空間

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: refactor`
- `priority: medium`
- `component: core`

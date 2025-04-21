# 改善: 依存性注入モジュール構造の実装

## 概要
モジュール化されたDI(依存性注入)構造の実装とサービス登録の整理を行います。

## 目的・理由
現状では依存性注入の登録が体系的に整理されておらず、サービスの依存関係が不明確になっています。モジュール化されたDI構造を導入することで、コンポーネント間の結合度を低減し、テスト容易性と保守性を向上させます。

## 詳細
- IServiceModuleインターフェースの定義と実装
- レイヤー別モジュールの実装
- 統合DIコンテナの設定
- クリーンな依存関係階層の確立

## タスク分解
- [ ] `IServiceModule`インターフェースの設計と実装
- [ ] `CoreModule`の実装
- [ ] `InfrastructureModule`の実装
- [ ] `ApplicationModule`の実装
- [ ] `UIModule`の実装
- [ ] 拡張メソッド`AddBaketaServices`の実装
- [ ] Programクラスでの統合DIコンテナ設定

## 関連Issue/参考
- 関連: #1 改善: 新インターフェース構造への移行
- 子Issue: #2.1 実装: IServiceModuleインターフェースの設計
- 子Issue: #2.2 実装: レイヤー別モジュールの実装
- 子Issue: #2.3 実装: 拡張メソッドとDIコンテナ統合
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (8. 依存性注入の最適化)
- 参照: E:\dev\Baketa\docs\2-development\guidelines\dependency-injection.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (依存性注入パターン)

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: refactor`
- `priority: high`
- `component: core`

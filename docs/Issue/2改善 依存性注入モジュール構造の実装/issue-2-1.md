# 実装: IServiceModuleインターフェースの設計

## 概要
依存性注入の基盤となる`IServiceModule`インターフェースを設計・実装します。

## 目的・理由
サービス登録を体系的に整理するための基盤として、モジュール化されたDIパターンを導入します。これにより、機能ごとの依存関係が明確になり、テスト容易性と保守性が向上します。

## 詳細
- `IServiceModule`インターフェースの設計と実装
- モジュール間の依存関係を管理する仕組みの構築
- モジュールの登録・解決の標準パターンの確立

## タスク分解
- [ ] `IServiceModule`インターフェースの定義
  - [ ] `void RegisterServices(IServiceCollection services)`メソッドの定義
  - [ ] オプショナルな`IEnumerable<Type> GetDependentModules()`メソッドの定義
- [ ] `ServiceModuleBase`抽象クラスの実装
  - [ ] 共通の依存関係解決ロジックの実装
  - [ ] モジュール登録のヘルパーメソッド実装
- [ ] モジュール検出機構の設計（オプショナル）
  - [ ] アセンブリスキャンによるモジュール自動検出
- [ ] テスト用のモックモジュールの実装
- [ ] サンプルモジュールの実装によるパターン検証

## 関連Issue/参考
- 親Issue: #2 改善: 依存性注入モジュール構造の実装
- 依存: #1.1 改善: 名前空間構造設計と移行計画
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (8.1 モジュールベースのDI)
- 参照: E:\dev\Baketa\docs\2-development\guidelines\dependency-injection.md (5.2 サービスコレクションの拡張メソッド)
- 参照: `Microsoft.Extensions.DependencyInjection`ドキュメント

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: feature`
- `priority: high`
- `component: core`

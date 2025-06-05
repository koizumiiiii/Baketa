# Baketa翻訳基盤実装 - 開発ノート

このドキュメントはBaketa翻訳基盤の実装過程で発生した問題と解決策をまとめたものです。

## 1. 名前空間の競合問題

### 1.1 発生した問題

プロジェクト内で同じデータモデルクラスが2つの異なる名前空間に定義されていました：

- `Baketa.Core.Translation.Models`
- `Baketa.Core.Models.Translation`

これにより以下の問題が発生：

1. **型の曖昧参照エラー (CS0104)**
   - コンパイラが同じクラス名の異なる名前空間を区別できない
   - `LanguagePair`, `TranslationRequest`, `TranslationResponse` などで多数発生

2. **継承クラスの実装不足エラー (CS0534)**
   - `TranslationEngineBase`クラスのメソッドが抽象として認識されるが、実装では仮想メソッドとして定義
   - `DummyEngine`や`SimpleEngine`クラスで抽象メソッドが未実装とみなされエラー

### 1.2 根本原因

C#コンパイラは同じ型名が複数の名前空間にある場合、どの名前空間の型を使用すべきか判断できません。両方の名前空間を参照するアプローチは混乱を招き、問題を悪化させました。

### 1.3 解決アプローチ

#### 短期的な解決策：名前空間エイリアスの使用

```csharp
// 名前空間エイリアスを定義して曖昧さを回避
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;  // 必要に応じて
```

#### 長期的な解決策：名前空間の統一

1. **名前空間の完全統一**
   - `Baketa.Core.Models.Translation`を標準名前空間として採用
   - 重複する定義を段階的に削除

2. **一貫した名前空間の使用**
   - すべてのコードファイルで標準となる名前空間を一貫して使用
   - 非標準名前空間の参照を段階的に削除

#### 実装クラスの修正

以下のクラスで必要な抽象メソッドをすべて実装：

1. **DummyEngine**と**SimpleEngine**
   ```csharp
   // 必須抽象メソッドの実装
   protected override Task<CoreModels.TranslationResponse> TranslateInternalAsync(
       CoreModels.TranslationRequest request,
       CancellationToken cancellationToken)
   {
       // 実装内容
   }

   public override Task<IReadOnlyCollection<CoreModels.LanguagePair>> GetSupportedLanguagePairsAsync()
   {
       // 実装内容
   }

   protected override Task<bool> InitializeInternalAsync()
   {
       // 実装内容
   }
   ```

### 1.4 修正したファイル

1. `E:\dev\Baketa\Baketa.Core\Abstractions\Translation\ITranslationEngine.cs`
2. `E:\dev\Baketa\Baketa.Core\Translation\TranslationEngineBase.cs`
3. `E:\dev\Baketa\Baketa.Core\Translation\Testing\DummyEngine.cs`
4. `E:\dev\Baketa\Baketa.Core\Translation\Testing\SimpleEngine.cs`

## 2. HttpClient依存関係の問題

### 2.1 発生した問題

`TranslationServiceCollectionExtensions.cs` で `services.AddHttpClient()` メソッドを呼び出す際にエラーが発生：

```
エラー CS1061 'IServiceCollection' に 'AddHttpClient' の定義が含まれておらず...
```

### 2.2 原因

`AddHttpClient()` メソッドは `Microsoft.Extensions.Http` パッケージが提供する拡張メソッドですが、このパッケージが参照されていませんでした。

### 2.3 解決策

1. **プロジェクトファイルへのパッケージ追加**
   ```xml
   <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
   ```

2. **using ディレクティブの追加**
   ```csharp
   using Microsoft.Extensions.Http;
   using Microsoft.Extensions.DependencyInjection.Extensions;
   ```

## 3. 翻訳基盤実装の依存関係と進め方

### 3.1 実装順序の推奨

以下の順序で実装を進めることで依存関係の問題を避けられます：

1. 共通データモデルとインターフェース定義
2. 基底クラスとユーティリティ
3. WebAPI翻訳エンジン実装（#63）
4. 翻訳結果管理システム実装（#64）
5. キャッシュシステム実装（#53）
6. イベントシステム実装（#65）

### 3.2 クリーンビルドのプラクティス

問題解決のためのビルドシステムリセット手順：

```
rmdir /s /q E:\dev\Baketa\Baketa.Core\bin
rmdir /s /q E:\dev\Baketa\Baketa.Core\obj
```

### 3.3 テスト戦略

1. **モックを活用したテスト**
   - 実際の依存関係を使わずにコンポーネントをテスト
   - インターフェース契約の検証に集中

2. **統合テスト**
   - 各コンポーネントを段階的に統合
   - 依存関係の少ない組み合わせから統合を開始

## 4. 名前空間の理解

### 4.1 `Baketa.Core.Translation.Models`

- 翻訳機能に特化した新しいモデル定義
- `Baketa.Core\Translation\Models` ディレクトリに配置
- 最新の設計ガイドラインに沿った実装
- 翻訳コンテキストやキャッシュなど拡張された機能をサポート

### 4.2 `Baketa.Core.Models.Translation`

- 以前のバージョンやレガシー機能向けのモデル定義
- `Baketa.Core\Models\Translation` ディレクトリに配置
- プロジェクト初期段階で作成された基本的なモデル
- シンプルな翻訳リクエスト/レスポンス処理向け

## 5. 今後のベストプラクティス

### 5.1 命名規則と名前空間設計

- 大規模プロジェクトでは、名前空間の階層と命名規則を事前に計画
- 同じ型名を異なる名前空間で定義しないルールの徹底
- クラス・メソッドへのXMLドキュメントコメントの徹底

### 5.2 ライブラリとパッケージの依存関係

- 各コンポーネントが必要とする外部ライブラリを明確に文書化
- 拡張メソッドを提供するパッケージの明示
- 適切なバージョン管理と互換性の確保

### 5.3 テスト駆動開発

- インターフェース仕様に基づくテストの先行実装
- モックを活用した依存性分離テスト
- 型の正しい参照を確認するテストケースの追加

---

これらの教訓と対応策を適用することで、翻訳基盤の実装を効率的に進め、同様の問題の再発を防止できます。
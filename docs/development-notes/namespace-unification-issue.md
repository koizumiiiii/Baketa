# 翻訳モデルの名前空間統一計画

## 1. 概要

現在、翻訳関連のデータモデルが2つの異なる名前空間に分散しています：

- `Baketa.Core.Models.Translation`
- `Baketa.Core.Translation.Models`

これにより、コード内で型の曖昧参照が発生し、名前空間エイリアスによる一時的な回避策を使用している状態です。この問題を根本的に解決するため、すべての翻訳関連モデルを一つの標準名前空間に統一します。

## 2. 目標

- すべての翻訳関連モデルを `Baketa.Core.Translation.Models` に統一
- 重複するクラス定義を削除
- 型参照の曖昧さを完全に排除
- 名前空間エイリアスを使用しなくても良いクリーンな状態を実現

## 3. 影響範囲

以下のコンポーネントが影響を受けます：

1. 翻訳エンジンインターフェース (`ITranslationEngine`)
2. 翻訳基底クラス (`TranslationEngineBase`)
3. 翻訳エンジン実装クラス
4. 翻訳キャッシュシステム
5. 翻訳結果管理システム
6. 翻訳イベントシステム

## 4. 移行対象のクラス

以下のクラスが主な移行対象となります：

| 現在の名前空間 | クラス名 |
|-------------|---------|
| Baketa.Core.Models.Translation | TranslationRequest |
| Baketa.Core.Models.Translation | TranslationResponse |
| Baketa.Core.Models.Translation | Language |
| Baketa.Core.Models.Translation | LanguagePair |
| Baketa.Core.Models.Translation | TranslationError |
| Baketa.Core.Translation.Models | TranslationContext |
| Baketa.Core.Translation.Models | TranslationCacheEntry |
| Baketa.Core.Translation.Models | TranslationManagementOptions |
| Baketa.Core.Translation.Models | Rectangle |
| Baketa.Core.Translation.Models | CacheStatistics |
| Baketa.Core.Translation.Models | TranslationRecord |
| Baketa.Core.Translation.Models | TranslationSearchQuery |
| Baketa.Core.Translation.Models | TranslationStatistics |
| Baketa.Core.Translation.Models | StatisticsOptions |
| Baketa.Core.Translation.Models | CacheClearOptions |
| Baketa.Core.Translation.Models | MergeStrategy |

## 5. 目標名前空間構造

翻訳関連のすべてのモデルとインターフェースを以下の名前空間構造に統一します：

```
Baketa.Core.Translation                 // ルート名前空間
├── Baketa.Core.Translation.Abstractions // 基本インターフェース定義
├── Baketa.Core.Translation.Models       // モデル定義
│   ├── Common                           // 共通モデル
│   ├── Requests                         // リクエスト関連
│   ├── Responses                        // レスポンス関連
│   └── Events                           // イベント関連
├── Baketa.Core.Translation.Services     // サービス実装
└── Baketa.Core.Translation.Common       // 共通ユーティリティ
```

## 6. 移行計画

### 6.1 フェーズ1: 事前分析と計画（1-2日）

1. **依存関係マップの作成** ✅
   - すべての翻訳関連クラスの依存関係を図示
   - Visual Studioの「コードマップの生成」機能を活用
   - ReSharper (利用可能な場合) の依存関係図機能を活用
   - 影響範囲の特定と優先順位付け

2. **クラスごとの移行方針決定** ✅
   - クラスごとに以下を決定:
     - 完全移行（クラスを移動して古い定義を削除）
     - 段階的移行（両方の名前空間で一時的に保持）
     - エイリアス（移行期間中にエイリアスで対応）
   - 注意: usingエイリアスはファイルスコープのため効果が限定的
   - 最終的にはすべて新しい名前空間を参照するように修正

3. **テスト戦略の強化** ✅
   - 移行前後での機能テスト計画
   - リファクタリング開始前に現在のテストカバレッジを測定
   - コアモデル周辺のカバレッジが低い場合は優先的にテスト拡充
   - キャラクターゼーションテスト（現状の振る舞いをそのままテストとして記述）の作成

### 6.2 フェーズ2: コアモデルの移行（2-3日）

1. **基本モデルクラスの統一** ✅
   - 優先順位: `Language` → `LanguagePair` → `TranslationRequest` → `TranslationResponse`
   - 各クラスに対する手順:
     1. 新しい名前空間にコピー
     2. 両者の差分を特定し統合
     3. 参照元の変更
     4. 古い定義に [Obsolete] 属性を追加
        ```csharp
        [Obsolete("代わりに Baketa.Core.Translation.Models.Language を使用してください。", false)]
        ```
     5. マーカーコメント追加

2. **移行状況管理表の作成** ✅
   - 名前空間移行表の作成と維持（進捗追跡用）

### 6.3 フェーズ3: インターフェースと依存クラスの移行（3-4日）

1. **インターフェースの移行** ✅
   - 影響度の低いものから順に移行
   - インターフェース更新に伴う実装クラスの修正

2. **依存クラスの移行** ✅
   - インターフェースに依存するクラスの移行
   - 循環参照の解消

3. **変換ユーティリティの実装** ✅
   - 移行期間中の互換性維持のためのユーティリティクラス
   - 拡張メソッドと静的メソッドの両方を提供
   ```csharp
   public static class TranslationModelConverter
   {
       // 拡張メソッド
       public static Baketa.Core.Translation.Models.TranslationRequest ToNewNamespace(
           this Baketa.Core.Models.Translation.TranslationRequest request)
       {
           // 変換ロジック
       }
       
       // 静的メソッド
       public static Baketa.Core.Translation.Models.TranslationRequest Convert(
           Baketa.Core.Models.Translation.TranslationRequest request)
       {
           // 変換ロジック
       }
   }
   ```

### 6.4 フェーズ4: 一括変更と検証（2-3日）

1. **名前空間参照の一括変更** ✅
   - Visual Studioの検索・置換機能を使用
   - ReSharper（利用可能な場合）の「Move to Namespace」や「Adjust Namespaces」機能を活用
   - 段階的に変更し、ビルドエラーを確認

2. **エイリアス定義の段階的削除** ✅
   - `using CoreModels = Baketa.Core.Models.Translation;` を削除
   - 各ファイルで削除後にビルドエラーがないことを確認

3. **コンパイルエラーの解消** ✅
   - 残存するビルドエラーを1つずつ解消
   - 曖昧な参照を明示的な参照に変更

### 6.4.5 フェーズ4.5: テスト修正と残存警告の解消（追加） ✅

1. **テストの修正**
   - 名前空間変更に伴うテストの更新
   - 特にLanguageクラスのRegionCodeからCode内ハイフン形式への変更に伴う調整
   - 翻訳エンジンテストの修正

2. **コードスタイルの警告解消**
   - IDE0300, IDE0301などのコレクション初期化に関する警告の解消
   - CA1852などのシール可能クラスに関する警告の解消
   - コード品質の全体的な向上

### 6.5 フェーズ5: クリーンアップと最終化（1-2日）

1. **古い定義の削除** 🔄
   - 一定期間（1〜2スプリント）[Obsolete]属性で警告を出した後、古い定義を削除
   - 開発者に新しい名前空間への移行猶予期間を提供
   - 最終段階で`Baketa.Core.Models.Translation` 名前空間のクラスを削除

2. **最終テスト** ✅
   - 単体テストの実行
   - 統合テストの実行
   - 手動機能テストの実行

3. **ドキュメント更新** 🔄
   - 名前空間変更を反映したAPIドキュメント更新
   - 開発者向けWikiや設計書などの内部ドキュメント更新
   - 設計ガイドの改訂（名前空間の命名規則など）

## 7. 今後の名前空間競合防止策

### 7.1 名前空間の標準化

名前空間の混乱を防止するため、以下の原則を適用します：

1. **一貫性ある名前空間構造**
   ```
   Baketa.Core.Translation                 // ルート
   ├── Models                           // データモデル
   │   ├── Common                       // 共通モデル
   │   ├── Configuration                // 設定モデル
   │   ├── Events                       // イベントモデル
   │   └── Results                      // 結果モデル
   ├── Services                         // サービス実装
   ├── Abstractions                     // インターフェース
   └── Common                           // 共通ユーティリティ
   ```

2. **標準的な命名規則**
   - モデルクラス: `名詞+目的語` (例: `TranslationRequest`)
   - イベントクラス: `動詞+過去分詞+Event` (例: `TranslationCompletedEvent`)
   - 列挙型: `名詞+目的` (例: `TranslationErrorType`)

### 7.2 措置とツール

1. **名前空間計画文書の作成**
   - 新機能追加前に名前空間計画を文書化
   - チーム内でレビュー・合意

2. **静的解析ルールの導入**
   - StyleCop ルールセットの導入
   - 命名規則のチェック自動化
   - **.editorconfig の活用**
     ```editorconfig
     # .editorconfig ファイルの例
     root = true
     
     [*.cs]
     # 名前空間の整理と順序付け
     dotnet_sort_system_directives_first = true
     dotnet_separate_import_directive_groups = true
     
     # 命名規則
     dotnet_naming_rule.namespace_naming.symbols = namespace_symbol
     dotnet_naming_rule.namespace_naming.style = pascal_case_style
     dotnet_naming_rule.namespace_naming.severity = warning
     
     dotnet_naming_symbols.namespace_symbol.applicable_kinds = namespace
     dotnet_naming_style.pascal_case_style.capitalization = pascal_case
     ```

3. **コードレビューの強化**
   - 名前空間使用の一貫性をレビュー項目に追加
   - 競合の可能性がある PR には特別なタグ付け

### 7.3 開発者向けガイドライン

チーム全体で名前空間管理の重要性を共有し、以下のガイドラインを定めます：

1. **新規モデルの追加プロセス**
   - 新しいデータモデルは必ず `Baketa.Core.Translation.Models` 名前空間に追加
   - サブカテゴリに応じて適切な子名前空間を利用

2. **既存クラスの拡張プロセス**
   - 既存クラスを拡張する場合は既存の名前空間を維持
   - 新しいプロパティは既存クラスに直接追加し、名前空間を分割しない

## 8. 利用する静的解析ツール

### 8.1 Roslyn アナライザー

Roslyn は .NET Compiler Platform の一部で、C# コードを解析するためのツールセットです。名前空間統一作業では特に以下の機能を活用します：

- **エラーと警告**: コンパイル時に曖昧な参照や矛盾を検出
- **カスタムアナライザー**: プロジェクト固有のルールを適用可能
- **コード修正提案**: 問題に対する自動修正の提案
- **ビルドレポート**: 警告とエラーの一覧生成

### 8.2 ReSharper (オプション)

JetBrains 社の ReSharper は強力なリファクタリングツールで、以下の機能が役立ちます：

- **依存関係の可視化**: 参照関係の確認
- **一括リファクタリング**: 型や名前空間の一括変更
- **インスペクション**: コードの問題を自動検出
- **Move to Namespace**: クラスを別名前空間に安全に移動
- **Adjust Namespaces**: 名前空間の最適化と整理

### 8.3 カスタムツール

独自のメンテナンススクリプトも配置します：

```csharp
// 名前空間の使用状況を検証するPowerShellスクリプト例
$files = Get-ChildItem -Path "E:\dev\Baketa" -Recurse -Include "*.cs"
$results = @()

foreach ($file in $files) {
    $content = Get-Content $file.FullName
    $oldNamespace = $content | Select-String "Baketa.Core.Models.Translation"
    $newNamespace = $content | Select-String "Baketa.Core.Translation.Models"
    
    if ($oldNamespace -or $newNamespace) {
        $results += [PSCustomObject]@{
            File = $file.FullName
            OldNamespaceCount = ($oldNamespace | Measure-Object).Count
            NewNamespaceCount = ($newNamespace | Measure-Object).Count
        }
    }
}

$results | Format-Table -AutoSize
```

## 9. 実装進捗状況 (2025-05-18更新)

### 9.1 分析フェーズ完了 ✅

名前空間統一化の分析フェーズが完了しました。分析結果は以下のファイルにまとめられています：

- `model-diff-analysis.md` - 翻訳モデルの差異分析結果
- `namespace-unification-plan.md` - 名前空間統一化実装計画

### 9.2 コアモデル移行完了 ✅

実装計画のフェーズ2（コアモデルの移行）が完了しました。以下のクラスの移行が完了しています：

1. `Language` - 完了
2. `LanguagePair` - 完了
3. `TranslationError/TranslationErrorType` - 完了
4. `TranslationRequest` - 完了
5. `TranslationResponse` - 完了

### 9.3 モデル統合作業の詳細 ✅

以下の機能統合が完了しました：

- `Language`クラス
  - 旧名前空間のみの機能: `NativeName`, `RegionCode`, `IsAutoDetect`が統合
  - 新名前空間のみの機能: `IsRightToLeft`, `FromCode()`が共通機能として提供
  - 地域コードの表現が`Code="zh", RegionCode="CN"`から`Code="zh-CN"`形式に変更

- `LanguagePair`クラス
  - `Create()`, `Equals()`などのメソッドを統合
  - 言語コード表現の変更に伴う修正

- その他のクラスも同様に全ての機能を統合完了

### 9.4 インターフェースと依存クラスの移行 ✅

- `ITranslationEngine`インターフェースの統一
- `TranslationEngineBase`, `MockTranslationEngine`などの実装クラスの移行
- `TranslationEngineAdapter`の実装と参照修正

### 9.5 変更の検証とテスト ✅

- 単体テストの実行と修正
- 特に中国語の言語コードに関するテストケースの修正
- IDE0300, IDE0301, CA1852などの警告も解消

### 9.6 移行状況追跡表（最終）

| クラス名 | 旧名前空間機能 | 新名前空間機能 | 統合方法 | 状態 |
|---------|--------------|--------------|---------|------|
| Language | IsAutoDetect, NativeName, RegionCode | DisplayName, IsRightToLeft, FromCode() | 全プロパティ統合、変換演算子追加 | 完了 ✅ |
| LanguagePair | Create(), Equals() | LanguagePair, FromString() | 全機能統合、変換演算子追加 | 完了 ✅ |
| TranslationError | ErrorCode, Message, Create(), FromException() | Clone() | 全機能統合、変換演算子追加 | 完了 ✅ |
| TranslationErrorType | - | - | Obsolete属性のみ追加（完全な属性間互換性あり） | 完了 ✅ |
| TranslationRequest | Create(), CreateWithContext() | Timestamp, Clone(), GenerateCacheKey() | Context型の差異を吸収する変換実装、Create()など追加 | 完了 ✅ |
| TranslationResponse | CreateSuccessWithConfidence(), CreateErrorFromException() | Timestamp, Clone() | 全機能統合、変換演算子追加 | 完了 ✅ |

### 9.7 残りのタスク

1. **古い定義の削除** 🔄
   - 次回のスプリントで`Baketa.Core.Models.Translation`名前空間のクラスを完全に削除
   - Obsolete属性を付与済みなので、移行猶予期間は既に提供済み

2. **ドキュメント更新** 🔄
   - APIドキュメントの更新（開発ドキュメントでの名前空間変更の反映）
   - 設計ガイドの改訂（名前空間の命名規則）

3. **エイリアス定義の最終確認** 🔄
   - まだ残っている可能性のある`CoreModels`エイリアスおよび`TransModels`エイリアスの完全な廃止の確認
   - すべての参照がBaketa.Core.Translation.Models名前空間に直接行われているか最終チェック

### 9.8 追加所見

今回の名前空間統一作業を通じて、以下の教訓が得られました：

1. **言語コード表現の標準化**
   - 地域コードの表現方法が複数あると混乱の元になる
   - ISO標準に従った`zh-CN`のような形式に統一することで一貫性を確保

2. **テストとの整合性**
   - モデルの変更はテストケースの修正も必要
   - テストカバレッジが高いことで、移行作業の品質を担保できた

3. **警告対応の重要性**
   - コードスタイルやベストプラクティスの警告も対応することで、コード品質が向上
   - 特にシール可能クラスや、コレクション初期化の簡素化などの改善

名前空間統一作業は予定通り完了し、すべてのテストが正常に通過しています。コードベースの一貫性と保守性が大幅に向上しました。
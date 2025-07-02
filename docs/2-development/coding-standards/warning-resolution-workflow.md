# コード分析警告対応ワークフローガイド

## 1. 概要

このドキュメントでは、Baketaプロジェクトでコード分析警告を効率的に解決するための体系的なワークフローを定義します。大量の警告に直面した際の対応手順、優先度付け、チームでの一貫性確保について説明します。

## 2. 警告対応の基本原則

### 2.1 対応優先度

| 優先度 | 警告レベル | 対応方針 | 例 |
|--------|------------|-----------|-----|
| **最高** | エラー (Error) | 即座に修正 | CS0266, CS0103 |
| **高** | 重要な警告 | 可能な限り修正 | CA1031, CA2007 |
| **中** | 品質改善 | リファクタリング時に修正 | IDE0305, CA1805 |
| **低** | スタイル | チーム方針に従って対応 | IDE0028, CS0067 |

### 2.2 修正アプローチ

1. **エラーを最優先で解決**
2. **類似警告をまとめて修正**
3. **ファイル単位で修正を進める**
4. **テストが通ることを確認しながら進める**

## 3. 段階的対応ワークフロー

### Phase 1: 警告の分析と分類

#### 3.1 警告の収集
```bash
# ビルド時の警告をファイルに出力
dotnet build > build-warnings.txt 2>&1
```

#### 3.2 警告の分類
```yaml
分類例:
- 非同期関連: CS1998, CA2007
- コレクション関連: IDE0305, IDE0028, IDE0301
- 型安全性: CA1805, CA1859
- 例外処理: CA1031
- API設計: CA1024
```

### Phase 2: 優先度付けと計画

#### 3.3 修正計画の策定
```markdown
修正順序:
1. エラー（CS0266など）
2. 非同期プログラミング警告（CS1998, CA2007）
3. コレクション初期化警告（IDE0305, IDE0028）
4. 型・フィールド初期化警告（CA1805, CA1859）
5. スタイル警告（CS0067）
```

### Phase 3: 修正実装

#### 3.4 効率的な修正手順

##### A. エラーの即座修正
```csharp
// CS0266: 型変換エラー
// 修正前
IList<string> list = GetItems();
return list; // IReadOnlyList<string>への暗黙変換不可

// 修正後
IList<string> list = GetItems();
return list.AsReadOnly();
```

##### B. 非同期メソッドの修正
```csharp
// CS1998: awaitのない非同期メソッド
// 修正前
public async Task ProcessAsync()
{
    Process();
}

// 修正後
public Task ProcessAsync()
{
    Process();
    return Task.CompletedTask;
}
```

##### C. ConfigureAwaitの追加
```csharp
// CA2007: ConfigureAwaitの不足
// 修正前
await SomeMethodAsync();

// 修正後
await SomeMethodAsync().ConfigureAwait(false);
```

##### D. コレクション初期化の統一
```csharp
// IDE0305: コレクション初期化の簡素化
// プロジェクト方針の決定が重要

// 方針A: C# 12構文を採用
var list = [];  // 型推論可能な場合

// 方針B: 明示的初期化を維持
var list = new List<string>();  // 可読性重視

// 推奨: プロジェクト内で統一
```

##### E. フィールド初期化の最適化
```csharp
// CA1805: 冗長なフィールド初期化
// 修正前
public bool IsEnabled { get; set; } = false;

// 修正後
public bool IsEnabled { get; set; }
```

## 4. 特定警告の対処法（頻出パターン）

### 4.1 IDE0305/IDE0028: コレクション初期化

#### プロジェクト方針の決定
```csharp
// Baketaプロジェクトの統一方針
// 1. 型推論可能かつ明確な場合: []構文
return [];

// 2. 型推論不可能または可読性重視: 明示的初期化
return new List<SettingMetadata>();

// 3. ReadOnlyコレクション: 明示的変換
return list.AsReadOnly();
```

### 4.2 CS1998: awaitのない非同期メソッド

#### 判断基準
```csharp
// 1. 将来的にawaitが必要になる可能性が高い → asyncを維持
public async Task<Result> ProcessAsync()
{
    // TODO: 将来的に非同期処理を追加予定
    return ProcessSync();
}

// 2. 純粋に同期処理 → Taskを直接返す
public Task<Result> ProcessAsync()
{
    var result = ProcessSync();
    return Task.FromResult(result);
}
```

### 4.3 CA2007: ConfigureAwait

#### 適用基準
```csharp
// ライブラリコード（推奨）
await operation.ConfigureAwait(false);

// UIコード（コンテキスト継続が必要な場合）
await operation.ConfigureAwait(true);  // または省略
```

### 4.4 CS0067: 未使用のイベント

#### 対処方針
```csharp
// インターフェース実装による定義で将来使用予定
#pragma warning disable CS0067 // インターフェース実装要求、将来的な機能拡張で使用予定
public event EventHandler<EventArgs>? SomeEvent;
#pragma warning restore CS0067
```

## 5. チーム開発での一貫性確保

### 5.1 EditorConfig設定

#### 推奨設定（.editorconfig）
```ini
[*.cs]
# コレクション初期化の一貫性
dotnet_style_collection_initializer = true
dotnet_style_prefer_collection_expression = when_types_exactly_match

# 非同期メソッドのConfigureAwait
dotnet_diagnostic.CA2007.severity = warning

# フィールド初期化
dotnet_diagnostic.CA1805.severity = warning

# テストプロジェクトでは一部警告を緩和
[*Tests.cs]
dotnet_diagnostic.CA1849.severity = none
dotnet_diagnostic.CS1998.severity = suggestion
```

### 5.2 コーディング規約の文書化

#### 決定事項の記録
```markdown
## Baketaプロジェクト コーディング決定事項

### コレクション初期化
- 型推論可能で明確な場合: `[]` 構文を使用
- 型推論不可能な場合: `new T()` を使用
- ReadOnlyコレクション: `.AsReadOnly()` で明示的変換

### 非同期プログラミング
- awaitを使用しない場合は `Task.FromResult()` または `Task.CompletedTask`
- ライブラリコードでは `ConfigureAwait(false)` を必須とする
- UIコードでは `ConfigureAwait` を省略可能とする

### 例外処理
- 一般的な例外キャッチは避け、具体的な例外型を指定
- when句を使用して適切な例外フィルタリングを行う
```

## 6. 修正作業のチェックリスト

### 6.1 修正前の準備
- [ ] 現在のビルド状態を確認（エラー・警告の数）
- [ ] 修正対象の警告を分類・優先度付け
- [ ] テストが全て通ることを確認
- [ ] ブランチ作成とコミット履歴の整理

### 6.2 修正中の確認事項
- [ ] 同一種類の警告をまとめて修正
- [ ] 修正後にテストが通ることを確認
- [ ] コードの動作に影響がないことを確認
- [ ] コーディング規約に準拠していることを確認

### 6.3 修正後の検証
- [ ] 全ての警告が解消されていることを確認
- [ ] ビルドが成功することを確認
- [ ] 全てのテストが通ることを確認
- [ ] パフォーマンスに悪影響がないことを確認

## 7. 効率化のためのツールとスクリプト

### 7.1 警告分析スクリプト

#### PowerShell例
```powershell
# 警告の分類と集計
function Analyze-BuildWarnings {
    param([string]$LogFile)
    
    Get-Content $LogFile | 
    Where-Object { $_ -match "warning" } |
    ForEach-Object {
        if ($_ -match "warning\s+(\w+):") {
            $matches[1]
        }
    } |
    Group-Object |
    Sort-Object Count -Descending |
    Format-Table Name, Count -AutoSize
}
```

### 7.2 一括修正用の正規表現

#### よく使用するパターン
```regex
# ConfigureAwaitの追加
検索: await\s+([^;]+);
置換: await $1.ConfigureAwait(false);

# new List<T>()の検出
検索: new\s+List<([^>]+)>\(\)
置換: []  # 文脈に応じて手動調整が必要
```

## 8. 学習と改善

### 8.1 修正作業の振り返り

#### 記録すべき項目
- 修正にかかった時間
- 遭遇した困難とその解決策
- 効果的だった修正パターン
- 今後避けるべき警告の発生原因

### 8.2 継続的改善
- 定期的な警告レビュー（週次/月次）
- 新しい警告の早期発見・対応
- チーム内での知識共有
- ツール・プロセスの改善

## 9. 参考資料

### 9.1 関連ドキュメント
- [C#コーディング基本規約](csharp-standards.md)
- [非同期プログラミングガイドライン](async-programming.md)
- [コード分析警告の対処ガイド](ca-warnings-fixes.md)

### 9.2 外部リンク
- [Microsoft - コード分析ルール](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-style-rule-options)
- [EditorConfig - 設定オプション](https://editorconfig.org/)
- [.NET Analyzer - 警告一覧](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/)

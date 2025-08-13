# Baketa コードレビューワークフロー

## 概要

Baketaプロジェクトでは、Gemini APIが利用できない場合の代替として、静的解析によるコードレビューシステムを提供します。このワークフローは手動レビューと自動チェックを組み合わせて、高品質なコードを保証します。

## ワークフロー図

```
開発完了
    ↓
自動静的解析
    ↓
手動チェックリスト
    ↓
問題修正
    ↓
最終確認
    ↓
マージ承認
```

## 1. 開発完了後の自動レビュー

### 基本レビュー実行
```powershell
# プロジェクトルートで実行
cd E:\dev\Baketa
.\scripts\code-review.ps1 -Detailed
```

### 特定領域のレビュー
```powershell
# アーキテクチャのみ
.\scripts\code-review.ps1 -ArchitectureOnly -Detailed

# パフォーマンスのみ
.\scripts\code-review.ps1 -PerformanceOnly -Detailed

# セキュリティのみ
.\scripts\code-review.ps1 -SecurityOnly -Detailed

# 特定ファイル/ディレクトリ
.\scripts\code-review.ps1 -Path "Baketa.Core" -Detailed
```

### レビュー結果の保存
```powershell
# JSON形式で結果を保存
.\scripts\code-review.ps1 -OutputFormat json -Detailed

# レポートファイルは code-review-results.json として保存される
```

## 2. 手動チェックリストレビュー

自動レビュー完了後、`scripts/code-review-checklist.md`を使用して手動レビューを実行：

### チェックリスト使用手順

1. **該当項目の特定**
   - 変更したコードに関連するチェック項目を特定
   - レイヤー、機能、技術要素に基づいて選択

2. **項目別チェック**
   ```markdown
   - [x] ファイルスコープ名前空間を使用
   - [x] ConfigureAwait(false)を実装
   - [ ] プライマリコンストラクターを活用
   - [x] ReactiveUIパターンを遵守
   ```

3. **重要度別対応**
   - 🔴 **Critical**: 即座に修正が必要
   - 🟡 **Important**: 修正を強く推奨
   - 🟢 **Nice to have**: 時間がある場合に改善

## 3. Gemini API エラー時の代替手順

### 通常のGeminiレビューコマンド
```powershell
# 通常時（Gemini API利用可能）
gemini -p "実装完了しました。以下のコードについてレビューをお願いします。..."
```

### Gemini APIエラー時の代替フロー

#### Step 1: エラー確認
```powershell
# Gemini APIの状況確認
gemini --version
# または
curl -s https://api.google.com/ai/gemini/health
```

#### Step 2: 代替静的レビュー実行
```powershell
# 包括的レビュー
.\scripts\code-review.ps1 -Detailed -OutputFormat json

# 結果の確認
Get-Content code-review-results.json | ConvertFrom-Json | Format-Table
```

#### Step 3: 手動チェックリスト実行
```powershell
# チェックリストを開く
notepad .\scripts\code-review-checklist.md

# または VS Code で開く
code .\scripts\code-review-checklist.md
```

#### Step 4: 問題修正とフォローアップ
問題が検出された場合：
1. 自動検出された問題から優先順位付け
2. Critical/Error レベルの問題を優先修正
3. Warning レベルの問題を検討
4. Info レベルの改善提案を評価

## 4. 問題修正ガイドライン

### アーキテクチャ問題

**クリーンアーキテクチャ違反**
```
問題: UI層がInfrastructure層を直接参照
解決: Application層を通じた間接参照に変更
```

**旧Interfacesネームスペース**
```
問題: using Baketa.Core.Interfaces
解決: using Baketa.Core.Abstractions に変更
```

### C# 12/.NET 8 問題

**ファイルスコープ名前空間**
```csharp
// ❌ 旧形式
namespace Baketa.Core.Services
{
    public class MyService { }
}

// ✅ 新形式
namespace Baketa.Core.Services;

public class MyService { }
```

**ConfigureAwait(false)**
```csharp
// ❌ 問題あり
await SomeAsyncMethod();

// ✅ ライブラリコードで推奨
await SomeAsyncMethod().ConfigureAwait(false);
```

**コレクション式**
```csharp
// ❌ 旧形式
var list = new List<string>();

// ✅ C# 12形式
var list = string[];
```

### Baketa固有問題

**IDisposable適切な使用**
```csharp
// ❌ 問題あり
var image = new Bitmap(path);
ProcessImage(image);

// ✅ 適切
using var image = new Bitmap(path);
ProcessImage(image);
```

**ReactiveUI ViewModelBase継承**
```csharp
// ❌ 問題あり
public class SettingsViewModel : INotifyPropertyChanged

// ✅ 適切
public class SettingsViewModel : ViewModelBase
```

## 5. レビュー品質基準

### 合格基準
- **Error件数**: 0件
- **Critical Warning件数**: 0件
- **Warning件数**: 2件以下（理由が明確な場合）
- **アーキテクチャ準拠**: 100%
- **セキュリティ問題**: 0件

### 条件付き合格
- **Warning件数**: 5件以下（改善計画あり）
- **Performance問題**: 軽微で影響が限定的
- **Info レベル**: 改善余地はあるが機能に影響なし

### 再レビュー必要
- **Error件数**: 1件以上
- **Critical Warning件数**: 1件以上
- **アーキテクチャ違反**: あり
- **セキュリティ問題**: あり

## 6. 継続的改善

### 週次レビュー
```powershell
# 週次での全体健全性チェック
.\scripts\code-review.ps1 -OutputFormat json
```

### 月次レポート
```powershell
# 傾向分析用のデータ収集
Get-Content code-review-results.json | ConvertFrom-Json | 
    Group-Object {$_.Issues.Category} | 
    Select-Object Name, Count
```

### チェックリスト更新
- 新しい問題パターンの発見時
- アーキテクチャ変更時
- C#/.NET新バージョン対応時
- Baketa特有の新機能追加時

## 7. チーム連携

### レビュー結果共有
```powershell
# レビュー結果をMarkdown形式で生成
.\scripts\code-review.ps1 -Detailed > review-report.md
```

### 問題トラッキング
- GitHub Issues との連携
- 技術債務の管理
- 改善優先順位の決定

### ナレッジ共有
- 頻出問題のドキュメント化
- ベストプラクティスの共有
- 新メンバーオンボーディング

## 8. トラブルシューティング

### よくある問題

**ripgrep not found**
```powershell
# ripgrepのインストール
winget install BurntSushi.ripgrep.MSVC
# または
choco install ripgrep
```

**PowerShell実行ポリシー**
```powershell
# 実行ポリシーの確認と変更
Get-ExecutionPolicy
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

**パス関連エラー**
```powershell
# スクリプトディレクトリで実行
cd E:\dev\Baketa\scripts
.\code-review.ps1
```

### ログとデバッグ
```powershell
# 詳細出力でのデバッグ
.\scripts\code-review.ps1 -Detailed -Verbose
```

## 9. スクリプトカスタマイズ

### 新しいチェックルールの追加

1. `code-review.ps1`に新しい関数を追加
2. チェックロジックを実装
3. `Add-Issue`で問題を記録
4. テストとドキュメント更新

### 例: 新しいチェック関数
```powershell
function Test-CustomPattern {
    Write-Host "🔍 カスタムパターンチェック..." -ForegroundColor Yellow
    
    $issues = rg --type cs "YourPattern" "$ProjectRoot" 2>$null
    if ($issues) {
        $issues | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Warning" -Category "Custom" `
                -Description "カスタムパターンが検出されました"
        }
    }
}
```

## 10. 効果測定

### メトリクス収集
- レビュー実行回数
- 検出問題数の推移
- 修正率
- 再発率

### 改善効果
- バグ減少率
- 開発速度向上
- コード品質向上
- 技術債務削減

---

## クイックリファレンス

```powershell
# 基本レビュー
.\scripts\code-review.ps1

# 詳細レビュー
.\scripts\code-review.ps1 -Detailed

# アーキテクチャのみ
.\scripts\code-review.ps1 -ArchitectureOnly

# JSON出力
.\scripts\code-review.ps1 -OutputFormat json

# 特定パス
.\scripts\code-review.ps1 -Path "Baketa.UI"
```

このワークフローにより、Gemini APIが利用できない場合でも、Baketaプロジェクトの高品質なコードレビューを継続できます。
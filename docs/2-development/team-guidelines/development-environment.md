# Baketa プロジェクト開発環境ガイドライン

## 1. 開発環境要件

Baketaプロジェクトの開発には、以下の環境が必要です：

### 1.1 必須ソフトウェア

| ソフトウェア | 最低バージョン | 推奨バージョン | 備考 |
|-------------|---------------|--------------|------|
| Visual Studio | 17.8 | 17.9+ | Enterpriseまたは Professional |
| .NET SDK | 8.0.100 | 8.0.200+ | x64アーキテクチャのみ対応 |
| Git | 2.30.0 | 2.40.0+ | |
| PowerShell | 7.2 | 7.4+ | Windows PowerShell 5.1も可 |

### 1.2 拡張機能・ツール

| 拡張機能 | 目的 | 必須/推奨 |
|---------|------|----------|
| Roslyn Analyzers | コード品質分析 | 必須 |
| .NET Productivity Tools | 生産性向上 | 推奨 |
| Visual Studio IntelliCode | コード補完強化 | 推奨 |
| GitHub Copilot | AI支援コーディング | オプション |

## 2. 環境設定手順

### 2.1 Visual Studio設定

1. **言語バージョン確認**:
   - プロジェクトでは `Directory.Build.props` によりC# 12が有効化されていますが、正しく認識されているか確認してください。
   - 確認方法: ソリューションプロパティ → ビルド → 詳細設定 → 言語バージョン

2. **コード分析設定**:
   - コード分析は「すべての分析ツールを有効」に設定してください。
   - 設定方法: ツール → オプション → テキストエディター → C# → コード分析

### 2.2 .NET SDKバージョン確認

以下のコマンドを実行し、.NET 8 SDKが正しくインストールされていることを確認してください：

```powershell
dotnet --info
```

出力に `8.0.xxx` のSDKエントリが含まれていることを確認してください。

### 2.3 Gitの設定

リポジトリのラインエンディング設定を統一するため、以下の設定を適用してください：

```bash
git config --global core.autocrlf true
```

## 3. C# 12機能の使用ガイドライン

### 3.1 標準的な使用パターン

以下のC# 12機能は積極的に使用することを推奨します：

1. **コレクション式**: 
   ```csharp
   // 空のコレクション
   var emptyArray = [];
   
   // 要素を持つコレクション
   var items = [1, 2, 3];
   
   // スプレッド演算子
   var combined = [..first, ..second];
   ```

2. **プライマリコンストラクタとメンバー初期化の拡張**:
   ```csharp
   // オプションパラメータを持つプライマリコンストラクタ
   public class Config(string name, bool enabled = true, int timeout = 30)
   {
       public string Name { get; } = name;
       public bool Enabled { get; } = enabled;
   }
   ```

### 3.2 注意が必要な機能

以下の機能は特定の条件下でのみ使用してください：

1. **インラインアレイ**: パフォーマンスが重要な場面でのみ使用
2. **interceptors**: 現状では使用しない（プレビュー機能のため）

## 4. トラブルシューティング

### 4.1 ビルドエラーの対処

C# 12機能使用時にエラーが発生した場合：

1. Visual Studioの更新: 最新バージョンにアップデート
2. .NET SDKの更新: 最新の8.0.x SDKをインストール
3. プロジェクト設定の確認: `Directory.Build.props`が正しく適用されているか確認
4. ローカルキャッシュのクリア: 
   ```powershell
   dotnet nuget locals all --clear
   ```

### 4.2 IDE警告への対応

1. **IDE0300** (コレクションの初期化を簡素化できます):
   - C# 12のコレクション式を使用して修正
   - 例: `new byte[0]` → `[]`

2. **CA1825** (不要な長さ0の配列の割り当て):
   - コレクション式 `[]` を使用して修正

## 5. 開発環境の統一確認

チーム内で開発環境を統一するために、以下の確認作業を行ってください：

1. **環境チェックスクリプトの実行**:
   ```powershell
   .\scripts\check-environment.ps1
   ```
   (Note: このスクリプトは別途作成する必要があります)

2. **定期的な環境更新**:
   - 毎月第一週に開発環境の更新を行うことを推奨
   - アップデート後に上記スクリプトを再実行

## 6. サポートとリソース

- **.NET 8ドキュメント**: https://docs.microsoft.com/ja-jp/dotnet/core/whats-new/dotnet-8
- **C# 12言語仕様**: https://learn.microsoft.com/ja-jp/dotnet/csharp/whats-new/csharp-12
- **内部Wiki**: (社内リンク)
- **Teamsチャンネル**: #baketa-dev-support
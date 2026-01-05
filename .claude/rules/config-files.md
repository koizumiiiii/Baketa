---
description: 設定ファイル編集時の注意事項
globs:
  - "**/*.json"
  - "**/*.csproj"
  - "**/appsettings*.json"
  - "**/Directory.Build.props"
---

# 設定ファイルルール

## 編集前の確認
設定ファイルは影響範囲が広いため、編集前に以下を確認:

1. **変更の必要性**: 本当にこの設定変更が必要か
2. **影響範囲**: 他のプロジェクトや環境への影響
3. **バックアップ**: Git で変更履歴が追跡されているか

## appsettings.json

### 環境別ファイル
- `appsettings.json` - 基本設定（本番）
- `appsettings.Development.json` - 開発環境上書き
- `appsettings.SentencePiece.json` - レガシー（非推奨）

### 機密情報
以下は appsettings に含めない:
- APIキー
- パスワード
- 接続文字列（本番用）

## *.csproj

### 変更時の注意
- `<TargetFramework>` の変更は全体に影響
- `<PackageReference>` の追加はビルド時間に影響
- `<ProjectReference>` の追加はアーキテクチャに影響

### 必須項目
```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

## Directory.Build.props
- ソリューション全体に影響
- 変更前に必ず全プロジェクトのビルドを確認

---
description: ソリューションをビルドしてエラーをチェック
---

# ビルド実行

ソリューション全体をビルドしてエラーをチェックします。

## 標準ビルド
```bash
dotnet build
```

## Releaseビルド
```bash
dotnet build --configuration Release
```

## x64指定ビルド（推奨）
```bash
dotnet build --configuration Debug --arch x64
```

## クリーンビルド
```bash
dotnet clean && dotnet build
```

## ネイティブDLLビルド（必要時）
```bash
# Visual Studio 2022 Developer Command Prompt から実行
msbuild BaketaCaptureNative\BaketaCaptureNative.sln /p:Configuration=Debug /p:Platform=x64
```

## ビルドエラー対応

### よくあるエラー
1. **CS0246 (型が見つからない)**: using文の追加、NuGetパッケージの復元
2. **CS0103 (名前が存在しない)**: 名前空間の確認、タイポチェック
3. **CS1061 (メンバーが含まれない)**: インターフェース定義の確認

### トラブルシューティング
```bash
# NuGetパッケージ復元
dotnet restore

# objフォルダクリア
dotnet clean
```

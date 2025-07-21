# Baketa ビルドガイド

## 🚀 クイックスタート

```powershell
# 1. 自動ビルドスクリプト実行（推奨）
.\scripts\build_all.ps1

# 2. アプリケーション実行
dotnet run --project Baketa.UI
```

## 📋 前提条件

### 必須環境
- **Windows 10/11** (64-bit)
- **Visual Studio 2022** (Community/Professional/Enterprise)
- **.NET 8.0 SDK**
- **Git** (クローン用)

### Visual Studio 2022 ワークロード
以下のコンポーネントが必要です：

```
✅ C++によるデスクトップ開発
✅ .NET デスクトップ開発
✅ Windows 10/11 SDK (19041.0以上)
✅ CMake ツール
```

## 🏗️ ビルドプロセス

### 方法1: 自動ビルドスクリプト（推奨）

```powershell
# 基本ビルド
.\scripts\build_all.ps1

# Releaseビルド
.\scripts\build_all.ps1 -Configuration Release

# 詳細ログ表示
.\scripts\build_all.ps1 -Verbose

# ネイティブDLLのみビルド
.\scripts\build_all.ps1 -SkipDotNet

# .NETプロジェクトのみビルド
.\scripts\build_all.ps1 -SkipNative
```

### 方法2: 手動ビルド

```cmd
# 1. ネイティブDLLビルド
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
msbuild BaketaCaptureNative\BaketaCaptureNative.sln /p:Configuration=Debug /p:Platform=x64

# 2. DLLコピー
copy BaketaCaptureNative\bin\Debug\BaketaCaptureNative.dll Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\

# 3. .NETソリューションビルド
dotnet build Baketa.sln --configuration Debug

# 4. アプリケーション実行
dotnet run --project Baketa.UI
```

## 🔧 トラブルシューティング

### よくある問題

#### 問題1: Visual Studio 2022が見つからない
```
エラー: Visual Studio 2022 が見つかりません
```

**解決策:**
1. Visual Studio 2022をインストール
2. C++デスクトップ開発ワークロードを追加
3. Windows SDKを最新化

#### 問題2: ネイティブDLLビルド失敗
```
エラー: C2589 スコープ解決演算子エラー
```

**解決策:**
```powershell
# ファイルをUTF-8 with BOMで保存し直す
# Visual Studioで以下を実行:
# 1. ファイル → 保存オプションの詳細設定
# 2. エンコード: Unicode (UTF-8 with signature)
```

#### 問題3: DLL Not Found Exception
```
エラー: System.DllNotFoundException: BaketaCaptureNative.dll
```

**解決策:**
```powershell
# DLLが正しい場所にあるか確認
ls Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\BaketaCaptureNative.dll

# なければ手動コピー
Copy-Item BaketaCaptureNative\bin\Debug\BaketaCaptureNative.dll Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\
```

#### 問題4: MarshalDirectiveException（解決済み）
```
エラー: System.Runtime.InteropServices.MarshalDirectiveException
```

**解決策:**
✅ この問題はネイティブDLL実装により解決済みです。
ネイティブDLLが正しくビルド・配置されていることを確認してください。

### デバッグモード

```powershell
# デバッグ情報付きビルド
.\scripts\build_all.ps1 -Configuration Debug -Verbose

# 個別コンポーネント確認
.\scripts\build_all.ps1 -SkipDotNet  # ネイティブDLLのみ
.\scripts\build_all.ps1 -SkipNative  # .NETのみ
```

## 📁 ビルド成果物

### Debug ビルド
```
Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\
├── Baketa.UI.exe                    # メインアプリケーション
├── BaketaCaptureNative.dll           # Windows Graphics Capture API
├── BaketaCaptureNative.pdb           # ネイティブDLLデバッグ情報
├── Baketa.*.dll                     # .NETアセンブリ
└── runtimes\                         # ランタイム依存関係
```

### Release ビルド
```powershell
# 配布用Releaseビルド
.\scripts\build_all.ps1 -Configuration Release

# パッケージング（手動）
dotnet publish Baketa.UI -c Release -r win-x64 --self-contained
```

## 🧪 テスト実行

```powershell
# 全テスト実行
dotnet test

# 特定プロジェクトのテスト
dotnet test tests/Baketa.Core.Tests/
dotnet test tests/Baketa.Infrastructure.Tests/

# カバレッジ付きテスト
dotnet test --collect:"XPlat Code Coverage"
```

## 🚀 開発用スクリプト

### その他の有用なスクリプト
```powershell
# 環境チェック
.\scripts\check-environment.ps1

# モデルダウンロード
.\scripts\download_opus_mt_models.ps1

# クリーンビルド
git clean -fdx
.\scripts\build_all.ps1
```

## 🔄 CI/CDパイプライン

### GitHub Actions（実装予定）
```yaml
# .github/workflows/build.yml
name: Build and Test
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Setup MSBuild
        uses: microsoft/setup-msbuild@v2
      - name: Build Native DLL
        run: .\scripts\build_all.ps1 -Configuration Release
      - name: Run Tests
        run: dotnet test --logger trx --results-directory TestResults
```

## 📦 配布要件

### エンドユーザー環境
- **Windows 10 version 1903以降**
- **Visual C++ 2019/2022 Redistributable (x64)**
- **.NET 8.0 Windows Desktop Runtime**

### 配布パッケージ内容
```
Baketa-v1.0.0-win-x64\
├── Baketa.UI.exe
├── BaketaCaptureNative.dll          # 🆕 ネイティブDLL
├── 依存関係DLL群...
├── Models\                          # OCR/翻訳モデル
└── runtimes\win-x64\native\         # ネイティブランタイム
```

## 🤝 開発者向け注意事項

### ビルド順序（重要）
1. **ネイティブDLL優先**: Visual Studio 2022でC++プロジェクトを先にビルド
2. **DLL配置**: .NET出力ディレクトリに自動コピー
3. **.NETビルド**: dotnet buildで.NETソリューション

### コード変更時の注意
- **ネイティブDLL変更**: Visual Studio 2022でのリビルド必須
- **P/Invoke変更**: 関数シグネチャの一致を確認
- **文字コード**: C++ファイルはUTF-8 with BOMで保存

---

**何か問題が発生した場合は、GitHub Issuesで報告してください。**
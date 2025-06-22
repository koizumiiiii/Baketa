# Baketa Phase3 エラー解決 トラブルシューティングガイド

## 🚨 発生したエラーと解決策

### エラー1: CS0101 - UiTheme/UiSize重複定義
```
E:\dev\Baketa\Baketa.Core\Settings\UiTheme.cs(31,13): error CS0101: 名前空間 'Baketa.Core.Settings' は既に 'UiSize' の定義を含んでいます
E:\dev\Baketa\Baketa.Core\Settings\UiTheme.cs(7,13): error CS0101: 名前空間 'Baketa.Core.Settings' は既に 'UiTheme' の定義を含んでいます
```

**解決済み**: UiTheme.csファイルを再作成しました

### エラー2: NuGetアクセス拒否
```
C:\Program Files\dotnet\sdk\9.0.300\NuGet.targets(186,5): error : Access to the path 'Fody.dll' is denied.
```

**原因**: Visual StudioやMSBuildプロセスがDLLをロック中

## 🔧 徹底的解決手順

### ステップ1: 徹底クリーンアップ実行

```powershell
# 管理者権限でPowerShellを開く
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
cd E:\dev\Baketa

# 徹底的クリーンアップ実行
.\thorough_cleanup.ps1
```

### ステップ2: Visual Studio完全再起動

1. **Visual Studioを完全に閉じる**
2. **タスクマネージャーで以下プロセスを確認・終了**:
   - devenv.exe
   - MSBuild.exe  
   - dotnet.exe
   - VBCSCompiler.exe
   - ServiceHub.*.exe

3. **Visual Studioを再起動**

### ステップ3: 手動確認

```bash
# エラーが解決されているかチェック
dotnet clean Baketa.sln
dotnet restore Baketa.sln
dotnet build Baketa.sln --verbosity minimal
```

## 🛠️ 代替解決策（上記で解決しない場合）

### 方法1: 完全リセット

```powershell
# 1. プロジェクト全体のgit reset（注意: 未コミット変更が失われます）
git clean -fdx
git reset --hard HEAD

# 2. 再度修正適用
# ISettingsService.cs の制約修正を再実行
# テストファイルのReactiveCommand修正を再実行
```

### 方法2: マニュアルファイル削除

```powershell
# NuGetキャッシュの手動削除
Remove-Item -Path "$env:USERPROFILE\.nuget\packages" -Recurse -Force
Remove-Item -Path "$env:LOCALAPPDATA\NuGet\v3-cache" -Recurse -Force

# Visual Studioコンポーネントキャッシュ削除
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\VisualStudio" -Recurse -Force
```

### 方法3: .NET SDK再インストール

```bash
# 最後の手段: .NET SDK 8.0の再インストール
# Microsoft公式サイトから最新版をダウンロード
```

## 📋 修正確認チェックリスト

### ✅ 必須チェック項目

- [ ] **プロセス終了**: devenv, MSBuild, dotnetプロセスが完全に終了している
- [ ] **ファイル削除**: bin, obj, .vsディレクトリが削除されている  
- [ ] **NuGetクリア**: パッケージキャッシュがクリアされている
- [ ] **制約修正**: ISettingsService.cs の`where T : class, new()`制約
- [ ] **テスト修正**: ReactiveCommandのCanExecuteパターン修正
- [ ] **UiTheme**: 重複定義が解消されている

### ✅ ビルド成功確認

```bash
# 段階的ビルド確認
dotnet build Baketa.Core\Baketa.Core.csproj        # ✅ 成功
dotnet build Baketa.Infrastructure\Baketa.Infrastructure.csproj  # ✅ 成功  
dotnet build Baketa.sln                            # ✅ 成功

# テスト実行確認
dotnet test --verbosity minimal                    # ✅ 全テスト通過
```

## 🎯 最終目標

- **コンパイルエラー**: 0件
- **テスト失敗**: 0件
- **警告**: 最小限（重要でない警告のみ）
- **ビルド時間**: 2分以内
- **Phase4準備**: 完了

## 🆘 それでも解決しない場合

1. **システム再起動**: PCを完全に再起動
2. **環境変数確認**: PATH, DOTNET_ROOT等の確認
3. **ファイルシステム権限**: プロジェクトフォルダの読み書き権限確認
4. **ウイルスソフト**: ビルドファイルの除外設定確認

---

**このガイドに従って段階的に実行すれば、Phase3のエラーは完全に解決されるはずです。**

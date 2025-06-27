# Claude Code 環境設定ガイド

## PATH問題の解決

Claude CodeでBash環境を使用する際に、Windows環境変数のPATHが認識されない問題があります。

### 問題の症状
```
/bin/bash: line 1: dotnet: command not found
```

### 解決方法

#### 1. PowerShell使用の強制（推奨）
Claude Codeには常にPowerShell使用を指示する：

```
"PowerShellで実行してください: dotnet [コマンド]"
```

#### 2. フルパス指定
```
"以下をフルパスで実行: 'C:\Program Files\dotnet\dotnet.exe' [コマンド]"
```

#### 3. 専用スクリプト使用
```
".\scripts\run_tests.ps1 を使用してテストを実行してください"
```

### .claude/instructions.md での指示

```markdown
**重要: すべての.NET CLIコマンドはPowerShellで実行してください**
- Bash環境ではPATHの問題により dotnet コマンドが認識されません
- 必ず「PowerShellで実行してください」を指示に含めてください
```

### よくあるコマンドパターン

#### ビルド
```
PowerShellで実行: dotnet build --configuration Debug --arch x64
```

#### テスト
```
PowerShellで実行: dotnet test tests/[プロジェクト名]/ --logger "console;verbosity=detailed"
```

#### 実行
```
PowerShellで実行: dotnet run --project Baketa.UI
```

### Claude Code コマンド例

```bash
# ❌ 失敗パターン
claude "dotnet test を実行して"

# ✅ 成功パターン
claude "PowerShellで以下を実行してください: dotnet test tests/Baketa.UI.Tests/"
```

この設定により、PATH問題を回避して確実にコマンドを実行できます。
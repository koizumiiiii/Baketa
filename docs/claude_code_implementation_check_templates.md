# Claude Code 実装完了チェックテンプレート

## 基本的な実装完了確認コマンド

### 🔧 通常の実装完了時
```bash
claude "【実装完了・エラーチェック必須】
実装が完了しました。PowerShellで以下のチェックを実行してください：
1. .\scripts\check_implementation.ps1
2. 結果を以下の形式で報告してください：

✅ 実装完了チェック結果:
- コンパイルエラー: なし / [N]件
- Code Analysis警告: なし / [N]件  
- テスト結果: 成功 / 失敗[N]件
- 実装内容: [実装内容の簡潔な説明]"
```

### 🚀 大規模な変更の実装完了時
```bash
claude "【大規模実装完了・詳細チェック必須】
大規模な実装が完了しました。PowerShellで以下の詳細チェックを実行してください：
1. .\scripts\check_implementation.ps1 -Detailed
2. 全テストスイート実行: .\scripts\run_tests.ps1 -Verbosity detailed
3. 詳細な結果レポートを提供してください"
```

### ⚡ 簡単な修正の完了時
```bash
claude "【簡易修正完了・基本チェック】
修正が完了しました。PowerShellで基本チェックを実行してください：
1. .\scripts\run_build.ps1 -Verbosity normal
2. エラーがないことを確認して報告してください"
```

## 自動チェックコマンドのバリエーション

### 📋 標準チェック
```bash
claude "PowerShellで実装完了チェックを実行してください: .\scripts\check_implementation.ps1"
```

### 📋 詳細チェック  
```bash
claude "PowerShellで詳細な実装完了チェックを実行してください: .\scripts\check_implementation.ps1 -Detailed"
```

### 📋 テストスキップチェック
```bash
claude "PowerShellでビルドエラーのみチェックを実行してください: .\scripts\check_implementation.ps1 -SkipTests"
```

### 📋 特定テストのみチェック
```bash
claude "PowerShellで特定のテストを含むチェックを実行してください: .\scripts\check_implementation.ps1 -TestFilter 'TestClassName'"
```

## エラー発見時の対応テンプレート

### 🚨 コンパイルエラー発見時
```bash
claude "【緊急修正必要】
コンパイルエラーが発見されました。以下を実行してください：
1. PowerShellでエラー詳細確認: dotnet build --verbosity detailed
2. エラーの根本原因を分析して修正
3. 修正後に再度チェック: .\scripts\check_implementation.ps1"
```

### ⚠️ 警告発見時
```bash
claude "【警告対応検討】
Code Analysis警告が発見されました。以下を実行してください：
1. PowerShellで警告詳細確認: .\scripts\check_implementation.ps1 -Detailed
2. 警告の根本原因を分析
3. 適切な修正または抑制を実施
4. 再チェック実行"
```

### 🧪 テスト失敗時
```bash
claude "【テスト修正必要】
テストの失敗が発見されました。以下を実行してください：
1. PowerShellで失敗テスト詳細確認: .\scripts\run_tests.ps1 -Verbosity detailed
2. 失敗の原因を分析（実装側 vs テスト側）
3. 適切な修正を実施
4. 再チェック実行"
```

## 成功パターンのテンプレート

### 🎉 完全成功時の報告例
```
✅ 実装完了チェック結果:
- コンパイルエラー: なし
- Code Analysis警告: なし
- テスト結果: 成功
- 実装内容: OCRフィルターの新規実装とテスト追加
```

### ⚠️ 警告ありだが実装完了時の報告例
```
✅ 実装完了チェック結果:
- コンパイルエラー: なし
- Code Analysis警告: 2件（IDE0290 プライマリコンストラクタ関連）
- テスト結果: 成功
- 実装内容: 翻訳エンジン設定クラスの追加
- 備考: 警告は既存のコーディングスタイルとの一貫性のため一時的に許容
```

## Claude Code での使用例

### 実装中の作業
```bash
# 実装指示
claude "【自動承認・日本語回答】新しいOCRフィルターを実装してください"

# 実装完了後の確認指示
claude "実装完了後、PowerShellで以下を実行してエラーチェックしてください: .\scripts\check_implementation.ps1"
```

### ワンライナーでの実装+チェック指示
```bash
claude "【実装+エラーチェック】以下を実行してください：
1. 新しいOCRフィルターを実装
2. 実装完了後にPowerShellで .\scripts\check_implementation.ps1 を実行
3. 結果を報告"
```

## 習慣化のためのリマインダー

### 💡 毎回の実装後に確認すること
1. **コンパイルエラー**: 実装未完了の証拠
2. **新しい警告**: コード品質の低下リスク
3. **テスト失敗**: 機能の回帰リスク
4. **実装内容の記録**: 変更履歴の明確化

### 🎯 目標
- **コンパイルエラー 0件**: 常に維持
- **警告の管理**: 新規警告は必ず対処または理由明記
- **テスト成功率**: 既存テストの維持、新規テストの追加
- **実装品質**: 根本原因解決アプローチの継続

この設定により、Claude Codeでの実装完了時に必ずエラーチェックが実行され、高品質なコードを維持できます。
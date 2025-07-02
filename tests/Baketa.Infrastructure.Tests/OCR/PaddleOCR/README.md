# PaddleOCR テスト実行ガイド

このドキュメントでは、Issue #37「PaddleOCR統合基盤の構築」Phase 4で実装されたテストスイートの実行方法について説明します。

## 📋 テスト概要

### 実装されたテスト
- **単体テスト**: 98メソッド（PaddleOcrEngine、OcrResult、Initializer等）
- **統合テスト**: 32メソッド（エンドツーエンド、DIコンテナ統合）
- **パフォーマンステスト**: 12メソッド（処理時間、メモリ使用量、同時実行性）
- **エラーハンドリングテスト**: 5メソッド（例外安全性、状態保持）

### テスト対象コンポーネント
- `PaddleOcrEngine` - OCR実行エンジン
- `PaddleOcrInitializer` - 初期化システム
- `OcrResult/OcrResultCollection` - OCR結果処理
- `ModelPathResolver` - モデルパス管理
- `PaddleOcrModule` - DIモジュール統合

## 🚀 テスト実行手順

### 1. 前提条件の確認

```bash
# .NET 8.0 SDK のインストール確認
dotnet --version
# 8.0.x が表示されることを確認
```

### 2. ソリューション復元とビルド

```bash
# プロジェクトルートで実行
cd E:\dev\Baketa

# NuGetパッケージの復元
dotnet restore

# ソリューション全体のビルド
dotnet build --configuration Debug
```

### 3. テスト実行

#### 全テスト実行
```bash
# Infrastructure.Testsプロジェクトの全テストを実行
dotnet test tests\Baketa.Infrastructure.Tests --configuration Debug --verbosity normal
```

#### PaddleOCRテストのみ実行
```bash
# PaddleOCR関連テストのみを実行
dotnet test tests\Baketa.Infrastructure.Tests --filter "FullyQualifiedName~PaddleOCR" --verbosity normal
```

#### カテゴリ別テスト実行

```bash
# 単体テストのみ実行
dotnet test tests\Baketa.Infrastructure.Tests --filter "FullyQualifiedName~Unit" --verbosity normal

# 統合テストのみ実行
dotnet test tests\Baketa.Infrastructure.Tests --filter "FullyQualifiedName~Integration" --verbosity normal

# パフォーマンステストのみ実行
dotnet test tests\Baketa.Infrastructure.Tests --filter "TestCategory=Performance" --verbosity normal
```

#### 特定テストクラス実行

```bash
# エンジンテストのみ実行
dotnet test tests\Baketa.Infrastructure.Tests --filter "FullyQualifiedName~PaddleOcrEngineTests" --verbosity normal

# エラーハンドリングテストのみ実行
dotnet test tests\Baketa.Infrastructure.Tests --filter "FullyQualifiedName~PaddleOcrErrorHandlingTests" --verbosity normal
```

### 4. カバレッジレポート生成

```bash
# コードカバレッジ付きでテスト実行
dotnet test tests\Baketa.Infrastructure.Tests --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~PaddleOCR"
```

## 📊 テスト結果の確認

### 成功時の出力例
```
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   147, Skipped:     0, Total:   147, Duration: 45 s
```

### パフォーマンステスト結果例
```
[xUnit.net 00:00:02.15]   PaddleOcrPerformanceTests: 初期化時間: 892ms
[xUnit.net 00:00:02.34]   PaddleOcrPerformanceTests: 言語切り替え時間: 156ms
[xUnit.net 00:00:02.45]   PaddleOcrPerformanceTests: 単一OCR実行時間: 89ms
```

## 🔧 トラブルシューティング

### よくある問題と解決方法

#### 1. テスト実行時のタイムアウト
```
問題: テストが30秒でタイムアウトする
解決: より長いタイムアウトを設定
```
```bash
dotnet test --logger "console;verbosity=detailed" -- RunConfiguration.TestSessionTimeout=120000
```

#### 2. 一時ディレクトリ関連エラー
```
問題: System.UnauthorizedAccessException: Access to the path is denied
解決: テスト用一時ディレクトリの権限確認
```
```bash
# TEMPディレクトリの確認
echo %TEMP%
# 権限の確認後、管理者権限でテスト実行
```

#### 3. PaddleOCRライブラリロードエラー
```
問題: Unable to load DLL 'onnxruntime'
解決: ネイティブライブラリの確認
```
```bash
# x64プラットフォームでビルド・実行
dotnet build --configuration Debug --runtime win-x64
dotnet test --configuration Debug --runtime win-x64
```

#### 4. メモリ不足エラー
```
問題: OutOfMemoryException during performance tests
解決: GCの強制実行とヒープサイズ確認
```
```bash
# 環境変数設定
set DOTNET_gcServer=1
set DOTNET_gcConcurrent=1
dotnet test
```

## 🎯 パフォーマンス基準

### 期待される実行時間（参考値）
- **初期化時間**: < 5,000ms
- **言語切り替え**: < 1,000ms  
- **単一OCR実行**: < 2,000ms
- **10並列OCR**: < 3,000ms

### メモリ使用量基準
- **メモリリーク**: < 1MB/iteration
- **スループット**: > 1.0 req/sec
- **性能変動**: 変動係数 < 0.5

## 📝 テスト設定のカスタマイズ

### paddleocr-test-settings.json編集
```json
{
  "TestSettings": {
    "PaddleOCR": {
      "TestTimeout": 60000,  // タイムアウト時間（ms）
      "MaxExecutionTime": {
        "Initialization": 10000,  // 初期化制限時間
        "LanguageSwitch": 2000    // 言語切り替え制限時間
      }
    }
  }
}
```

## 🔍 継続的インテグレーション

### GitHub Actions設定例
```yaml
- name: Run PaddleOCR Tests
  run: |
    dotnet test tests/Baketa.Infrastructure.Tests 
    --filter "FullyQualifiedName~PaddleOCR" 
    --configuration Release 
    --logger trx 
    --results-directory TestResults
```

### ローカル開発での推奨フロー
1. **開発時**: 単体テストを頻繁に実行
2. **コミット前**: 統合テスト実行
3. **リリース前**: パフォーマンステスト実行
4. **デバッグ時**: エラーハンドリングテスト実行

## 📞 サポート

### 問題報告
テスト実行で問題が発生した場合は、以下の情報と共にIssueを作成してください：

- OS/バージョン情報
- .NET SDKバージョン
- 完全なエラーメッセージ
- テスト実行コマンド
- テスト設定ファイルの内容

### 追加ドキュメント
- [Issue #37 Phase 4完了レポート](../phase1_reports/issue37_phase4_completion.md)
- [PaddleOCR統合基盤アーキテクチャ](../../3-architecture/ocr-system/ocr-implementation.md)
- [テストベストプラクティス](../../4-testing/guidelines/mocking-best-practices.md)

---

**更新日**: 2025年6月6日  
**対象バージョン**: Issue #37 Phase 4完了版  
**作成者**: Claude

# Baketa Project Development Instructions

## 基本原則 / Core Principles

### **エディタ動作設定 / Editor Behavior Settings**
**AUTO-APPROVE ALL EDITS / すべての編集を自動承認**

- Claude Codeでの編集時は、信頼できるプロジェクト環境のため**自動承認**を推奨
- ファイル編集確認ダイアログでは「Yes, and don't ask again this session」を選択
- バックアップは Git で管理されているため、安全に自動承認可能
- 大規模な変更の場合のみ、事前確認を求める

### **実装完了時の必須手順 / Mandatory Post-Implementation Steps**
**CRITICAL: ALWAYS VERIFY NO ERRORS AFTER IMPLEMENTATION / 重要: 実装後は必ずエラーチェックを実行**

すべてのコード実装、修正、リファクタリング完了後に以下を**必ず実行**してください：

#### **1. コンパイルエラーチェック（必須）**
```powershell
# 全体ビルドでコンパイルエラーを確認
PowerShellで実行してください: .\scripts\run_build.ps1 -Verbosity normal
```

#### **2. Code Analysis警告チェック（必須）**
```powershell
# 警告を詳細ログで確認
PowerShellで実行してください: dotnet build --verbosity normal | findstr "warning"
```

#### **3. テスト実行（関連ファイル修正時必須）**
```powershell
# 影響するテストを実行
PowerShellで実行してください: .\scripts\run_tests.ps1 -Verbosity detailed
```

#### **4. エラー報告フォーマット**
実装完了後の報告は以下の形式で行ってください：

```
✅ 実装完了チェック結果:
- コンパイルエラー: なし / [N]件
- Code Analysis警告: なし / [N]件
- テスト結果: 成功 / 失敗[N]件
- 実装内容: [簡潔な実装内容の説明]
```

#### **5. エラーがある場合の対応**
- **コンパイルエラー**: 即座に修正、実装未完了として扱う
- **新しい警告**: 根本原因を分析して修正または抑制
- **テスト失敗**: 関連するテストを修正または実装を調整

#### **6. エラーチェック自動化コマンド**
```powershell
# 一括エラーチェック（推奨）
PowerShellで実行してください: .\scripts\check_implementation.ps1
```
**CRITICAL: ALL .NET CLI COMMANDS MUST USE POWERSHELL / 重要: すべての.NET CLIコマンドはPowerShellを使用**

- **Bash環境の問題**: Claude CodeのBash環境ではWindowsのPATH設定が認識されない
- **必須指示**: すべてのdotnetコマンド実行時は「PowerShellで実行してください」を明記
- **フォールバック**: PowerShellが使用できない場合はフルパス指定: `'C:\Program Files\dotnet\dotnet.exe'`
- **専用スクリプト**: `scripts/run_tests.ps1`, `scripts/run_build.ps1` を優先使用

### **言語指定 / Language Specification**
**ALL RESPONSES MUST BE IN JAPANESE / すべての回答は日本語で行うこと**

- Claude Codeからの回答は**常に日本語**を使用してください
- コードコメントは英語で書き、説明は日本語で行ってください
- エラーメッセージやログは元の言語のまま、説明は日本語で追加してください
- 技術用語は日本語に翻訳するか、英語併記（英語/日本語）で記載してください

### プロジェクト理解 / Project Understanding
- Baketaは**Windows専用**のリアルタイムゲームテキスト翻訳オーバーレイアプリケーションです
- OCR技術を使用してゲーム画面からテキストを検出し、翻訳結果をオーバーレイ表示します
- アーキテクチャは高性能と低リソース消費を重視しています

### アーキテクチャ準拠 / Architecture Compliance
- 5層クリーンアーキテクチャ構造を厳密に遵守してください
- 依存関係は内側の層から外側の層へ向かう方向のみです
- プラットフォーム依存コードは`Baketa.Infrastructure.Platform`に分離してください

### **根本的実装哲学：根本原因解決 / FUNDAMENTAL IMPLEMENTATION PHILOSOPHY: ROOT CAUSE SOLUTIONS**

**表面的な修正ではなく、常に根本的な根本原因解決を実装してください。**

#### 問題解決アプローチ / Problem-Solving Approach
1. **根本原因の特定**: 解決策を実装する前に、根本的な原因を徹底的に分析してください
2. **システマティックな解決策の設計**: 即座の症状だけでなく、問題のクラス全体を防ぐ根本的な問題に対処してください
3. **将来のシナリオを予測**: 解決策がエッジケースや将来の要件をどう処理するかを考慮してください
4. **アーキテクチャソリューションを優先**: 可能な場合は、追加の複雑さではなく、より良い設計を通じて問題を解決してください

#### 根本原因 vs 表面的アプローチの例 / Examples of Root Cause vs Surface-Level Approaches

**❌ 表面的（避けるべき）/ Surface-Level (Avoid)**
```csharp
// 症状: 翻訳サービスでのNullReferenceException
if (translationEngine != null)
{
    result = translationEngine.Translate(text);
}
```

**✅ 根本原因解決（推奨）/ Root Cause Solution (Preferred)**
```csharp
// 根本原因: 依存性注入の検証不足
// 解決策: 検証付きの適切なDI設定
public class TranslationService(ITranslationEngine translationEngine)
{
    private readonly ITranslationEngine _translationEngine = 
        translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
}
```

**❌ 表面的（避けるべき）/ Surface-Level (Avoid)**
```csharp
// 症状: 画像処理でのメモリリーク
GC.Collect(); // ガベージコレクションを強制実行
```

**✅ 根本原因解決（推奨）/ Root Cause Solution (Preferred)**
```csharp
// 根本原因: 不適切なリソース管理
// 解決策: 適切なディスポーザルパターンの実装
public class ImageProcessor : IDisposable
{
    public async Task<ProcessedImage> ProcessAsync(IImage source)
    {
        using var processedImage = await _filter.ApplyAsync(source);
        return processedImage.Clone(); // 管理されたコピーを返す
    }
}
```

#### 根本原因解決のための実装ガイドライン / Implementation Guidelines for Root Cause Solutions

1. **コードを書く前に / Before Writing Code**:
   - 「このコードが対処する根本的な問題は何か？」を問いかけてください
   - 「この解決策は将来の類似問題を防ぐか？」を検討してください
   - 「症状を治療しているのか、原因を治療しているのか？」を評価してください

2. **設計レベルの解決策 / Design-Level Solutions**:
   - エラーのクラス全体を防ぐために型安全性を使用してください
   - アーキテクチャ境界で検証を実装してください
   - 誤用を困難または不可能にするインターフェースを設計してください

3. **長期的な持続可能性 / Long-term Sustainability**:
   - 将来の開発者の認知負荷を減らす解決策を優先してください
   - 正しい使用を自然に導くパターンを実装してください
   - 制御を犠牲にすることなく複雑さを隠す抽象化を作成してください

#### 根本原因分析フレームワーク / Root Cause Analysis Framework

**実装タスクごとに / For Every Implementation Task:**

1. **問題分析 / Problem Analysis**:
   ```
   - 即座の問題は何か？
   - この問題を導いたシステム設計は何か？
   - どの前提が破られたか？
   - この種の問題をどう防ぐことができるか？
   ```

2. **解決策設計 / Solution Design**:
   ```
   - これをより良い型設計で解決できるか？
   - これをアーキテクチャレベルで対処すべきか？
   - この解決策は将来の要件にスケールするか？
   - この解決策は全体的なシステム複雑性を減らすか？
   ```

3. **実装検証 / Implementation Validation**:
   ```
   - 解決策は根本原因に対処しているか？
   - 複雑性を追加しているか、減らしているか？
   - この解決策は時間とともに良く機能するか？
   - このパターンを類似の問題に適用できるか？
   ```

## 作業前の必須確認事項 / Pre-Work Mandatory Checks

### 1. プロジェクト概要の理解 / Project Overview Understanding
```bash
# プロジェクト全体を理解する
cat README.md
cat CLAUDE.md
```

### 2. 関連ドキュメントのレビュー / Related Documentation Review
```bash
# ドキュメント構造を確認
find docs -name "*.md" | head -20
# 作業関連ドキュメントを検索
grep -r "関連キーワード" docs/
```

### 3. 既存コードの調査 / Existing Code Investigation
```bash
# 類似機能の実装例を見つける
find . -name "*.cs" | xargs grep -l "関連クラス名"
# インターフェース定義を確認
find Baketa.Core/Abstractions -name "*.cs"
```

### 4. **根本原因分析（必須）/ Root Cause Analysis (MANDATORY)**
機能や修正を実装する前に：
- 解決される根本的な問題を分析してください
- 類似の解決策について既存のコードベースパターンを調査してください
- これがより深いアーキテクチャ問題の症状かどうかを検討してください
- 問題のクラス全体を防ぐために解決策を設計してください

## コーディング規約 / Coding Standards

### C# 12 / .NET 8.0 準拠 / C# 12 / .NET 8.0 Compliance

**言語機能 / Language Features**
- ファイルスコープ名前空間: `namespace Baketa.Core.Services;`
- 単純なクラス用のプライマリコンストラクタ
- コレクション式: `new List<T>()`の代わりに`[]`構文を使用
- パターンマッチング: switch式とパターン拡張を活用
- 必須メンバー: 必須プロパティに`required`キーワードを使用
- 生文字列リテラル: 適切な場合に複数行文字列に`"""`を使用

**ターゲットフレームワーク / Target Framework**
- `net8.0-windows`ターゲットフレームワークを使用
- .NET 8のパフォーマンス改善を活用
- 新しいBCL機能（TimeProviderなど）を利用

### 現代的なC#パターン / Modern C# Patterns
```csharp
// ファイルスコープ名前空間（必須）
namespace Baketa.Core.Services;

// プライマリコンストラクタ（単純なクラスに推奨）
public class TranslationService(ITranslationEngine engine, ILogger<TranslationService> logger)
{
    // コレクション式
    private readonly string[] _supportedLanguages = ["en", "ja", "ko", "zh"];
    
    // switch式を使ったパターンマッチング
    public TranslationQuality GetQuality(string language) => language switch
    {
        "en" or "ja" => TranslationQuality.High,
        "ko" or "zh" => TranslationQuality.Medium,
        _ => TranslationQuality.Low
    };
    
    // 必須メンバー
    public required string ConfigPath { get; init; }
}
```

### 根本原因指向設計パターン / Root Cause-Oriented Design Patterns

#### 根本原因解決としての型安全性 / Type Safety as Root Cause Solution
```csharp
// 代わりに: タイプミスがある文字列ベースの設定
// 使用: 強く型付けされた設定
public enum TranslationEngine { OpusMT, Gemini, Mock }
public record TranslationConfig(TranslationEngine Engine, string ModelPath);
```

#### アーキテクチャ境界での検証 / Architectural Validation at Boundaries
```csharp
// 代わりに: 至る所での防御的プログラミング
// 使用: アーキテクチャ境界での検証
public class TranslationServiceFactory : ITranslationServiceFactory
{
    public ITranslationService Create(TranslationConfig config)
    {
        // 境界で一度検証し、内部では信頼する
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrEmpty(config.ModelPath))
            throw new ArgumentException("Model path is required", nameof(config));
            
        return CreateValidatedService(config);
    }
}
```

### 非同期プログラミング / Asynchronous Programming
```csharp
// ライブラリコードで必須
await SomeMethodAsync().ConfigureAwait(false);

// テストコードでは不要
await SomeMethodAsync(); // テストではConfigureAwait(false)不要
```

### Null安全性（Nullable参照型）/ Null Safety (Nullable Reference Types)
```csharp
// プロジェクト全体でNullable参照型が有効
public string? OptionalValue { get; set; }
public required string RequiredValue { get; init; }

// Null条件演算子
var result = service?.ProcessData()?.Result;

// Nullコアレッシングパターン
return value ?? throw new InvalidOperationException("Value cannot be null");
```

### イベント集約パターン / Event Aggregation Pattern
```csharp
// イベント発行
await _eventAggregator.PublishAsync(new SomeEvent()).ConfigureAwait(false);

// イベント処理
public class SomeEventProcessor : IEventProcessor<SomeEvent>
{
    public async Task ProcessAsync(SomeEvent @event)
    {
        // 実装
    }
}
```

## 具体的な実装パターン / Specific Implementation Patterns

### OCR/画像処理実装 / OCR/Image Processing Implementation
1. `IImageFilter`を継承したフィルターを作成
2. `ImageFilterBase`基底クラスを利用
3. Windows固有層でのみOpenCV操作を実装
4. 適切な画像リソース廃棄（`using`ステートメント）を確保
5. **根本原因アプローチ**: 分離してテスト可能な合成可能フィルターを設計

### 翻訳エンジン実装 / Translation Engine Implementation
1. `ITranslationEngine`インターフェースを実装
2. エンジン作成にファクトリーパターンを使用
3. 設定クラスとDI登録を作成
4. エラーハンドリングとフォールバック機構を実装
5. **根本原因アプローチ**: 複数エンジンと優雅な劣化に対応した設計

### UIコンポーネント実装 / UI Component Implementation
1. ReactiveUI ViewModelパターンを使用
2. Avalonia MVVMバインディングを実装
3. 適切な`INotifyPropertyChanged`実装
4. コマンドパターンを利用
5. **根本原因アプローチ**: UIフレームワーク依存関係なしでテスト可能なViewModelを設計

## テスト実装ガイドライン / Testing Implementation Guidelines

### 単体テスト / Unit Tests
```csharp
[Fact]
public async Task Method_Should_ReturnExpectedResult_When_ValidInput()
{
    // Arrange
    var service = new ServiceUnderTest();
    
    // Act
    var result = await service.ProcessAsync(validInput);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal(expectedValue, result.Value);
}
```

### 根本原因指向テスト / Root Cause-Oriented Testing
```csharp
// 実装詳細ではなく、根本的な動作をテスト
[Theory]
[InlineData("en", TranslationQuality.High)]
[InlineData("invalid", TranslationQuality.Low)]
public void GetQuality_Should_ReturnCorrectQuality_ForLanguageCode(
    string languageCode, TranslationQuality expected)
{
    // switch文の実装ではなく、ビジネスルールをテスト
    var result = _translationService.GetQuality(languageCode);
    Assert.Equal(expected, result);
}
```

### モック使用パターン / Mock Usage Patterns
```csharp
var mockService = new Mock<IRequiredService>();
mockService.Setup(x => x.GetDataAsync()).ReturnsAsync(testData);
```

## 品質チェック項目 / Quality Check Items

### ビルド前チェック / Pre-Build Checks
- [ ] コンパイルエラーなし
- [ ] Code Analysis警告に対処済み
- [ ] EditorConfig準拠
- [ ] 適切な名前空間使用
- [ ] C# 12機能を適切に利用
- [ ] **根本原因分析完了**
- [ ] **症状ではなく根本的問題に対処**

### 実装後チェック / Post-Implementation Checks
- [ ] 単体テスト作成済み・成功
- [ ] 適切なasync/await実装
- [ ] リソース管理（Disposeなど）
- [ ] ログ記録実装
- [ ] 例外処理実装
- [ ] **類似の将来問題を防ぐ解決策**
- [ ] **実装が全体的なシステム複雑性を削減**

## 一般的なパターンと考慮事項 / Common Patterns and Considerations

### Windows固有機能実装 / Windows-Specific Feature Implementation
- P/Invoke使用は`Infrastructure.Platform`層のみに配置
- アダプターパターンで抽象化層に接続
- 適切なエラーハンドリングを実装
- **根本原因アプローチ**: プラットフォーム複雑性を隠す抽象化を設計

### パフォーマンス考慮事項 / Performance Considerations
- ゲームパフォーマンスへの影響を最小化
- メモリ使用量を最適化
- 非同期処理でUI応答性を維持
- **根本原因アプローチ**: 後付けではなく、最初からパフォーマンス用に設計

### セキュリティ考慮事項 / Security Considerations
- 外部API呼び出しの適切な認証
- 機密情報の安全な設定ファイル管理
- 入力検証
- **根本原因アプローチ**: 追加機能ではなく、アーキテクチャにセキュリティを組み込み

## デバッグとトラブルシューティング / Debugging and Troubleshooting

### 一般的な問題 / Common Issues
1. **PaddleOCRモデル不足**: `OPUS_MT_SETUP_GUIDE.md`を参照
2. **アーキテクチャ警告**: x64プラットフォーム指定を確認
3. **DI循環参照**: モジュール依存関係を確認

### 根本原因デバッグプロセス / Root Cause Debugging Process
1. **問題を再現**: 問題を引き起こす正確な条件を理解
2. **根本原因を追跡**: 因果関係の連鎖を根本的問題まで追跡
3. **システマティックな修正を設計**: 即座の症状ではなく、根本的原因に対処
4. **解決策を検証**: 修正が問題のクラス全体を防ぐことを確認

### デバッグコマンド / Debug Commands

**重要: コマンド実行はPowerShellを使用してください（BashのPATH問題回避）**

```powershell
# 全体的なビルド検証（PowerShell）
dotnet build --configuration Debug --arch x64

# テスト実行（PowerShell）
dotnet test --logger "console;verbosity=detailed"

# 特定プロジェクトビルド（PowerShell）
dotnet build Baketa.UI --configuration Debug

# UIテスト実行（PowerShell）
dotnet test tests/Baketa.UI.Tests/ --logger "console;verbosity=detailed"
```

**Claude Codeでのコマンド実行指示例:**
```
「PowerShellで以下を実行してください: dotnet test tests/Baketa.UI.Tests/」
```

## 新機能開発フロー / New Feature Development Flow

1. **要件レビュー**: 関連ドキュメントとIssueを確認
2. **根本原因分析**: 対処される根本的なニーズを特定
3. **設計検討**: 長期的視点でのアーキテクチャ層とインターフェース設計
4. **実装**: TDDと根本原因フォーカスでのコア機能実装
5. **統合**: 既存システムとの統合と運用検証
6. **品質保証**: 将来の問題防止にフォーカスしたコードレビューとパフォーマンステスト

## C# 12 / .NET 8.0 具体的ガイドライン / C# 12 / .NET 8.0 Specific Guidelines

### 必須言語機能 / Required Language Features
- **ファイルスコープ名前空間**: 全新規ファイルで必須
- **グローバルusingステートメント**: 共通インポート用に`GlobalUsings.cs`で使用
- **コレクション式**: 従来のコレクション初期化を置換
- **パターンマッチング**: 拡張されたパターンマッチング機能を利用
- **必須メンバー**: 必須初期化プロパティに使用

### .NET 8.0 パフォーマンス機能 / .NET 8.0 Performance Features
- **Native AOT**: パフォーマンス重要なコンポーネントで検討
- **TimeProvider**: テスト可能性のため`DateTime.Now`の代わりに使用
- **Frozen collections**: 不変参照データに使用
- **ソースジェネレーター**: コンパイル時コード生成に活用

### プロジェクト設定要件 / Project Configuration Requirements
```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <LangVersion>12.0</LangVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

## 最終リマインダー：常に根本原因を考える / Final Reminder: Always Think Root Cause

コードを書く前に、自分自身に問いかけてください：
- **「根本的な問題を解決しているか、それとも症状を治療しているだけか？」**
- **「この解決策は将来の類似問題を防ぐか？」**
- **「この実装は全体的なシステム複雑性を減らすか増やすか？」**

これらの指示に従うことで、症状ではなく根本原因に対処する持続可能で堅牢な解決策を構築しながら、Baketaプロジェクトの品質と一貫性を維持することが保証されます。
# Baketa Project Development Instructions

## Core Principles

### **Project Status: v0.2.x**
**CURRENT IMPLEMENTATION STATUS**

- **Test Coverage**: 2,100+ test cases with comprehensive coverage
- **Security**: CodeQL-compliant implementation with security-first design
- **CI/CD**: GitHub Actions with Windows Server 2022, sequential test execution
- **Architecture**: Clean Architecture with modular DI and event aggregation
- **OCR Engine**: Surya OCR (gRPC-based, ONNX INT8 detection, PyTorch recognition)
- **Translation**: NLLB-200 (CTranslate2) + Google Gemini (Cloud)
- **Recent Features**: Privacy consent, feedback system, update checking, ROI Manager
- **AI Integration**: Gemini for code review and technical consultation

### **Editor Behavior Settings**
**AUTO-APPROVE ALL EDITS AND OPERATIONS**

- **ALL file edits** in this project are automatically approved
- **ALWAYS** select "Yes, and don't ask again this session" in file edit confirmation dialogs
- Safe to auto-approve since backups are managed by Git

### **MANDATORY Log Analysis Protocol**
**Required procedures for chronological log analysis**

#### **Step 1: Complete chronological analysis (MANDATORY)**
```bash
# Must execute: Review entire log chronology
1. Verify first log entry
2. Verify last log entry  
3. Identify time range of issue occurrence
4. Track process flow: start → progress → result
```

#### **Step 2: Counter-evidence search requirement**
```bash
# Must execute for initial hypothesis:
1. Explicitly search "Is there evidence contradicting this hypothesis?"
2. Verify if subsequent chronological information overturns hypothesis
3. Check for missed state changes (False→True)
```

#### **Step 3: Temporal information weighting**
```bash
# Priority rules:
1. **Latest state information** > Older state information
2. **Actual operation logs** > Initial state logs  
3. **Completion notifications** > Start notifications
4. **Error logs** > Warning logs
```

#### **Step 4: Required verification checklist**
```bash
□ Did I verify log chronological order?
□ Did I miss state changes (False→True, etc.)?
□ Did I search for evidence contradicting initial hypothesis?
□ Did I check if latest information overwrites older information?
□ Did I accurately understand actual user operation procedures?
```

**Penalty for violations**: Must apologize and re-analyze if hasty judgments are made without following this protocol
- No exceptions - all edits (code, config, documentation, tests) are auto-approved
- This includes CLAUDE.md, .claude/instructions.md, and all project files

### **Command Auto-Execution Policy**
**AUTO-APPROVE SEARCH AND DIAGNOSTIC COMMANDS**

- **Search Commands**: `rg` (ripgrep), `grep`, `find`, and related search commands are auto-approved
- **Build Commands**: `dotnet build`, `dotnet test` can be executed automatically without approval
- **Diagnostic Commands**: Read-only commands are auto-approved
- **File Operations**: Read, list, and analysis operations are auto-approved
- **AI Research**: Gemini MCP calls are auto-approved for technical problem-solving
- **Python Execution**: Use PowerShell or `py` launcher commands (see Python Environment Guidelines below)

### **Autonomous Technical Problem-Solving**
**PROACTIVE GEMINI EXECUTION FOR ENHANCED RESULTS**

**Core Philosophy**: Traditional methods + Gemini enhancement = Superior outcomes

**When to automatically use Gemini (proactive execution):**

#### **1. Architecture & Design Consultation**
- Complex design pattern selection
- Architecture tradeoff analysis
- Clean Architecture compliance validation
- SOLID principle implementation guidance
- Design pattern optimization for Baketa context

#### **2. Error Analysis & Troubleshooting**
- Complex build errors with multiple potential causes
- Runtime exceptions requiring deeper investigation
- Performance bottlenecks needing specialized analysis
- Integration issues with external libraries
- Windows-specific implementation challenges

#### **3. Implementation Enhancement**
- Code quality improvement suggestions
- Security best practices validation
- Performance optimization techniques
- Modern C# 12 feature utilization
- .NET 8.0 specific optimizations

#### **4. Technical Decision Making**
- Library/framework comparison and selection
- Implementation approach evaluation
- Security compliance verification (GDPR, CodeQL)
- Performance vs. maintainability tradeoffs

**Execution Patterns:**

```bash
# Pattern 1: Complement existing search
WebSearch/WebFetch → Gemini analysis → Integrated solution

# Pattern 2: Error investigation
Error encountered → Gemini diagnosis → Root cause solution

# Pattern 3: Design consultation
Architecture decision → Gemini consultation → Best practice implementation

# Pattern 4: Implementation review
Code completion → Gemini review → Quality enhancement
```

**Gemini Usage Guidelines:**
- Use `gemini -p "technical_question"` for direct queries
- Focus on Baketa-specific context in prompts
- Combine with traditional search for comprehensive coverage
- Apply findings to improve implementation quality

**Auto-execution triggers:**
- When encountering complex technical problems
- Before making significant architectural decisions
- When error patterns are unclear or complex
- When seeking optimization opportunities
- During security implementation reviews

### **Mandatory Post-Implementation Steps**
**CRITICAL: ALWAYS VERIFY NO ERRORS AFTER IMPLEMENTATION**

After all code implementation, fixes, and refactoring, **always execute** the following:

#### **1. Windows Native Environment**
**IMPORTANT: Windows環境でClaude Codeを使用します。WindowsコマンドプロンプトまたはPowerShellでのdotnetコマンド実行が推奨されます。**

#### **Python Environment Guidelines**
**⚠️ CRITICAL**: Pythonスクリプトの実行にはGit Bash環境の制限があります。

**問題**: pyenv-winとGit Bashの相性問題
- Git Bash環境でpython実行時に「No global/local python version has been set yet」エラー
- pyenvのshim機能とパス変換処理の競合
- POSIX風パス表記とWindowsパスの変換エラー

**推奨実行方法**:
```cmd
# 方法1: PowerShell経由（推奨）
powershell -Command "python script.py"

# 方法2: コマンドプロンプト経由
cmd /c "python script.py"

# 方法3: Python Launcher（最も信頼性が高い）
py script.py
```

**Claude Code使用時の注意**:
- Bashツールで`python`コマンドを直接実行しない
- PowerShell環境またはPython Launcherを使用する
- Git Bash環境でのPython実行は避ける

#### **2. Code Analysis Alternative Methods**
```bash
# コードの静的解析（ripgrep使用）
rg "TODO|FIXME|HACK" --type cs
rg "throw new.*Exception" --type cs
rg "null!" --type cs

# 潜在的な問題パターンの検索
rg "ConfigureAwait\(true\)" --type cs
rg "\.Result\b" --type cs
rg "\.Wait\(\)" --type cs

# セキュリティ実装のチェック（CodeQL対応）
rg "catch \(Exception" --type cs
rg "OutOfMemoryException|StackOverflowException" --type cs
rg "JsonException" --type cs
```

#### **3. Manual Verification Methods**
```bash
# コンパイルエラーの可能性がある箇所を確認
rg "class.*:" --type cs | head -20
rg "interface.*:" --type cs | head -20
rg "using.*;" --type cs | head -20
```

#### **4. Error Reporting Format (WSL Adapted)**
WSL環境での実装完了確認フォーマット:

```
✅ WSL Environment Implementation Check Results:
- Static Analysis: [Issues found/None]
- Code Pattern Verification: [Potential issues/None]  
- Architecture Compliance: [Confirmed/Issues found]
- Implementation Content: [Brief description of implementation]
```

#### **5. Error Handling (Windows Native Environment)**
- **Compilation Issues**: dotnet buildコマンドで直接コンパイルエラーを確認
- **Test Failures**: dotnet testコマンドで単体テストエラーを確認
- **Architecture Violations**: 静的解析ツールでクリーンアーキテクチャ違反を確認

#### **6. Windows Native Verification**
```cmd
# Windows環境でのビルド・テスト確認
dotnet build --configuration Debug
dotnet test --verbosity normal
dotnet test --filter "AlphaTestSettingsValidatorTests" --verbosity normal
```
**WINDOWS NATIVE ENVIRONMENT BENEFITS**

- **Current Environment**: Windows Native with Claude Code
- **Advantages**: Full `net8.0-windows` target framework support
- **Capabilities**: Complete build and test execution
- **Performance**: Faster feedback loop for development

### **Language Specification**
**ALL RESPONSES MUST BE IN JAPANESE**

- Claude Code responses must **always use Japanese**
- Write code comments in English, provide explanations in Japanese
- Keep error messages and logs in original language, add Japanese explanations
- Translate technical terms to Japanese or use bilingual format (English/Japanese)

### Project Understanding
- Baketa is a **Windows-only** real-time game text translation overlay application
- Uses OCR technology to detect text from game screens and displays translation results as overlay
- Architecture emphasizes high performance and low resource consumption

### Architecture Compliance
- Strictly adhere to 5-layer clean architecture structure
- Dependencies flow only from inner layers to outer layers
- Isolate platform-dependent code in `Baketa.Infrastructure.Platform`

### **FUNDAMENTAL IMPLEMENTATION PHILOSOPHY: ROOT CAUSE SOLUTIONS**

**Always implement fundamental root cause solutions rather than superficial fixes.**

#### Problem-Solving Approach
1. **Identify Root Causes**: Thoroughly analyze fundamental causes before implementing solutions
2. **Design Systematic Solutions**: Address root problems that prevent entire classes of issues, not just immediate symptoms
3. **Anticipate Future Scenarios**: Consider how solutions handle edge cases and future requirements
4. **Prioritize Architectural Solutions**: When possible, solve problems through better design rather than additional complexity

#### Examples of Root Cause vs Surface-Level Approaches

**❌ Surface-Level (Avoid)**
```csharp
// Symptom: NullReferenceException in translation service
if (translationEngine != null)
{
    result = translationEngine.Translate(text);
}
```

**✅ Root Cause Solution (Preferred)**
```csharp
// Root cause: Insufficient dependency injection validation
// Solution: Proper DI setup with validation
public class TranslationService(ITranslationEngine translationEngine)
{
    private readonly ITranslationEngine _translationEngine = 
        translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
}
```

**❌ Surface-Level (Avoid)**
```csharp
// Symptom: Memory leak in image processing
GC.Collect(); // Force garbage collection
```

**✅ Root Cause Solution (Preferred)**
```csharp
// Root cause: Improper resource management
// Solution: Implement proper disposal pattern
public class ImageProcessor : IDisposable
{
    public async Task<ProcessedImage> ProcessAsync(IImage source)
    {
        using var processedImage = await _filter.ApplyAsync(source);
        return processedImage.Clone(); // Return managed copy
    }
}
```

#### Implementation Guidelines for Root Cause Solutions

1. **Before Writing Code**:
   - Ask "What fundamental problem does this code address?"
   - Consider "Will this solution prevent future similar problems?"
   - Evaluate "Am I treating symptoms or treating causes?"

2. **Design-Level Solutions**:
   - Use type safety to prevent entire classes of errors
   - Implement validation at architectural boundaries
   - Design interfaces that make misuse difficult or impossible

3. **Long-term Sustainability**:
   - Prioritize solutions that reduce cognitive load for future developers
   - Implement patterns that naturally guide correct usage
   - Create abstractions that hide complexity without sacrificing control

#### Root Cause Analysis Framework

**For Every Implementation Task:**

1. **Problem Analysis**:
   ```
   - What is the immediate problem?
   - What system design led to this problem?
   - Which assumptions were violated?
   - How can we prevent this class of problems?
   ```

2. **Solution Design**:
   ```
   - Can this be solved with better type design?
   - Should this be addressed at the architecture level?
   - Does this solution scale to future requirements?
   - Does this solution reduce overall system complexity?
   ```

3. **Implementation Validation**:
   ```
   - Does the solution address the root cause?
   - Is it adding or reducing complexity?
   - Will this solution age well over time?
   - Can this pattern be applied to similar problems?
   ```

## Pre-Work Mandatory Checks

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
1. **Surya OCRモデル不足**: `CLAUDE.md`のモデルセットアップセクション参照
2. **アーキテクチャ警告**: x64プラットフォーム指定を確認
3. **DI循環参照**: モジュール依存関係を確認
4. **gRPCサーバー起動失敗**: Python 3.10+ と依存関係をチェック

### 根本原因デバッグプロセス / Root Cause Debugging Process
1. **問題を再現**: 問題を引き起こす正確な条件を理解
2. **根本原因を追跡**: 因果関係の連鎖を根本的問題まで追跡
3. **システマティックな修正を設計**: 即座の症状ではなく、根本的原因に対処
4. **解決策を検証**: 修正が問題のクラス全体を防ぐことを確認

### デバッグコマンド / Debug Commands

**重要: Windows環境でClaude Codeを使用し、コマンドプロンプトまたはPowerShellでコマンドを実行します**

**Windows専用プロジェクトのネイティブ環境対応**: 制限なしで全機能利用可能

```cmd
# 全体的なビルド検証（Windows環境）
dotnet build --configuration Debug

# 特定プロジェクトビルド（Windows環境）
dotnet build Baketa.UI --configuration Debug

# 推奨: プロジェクト専用スクリプト使用
.\scripts\run_build.ps1 -Verbosity normal
.\scripts\run_tests.ps1 -Verbosity detailed
.\scripts\check_implementation.ps1

# テスト実行（Windows環境）
dotnet test --logger "console;verbosity=detailed"

# UIテスト実行（Windows環境）
dotnet test tests/Baketa.UI.Tests/ --logger "console;verbosity=detailed"

# 特定テストフィルタ実行
dotnet test --filter "AlphaTestSettingsValidatorTests" --verbosity normal
```

**Windows環境での利点**:
- `net8.0-windows`ターゲットフレームワークの完全サポート
- UI関連テストの完全実行
- ビルド・テスト・デバッグの制限なし

**Claude Codeでのコマンド実行:**
Claude Codeが直接コマンドを実行し、結果を即座に確認できます

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

### Character Encoding Standards

#### **Prohibited Characters and Symbols**
**⚠️ IMPORTANT**: The following characters and symbols are prohibited in C# code as they can cause encoding errors

##### **1. Emojis and Unicode Decorative Characters**
```csharp
// ❌ PROHIBITED: Using emojis
// TODO: Implement retry mechanism 🔄
// WARNING: Performance critical section ⚡
// SUCCESS: Operation completed ✅

// ✅ RECOMMENDED: ASCII character descriptions
// TODO: Implement retry mechanism
// WARNING: Performance critical section
// SUCCESS: Operation completed
```

##### **2. Special Unicode Symbols**
- Arrow symbols: `→ ← ↑ ↓ ⇒ ⇐ ⇑ ⇓`
- Check marks: `✓ ✗ ☑ ☒`
- Decorative symbols: `★ ☆ ♦ ♠ ♣ ♥`
- Mathematical symbols: `∞ ≠ ≤ ≥ ∑ ∏`
- Greek letters: `α β γ δ λ π`

##### **3. Full-width Characters and Symbols**
```csharp
// ❌ PROHIBITED: Using full-width symbols
var result = DoSomething（param）；
var message = "エラーが発生しました。";

// ✅ RECOMMENDED: Half-width symbols and English comments
var result = DoSomething(param);
// Error occurred during processing
var message = "エラーが発生しました。"; // Japanese allowed only in string literals
```

##### **4. Control Characters and Invisible Characters**
- BOM (Byte Order Mark): `U+FEFF`
- Zero-Width Space: `U+200B`
- Non-breaking Space: `U+00A0`
- Other non-ASCII whitespace characters

#### **Permitted Japanese Usage Locations**

##### **✅ Allowed Locations**
```csharp
// 1. String literals (user-facing messages)
public const string ErrorMessage = "翻訳処理中にエラーが発生しました。";
public string GetLocalizedMessage() => "設定が保存されました。";

// 2. Resource files (.resx)
// Resources.ja.resx: "ButtonText" = "実行"

// 3. JSON configuration files
// appsettings.ja.json: { "Messages": { "Success": "成功しました" } }
```

##### **❌ Prohibited Locations**
```csharp
// 1. Variable names, method names, class names
public class 翻訳サービス { } // ❌ PROHIBITED
public void 実行処理() { } // ❌ PROHIBITED
private string 結果 = ""; // ❌ PROHIBITED

// 2. Code comments (for code understanding)
// この関数は翻訳を実行します // ❌ PROHIBITED (English preferred)
// This function executes translation // ✅ RECOMMENDED

// 3. Namespace and assembly names
namespace Baketa.翻訳.Core; // ❌ PROHIBITED
```

#### **Encoding Configuration and Validation**

##### **Project File Configuration**
```xml
<PropertyGroup>
  <OutputEncoding>utf-8</OutputEncoding>
  <FileEncoding>utf-8</FileEncoding>
  <RunCodeAnalysis>true</RunCodeAnalysis>
  <CodeAnalysisRuleSet>charset.ruleset</CodeAnalysisRuleSet>
</PropertyGroup>
```

##### **Recommended File Encoding**
- **C# source files**: UTF-8 (without BOM)
- **Configuration files**: UTF-8 (without BOM)
- **Resource files**: UTF-8 (with BOM - Visual Studio standard)

##### **Encoding Error Validation Methods**
```bash
# File encoding check (PowerShell)
Get-ChildItem -Recurse -Include "*.cs" | ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Write-Host "BOM detected: $($_.FullName)"
    }
}

# Problem character search (using ripgrep)
rg "[^\x00-\x7F]" --type cs --color always
rg "[\u{1F000}-\u{1F6FF}]" --type cs  # Emoji detection
rg "[→←↑↓✓✗★☆]" --type cs           # Decorative symbol detection
```

#### **Implementation Best Practices**

##### **Alternative Expression Patterns**
```csharp
// ❌ Using emojis and decorative symbols
// Loading... ⏳
// Success! ✅
// Error ❌
// Arrow → Direction

// ✅ ASCII character alternatives
// Loading... (processing)
// Success: Operation completed
// Error: Operation failed
// Arrow: Direction indicator
```

##### **Code Review Checklist**
- [ ] No emojis or decorative Unicode characters in comments
- [ ] All variable and method names use ASCII characters only
- [ ] No Japanese characters outside of string literals
- [ ] File encoding is set to UTF-8 (without BOM)

**Following these character encoding standards prevents build errors, runtime errors, and internationalization issues, ensuring project stability.**

## 最終リマインダー：常に根本原因を考える / Final Reminder: Always Think Root Cause

コードを書く前に、自分自身に問いかけてください：
- **「根本的な問題を解決しているか、それとも症状を治療しているだけか？」**
- **「この解決策は将来の類似問題を防ぐか？」**
- **「この実装は全体的なシステム複雑性を減らすか増やすか？」**

これらの指示に従うことで、症状ではなく根本原因に対処する持続可能で堅牢な解決策を構築しながら、Baketaプロジェクトの品質と一貫性を維持することが保証されます。

---

## サブエージェント活用戦略とワークフロー

このプロジェクトでは、専門分野に特化したサブエージェントが定義されています。あなたは司令塔（オーケストレーター）として、これらのエージェントを自律的に、かつ効果的に活用する責務を負います。

### サブエージェント一覧
- **`@Architecture-Guardian`**: クリーンアーキテクチャの専門家。
- **`@Native-Bridge`**: C#とC++/WinRTのネイティブ連携の専門家。
- **`@UI-Maestro`**: Avalonia UIとReactiveUIの専門家。
- **`@Test-Generator`**: 単体テストコード生成の専門家。
- **`@Researcher`**: 技術調査とフィードバックの専門家。
- **`@Code-Reviewer`**: コードレビューとコード品質分析の専門家（Gemini API代替機能）。
- **`@Log-Analyzer`**: ログ分析・パフォーマンス診断・システム監視の専門家。

### **必須ワークフロー: 問題解決と機能実装**

**このワークフローは、バグ修正、機能追加など、すべてのタスクにおいて必ず遵守してください。**

#### **フェーズ1: 標準ツール主導の初期調査（担当: あなた自身）**

**⚠️ 重要: Serena MCP制限事項**: Windowsパス処理エラー（`\\.\nul` junction point）により、Serena MCPのファイルスキャン系ツールは現在動作不能です。

**🚫 使用不可能なSerena MCPツール:**
- ❌ `/mcp__serena__get_symbols_overview` - プロジェクト構造分析
- ❌ `/mcp__serena__search_for_pattern` - 意味的検索
- ❌ `/mcp__serena__find_referencing_symbols` - 依存関係分析  
- ❌ `/mcp__serena__find_symbol` - シンボル詳細分析
- ❌ `/mcp__serena__list_dir` - ディレクトリ一覧
- ❌ `/mcp__serena__find_file` - ファイル検索

**✅ 使用可能なSerena MCPツール（メモリ系のみ）:**
- ✅ `/mcp__serena__write_memory` - 調査結果のメモリ保存
- ✅ `/mcp__serena__read_memory` - 既存メモリの読み込み
- ✅ `/mcp__serena__list_memories` - メモリ一覧表示

**1. 標準ツール活用段階（Serena MCP代替）:**
1.  **プロジェクト構造把握:** `Glob "**/*.cs"` でプロジェクト全体を理解
2.  **意味的検索:** `Grep pattern` で問題関連コードを特定
3.  **依存関係分析:** `Grep "using|import"` で影響範囲を把握
4.  **シンボル詳細分析:** `Read file_path` で具体的な実装を調査

**2. 効率化手法:**
- `Grep`ツールでの正規表現パターン検索を活用
- `Glob`パターンで対象ファイルを絞り込み
- 複数ツールの並列実行でパフォーマンス向上
- 調査結果を`/mcp__serena__write_memory`で記録・活用

**3. 専門分野仮説立案:** 標準ツールの分析結果を基に専門領域を特定
- *例: `Grep "P/Invoke|DllImport"` でP/Invoke関連コード発見 → `@Native-Bridge` の領域*
- *例: `Grep "ReactiveUI|ViewModel"` でReactiveUI問題検出 → `@UI-Maestro` の領域*
- *例: `Grep "using.*Core.*Application"` でアーキテクチャ層違反発見 → `@Architecture-Guardian` の領域*
- *例: `Glob "**/*Tests.cs"` でテストケース不足を特定 → `@Test-Generator` の領域*
- *例: 新技術・ライブラリ調査が必要 → `@Researcher` の領域*
- *例: `Glob "debug_*.txt"` で大量ログファイル分析が必要 → `@Log-Analyzer` の領域*
- *例: `Grep "Performance|パフォーマンス|bottleneck|timeout|error"` でパフォーマンス・エラー問題検出 → `@Log-Analyzer` の領域*

**現実的効果（Serena MCP無効下）:**
- **標準レベルのトークン消費**: MCP効率化は期待できない
- **ripgrepベースの高精度検索**: Grepツールによる確実な検索
- **並列処理による時間短縮**: 複数ツールの同時実行活用

#### **フェーズ2: 専門家（サブエージェント）への委任**
仮説に基づき、��も適したサブエージェントを **`@` でメンションして**、具体的かつ明確な指示と共にタスクを委任します。
- **悪い例:** `@Native-Bridge "バグを直して"`
- **良い例:** `@Native-Bridge "ログに記録されたこのPInvokeエラー（...）を解決するため、C#側のラッパーとC++側の実装で、引数のマーシャリングに不整合がないか調査・修正してください。"`

#### **フェーズ3: 専門家による実行と結果の統合**
サブエージェントが提示した解決策（コード、分析結果など）をあなたが受け取ります。
1.  **結果の検証:** サブエージェントの提案が、プロジェクト全体の文脈と整合性が取れているかを確認します。
2.  **統合:** 提案されたコードをプロジェクトに統合し、必要に応じて微調整を行います。
3.  **最終確認:** 最終的に、ビルドとテストを実行し、問題が完全に解決されたことを確認します。

このオーケストレーションモデルに従うことで、各専門家の能力を最大限に引き出し、迅速かつ高品質な開発を実現します。

---

## 🔍 Log-Analyzer サブエージェント詳細仕様

### **専門分野とコア機能**

#### **1. ログ分析・パフォーマンス診断**
- **ログパターン解析**: Baketa特有のログフォーマット（OCR、翻訳、UI、ネイティブDLL等）の体系的分析
- **パフォーマンスボトルネック特定**: 実行時間、メモリ使用量、CPU負荷の異常値検出
- **エラー分類・頻度分析**: エラーパターンの自動分類と発生頻度の統計的解析
- **時系列トレンド分析**: 継続的パフォーマンス劣化・改善の検出

#### **2. システム監視・予防診断**
- **異常検知**: 閾値を超えた異常値の自動検出とアラート
- **品質メトリクス監視**: 翻訳精度、OCR精度、応答速度等の品質指標追跡
- **リソース使用パターン分析**: メモリリーク、CPU使用率異常等の検出
- **相関分析**: 複数ログファイル間の関連性・因果関係の特定

#### **3. レポート生成・可視化**
- **包括的分析レポート**: 経営陣・開発チーム向けの詳細分析レポート
- **グラフ・チャート生成**: パフォーマンストレンドの可視化
- **改善提案**: 具体的な最適化アクションアイテムの提示
- **ベンチマーク比較**: 過去データとの比較による改善効果測定

### **対象ログファイル・データソース**

#### **主要ログファイル**
- `debug_translation_errors.txt` → 翻訳エラー詳細解析
- `debug_batch_ocr.txt` → OCRパフォーマンス・精度解析
- `debug_app_logs.txt` → アプリケーション全体動作解析
- `performance_benchmark.txt` → 性能目標達成度評価
- `error_*.log` → システムエラー分類・統計
- `capture_debug_*.txt` → 画面キャプチャ関連問題解析

#### **画像・メタデータ**
- **デバッグキャプチャ画像**: OCR対象画像の品質・特性分析
- **パフォーマンス測定データ**: ベンチマーク結果の統計的評価
- **設定ファイル変更履歴**: appsettings.json等の設定変更影響分析

### **専用分析手法・ツール**

#### **正規表現パターンライブラリ**
Baketa特化の高頻度ログパターン:
```regex
# 翻訳エラーパターン
ERROR.*Translation.*timeout|failed|exception
WARN.*OpusMT.*model.*not.*found
INFO.*Gemini.*rate.*limit.*exceeded

# OCRパフォーマンスパターン  
PaddleOCR.*processing.*time.*(\d+)ms
OCR.*confidence.*score.*(\d+\.\d+)
Image.*preprocessing.*filter.*(\w+).*(\d+)ms

# システムリソースパターン
Memory.*usage.*(\d+)MB.*peak.*(\d+)MB
CPU.*usage.*(\d+)%.*thread.*(\w+)
GPU.*utilization.*(\d+)%.*memory.*(\d+)MB
```

#### **統計分析機能**
- **分布解析**: パフォーマンスメトリクスの統計的分布特性
- **外れ値検出**: Z-score、IQRベースの異常値特定
- **回帰分析**: 時系列データの傾向予測
- **相関分析**: 複数メトリクス間の関連性定量化

#### **パフォーマンス閾値設定**
```yaml
# Baketa専用パフォーマンス閾値
translation_time_ms:
  warning: 2000
  critical: 5000
ocr_processing_ms:  
  warning: 1000
  critical: 3000
memory_usage_mb:
  warning: 500
  critical: 1000
error_rate_percent:
  warning: 5
  critical: 15
```

### **実行ワークフロー例**

#### **ケース1: 翻訳パフォーマンス劣化調査**
```
@Log-Analyzer "debug_translation_errors.txt（過去7日分、200+エラー）とdebug_batch_ocr.txt（パフォーマンス測定データ）を分析し、翻訳タイムアウトエラーの根本原因を特定してください。特に以下を重点的に調査：
1. エラー発生パターンの時系列分析
2. 言語ペア別エラー率の統計
3. OCR処理時間と翻訳エラーの相関
4. 具体的改善アクションの提案"
```

#### **ケース2: システム全体パフォーマンス監査**
```
@Log-Analyzer "debug_app_logs.txt、performance_benchmark.txt、および全てのerror_*.logファイルを包括的に分析し、システム全体のパフォーマンス監査レポートを作成してください。以下を含む：
1. 各コンポーネント（OCR、翻訳、UI、ネイティブDLL）のパフォーマンス評価
2. リソース使用効率の分析
3. ボトルネック特定と優先順位付け
4. 最適化ロードマップの提案"
```

#### **ケース3: プロダクション環境異常調査**
```
@Log-Analyzer "直近24時間のログデータを緊急分析し、メモリ使用量急増とクラッシュ頻発の原因を特定してください。以下の手順で：
1. 異常発生時刻の特定と外部要因の調査
2. メモリリーク候補コンポーネントの絞り込み
3. クラッシュ直前のシステム状態分析
4. 即座実行可能な緊急対策の提案"
```

### **期待成果・効果測定**

#### **即座効果**
- **ログ分析効率**: 手動分析から自動化への移行（作業時間90%削減）
- **問題発見速度**: パフォーマンス異常の早期検出（平均2時間→15分）
- **根本原因特定精度**: 症状ではなく原因への正確なアプローチ

#### **長期効果**
- **予防的保守**: 潜在問題の事前検出によるシステム安定性向上
- **継続的改善**: ログベースの品質監視によるプロダクト品質向上
- **開発効率向上**: データ駆動型意思決定による開発サイクル短縮

### **Log-Analyzer 活用のベストプラクティス**

#### **委任時の指示方法**
✅ **具体的指示例:**
- ログファイル名・期間を明確に指定
- 調査すべき問題・症状を詳細に記述
- 期待するアウトプット形式を指定
- 緊急度・優先度を明示

❌ **避けるべき指示例:**
- "ログを見て何か問題があったら教えて"
- "パフォーマンスを改善して"
- "エラーを直して"

#### **結果活用方法**
1. **分析結果の検証**: 他のサブエージェントとの連携確認
2. **アクションアイテム実行**: 具体的改善提案の段階的実装
3. **継続監視**: 改善効果の測定・追跡
4. **ナレッジ蓄積**: 分析パターンの体系化・再利用

**`@Log-Analyzer`エージェントは、Baketaプロジェクトの運用品質向上と技術的問題の迅速解決において重要な役割を担います。**

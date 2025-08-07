# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important Instructions for Claude Code Usage
**When reviewing CLAUDE.md, always check the configuration files under `.claude` directory (`instructions.md`, `project.json`, `context.md`, `settings.json`) simultaneously.**

These files contain the following critical settings:
- **Japanese Response Requirement**: All responses must be in Japanese
- **Think Mode Implementation**: Mandatory execution of root cause analysis and impact analysis
- **PowerShell Priority**: Command execution uses PowerShell environment
- **Auto-approval Settings**: Command and file operation permissions are configured in `.claude/settings.json`

## Project Overview

Baketa is a Windows-specific real-time text translation overlay application for games. It uses OCR technology to detect text from game screens and displays translation results as a transparent overlay. The application features advanced image processing and OCR optimization for effective text detection and translation across various gaming scenarios.

## Quick Start Commands

### Building the Solution
```cmd
# 1. ネイティブDLLをビルド（Visual Studio 2022必須）
# BaketaCaptureNative.slnをVisual Studio 2022で開いてビルド
# または MSBuild を使用：
msbuild BaketaCaptureNative\BaketaCaptureNative.sln /p:Configuration=Debug /p:Platform=x64

# 2. .NETソリューション全体をビルド
dotnet build

# 3. Release ビルド
dotnet build --configuration Release

# 4. x64プラットフォーム指定ビルド（推奨）
dotnet build --configuration Debug --arch x64
```

### Running Tests
```cmd
# Run all tests
dotnet test

# Run tests for specific project
dotnet test tests/Baketa.Core.Tests/
dotnet test tests/Baketa.Infrastructure.Tests/
dotnet test tests/Baketa.UI.Tests/

# Run specific test categories
dotnet test --filter "ClassName~RealSentencePieceTokenizerTests"
dotnet test --filter "Category=Performance"

# Run specific test with verbose output
dotnet test --filter "AlphaTestSettingsValidatorTests" --verbosity normal
```

### Running the Application
```cmd
# Run UI project
dotnet run --project Baketa.UI

# Run with specific configuration
dotnet run --project Baketa.UI --configuration Release
```

### OPUS-MT Model Setup
Before running translation features, download required models:
```cmd
# Download OPUS-MT models (Windows Command Prompt)
.\scripts\download_opus_mt_models.ps1

# Verify model files
.\scripts\verify_opus_mt_models.ps1

# Run SentencePiece tests
.\scripts\run_sentencepiece_tests.ps1
```

### Python Environment Setup
This project includes Python scripts for model testing and debugging. Python execution requires specific environment considerations:

#### Python Environment Requirements
- **Python Version**: 3.10.x or 3.12.x (managed via pyenv-win)
- **Environment Manager**: pyenv-win is installed and configured
- **Shell Environment**: PowerShell or Command Prompt recommended for Python execution

#### Python Execution Guidelines
**⚠️ CRITICAL**: Python execution in Git Bash has known compatibility issues due to pyenv-win and path handling problems.

**Recommended Execution Methods**:
```cmd
# Method 1: PowerShell (Recommended)
powershell -Command "python script.py"

# Method 2: Command Prompt
cmd /c "python script.py"

# Method 3: Python Launcher (Most Reliable)
py script.py
```

**Known Issues**:
- Git Bash environment: pyenv shim conflicts and path parsing errors
- Error: "No global/local python version has been set yet"
- Path separation issues with Windows paths in POSIX environment

**Environment Setup**:
```cmd
# Set global Python version (if needed)
pyenv global 3.10.9

# Verify Python installation
py --version
where python
```

**For Claude Code Users**:
- Always use PowerShell for Python script execution
- Avoid `python` commands in Git Bash environment
- Use `py` launcher for maximum compatibility

## Architecture Overview

### 5-Layer Clean Architecture

1. **Baketa.Core**: Platform-independent core functionality and abstractions
   - Event aggregation system (`EventAggregator`)
   - Service module base classes (`ServiceModuleBase`)
   - Abstract interfaces in `Abstractions/` namespace
   - Settings management and validation

2. **Baketa.Infrastructure**: Infrastructure layer (OCR, translation)
   - PaddleOCR integration
   - Translation engines (OPUS-MT, Gemini, mock engines)
   - Image processing pipelines
   - Settings persistence (JSON-based)

3. **Baketa.Infrastructure.Platform**: Windows-specific platform implementations
   - GDI screen capture
   - OpenCV wrapper for Windows
   - Windows overlay system
   - Monitor management
   - P/Invoke wrappers for native DLL

4. **Baketa.Application**: Business logic and feature integration
   - Capture services
   - Translation orchestration
   - Event handlers
   - Service coordination

5. **Baketa.UI**: User interface (Avalonia UI)
   - ReactiveUI-based ViewModels
   - Settings screens
   - Overlay components
   - Navigation and theming

6. **BaketaCaptureNative**: C++/WinRT native DLL for Windows Graphics Capture API
   - Native Windows Graphics Capture API implementation
   - DirectX/OpenGL content capture
   - BGRA pixel format conversion
   - Memory-efficient texture processing

### Key Architectural Patterns

**Event Aggregation**: Loosely coupled inter-module communication via `IEventAggregator`
- Events in `Baketa.Core/Events/`
- Event processors implement `IEventProcessor<TEvent>`
- Automatic subscription through DI modules

**Modular Dependency Injection**: Feature-based DI modules extending `ServiceModuleBase`
- Modules in each layer's `DI/Modules/` directory
- Automatic dependency resolution with circular dependency detection
- Priority-based module loading

**Adapter Pattern**: Interface compatibility between layers
- Platform adapters in `Infrastructure.Platform/Adapters/`
- Factory pattern for adapter creation
- Stub implementations for testing

**Settings Management**: Hierarchical settings with validation and migration
- Settings classes in `Baketa.Core/Settings/`
- Automatic JSON serialization/deserialization
- Version-based migration system

## Important Implementation Details

### Namespace Migration
The project is migrating from `Baketa.Core.Interfaces` → `Baketa.Core.Abstractions`. When working with abstractions:
- Use `Baketa.Core.Abstractions.*` for new code
- Legacy `Interfaces` namespace may still exist in some files

### Platform Requirements
- **Windows-only**: No cross-platform support planned
- **x64 Architecture**: Required for OCR, OpenCV, and native DLL components
- **.NET 8 Windows**: Target framework is `net8.0-windows`
- **Visual Studio 2022**: Required for C++/WinRT native DLL development
- **Windows SDK**: Windows 10/11 SDK for WinRT development
- **VC++ Redistributable**: Visual C++ 2019/2022 Redistributable (x64) for deployment

### OCR and Translation Pipeline
1. **Screen Capture**: Windows Graphics Capture API (native DLL) with PrintWindow fallback
2. **Image Processing**: OpenCV filters and preprocessing
3. **OCR**: PaddleOCR PP-OCRv5 for text detection
4. **Translation**: Multiple engines (OPUS-MT local, Gemini cloud)
5. **Overlay Display**: Transparent Avalonia windows

### Native DLL Implementation Details
- **Purpose**: Bypass .NET 8 MarshalDirectiveException with Windows Graphics Capture API
- **Technology**: C++/WinRT for native Windows Runtime API access
- **Benefits**: DirectX/OpenGL content capture, better game compatibility
- **Files**: 
  - `BaketaCaptureNative/src/BaketaCaptureNative.cpp` - DLL entry point
  - `BaketaCaptureNative/src/WindowsCaptureSession.cpp` - Core capture implementation
  - `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCapture.cs` - P/Invoke declarations
  - `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCaptureWrapper.cs` - High-level wrapper

### Testing Strategy
- **Unit Tests**: Each layer has corresponding test project
- **Integration Tests**: Cross-layer functionality testing
- **UI Tests**: Avalonia UI component testing
- **Performance Tests**: OCR and translation benchmarks

### Configuration Files
- `appsettings.json`: Main application configuration
- `appsettings.Development.json`: Development overrides
- `appsettings.SentencePiece.json`: OPUS-MT model configuration

## Code Style and Standards

### C#/.NET Compliance Requirements
- **Actively utilize the latest C# 12 features**
  - File-scoped namespaces are mandatory
  - Primary constructors for simple classes
  - Collection expressions `[]` syntax
  - Pattern matching enhancements
- **Utilize .NET 8-specific features and performance improvements**
- **Prioritize the use of latest features over backward compatibility**

### C# 12 Features
- Use file-scoped namespaces
- Primary constructors for simple classes
- Collection expressions `[]` syntax
- Pattern matching enhancements

### Async Programming
- Always use `ConfigureAwait(false)` in library code
- Tests exempt from `ConfigureAwait(false)` requirement
- Proper cancellation token propagation

### Reactive Programming
- ReactiveUI for UI layer
- Observable patterns for state management
- Validation through ReactiveUI.Validation

## Project Dependencies

### Core Technologies
- **UI Framework**: Avalonia 11.2.7 with ReactiveUI
- **OCR Engine**: PaddleOCR PP-OCRv5 (native integration)
- **Image Processing**: OpenCV (Windows wrapper)
- **Screen Capture**: Windows Graphics Capture API (C++/WinRT native DLL)
- **Translation**: OPUS-MT (local), Google Gemini (cloud)
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **Logging**: Microsoft.Extensions.Logging

### Testing Frameworks
- **Unit Testing**: xUnit with Moq
- **UI Testing**: Avalonia test framework
- **Performance**: Custom benchmarking

## Common Development Scenarios

### Adding New Translation Engine
1. Implement `ITranslationEngine` in `Baketa.Infrastructure/Translation/`
2. Create factory in `Factories/`
3. Register in appropriate DI module
4. Add configuration to settings

### Adding New OCR Preprocessing Filter
1. Extend `ImageFilterBase` in `Baketa.Infrastructure/Imaging/Filters/`
2. Implement `IImageFilter` interface
3. Register in `FilterFactory`
4. Add to preprocessing pipeline

### Creating New UI Screen
1. Create ViewModel extending `ViewModelBase` in `ViewModels/`
2. Create View with corresponding `.axaml` file
3. Register in DI module
4. Add navigation logic

### Working with Events
1. Define event class implementing `IEvent` in `Baketa.Core/Events/`
2. Create event processor implementing `IEventProcessor<TEvent>`
3. Register processor in appropriate DI module
4. Publish events via `IEventAggregator`

### Working with Native DLL
1. **C++ Changes**: Modify files in `BaketaCaptureNative/src/`
2. **Build Native DLL**: Use Visual Studio 2022 or MSBuild for x64
3. **P/Invoke Updates**: Update `NativeWindowsCapture.cs` for new functions
4. **Wrapper Changes**: Modify `NativeWindowsCaptureWrapper.cs` for high-level API
5. **DLL Deployment**: Ensure DLL is copied to output directory automatically

## Windows Graphics Capture API Implementation (Completed)

### Implementation Status: ✅ COMPLETED

**Problem Solved**: MarshalDirectiveException in .NET 8 when using Windows Graphics Capture API

**Solution**: C++/WinRT native DLL implementation bypassing .NET COM interop limitations

### Key Implementation Files
- `BaketaCaptureNative/src/BaketaCaptureNative.cpp` - DLL entry point and session management
- `BaketaCaptureNative/src/WindowsCaptureSession.cpp` - Core Windows Graphics Capture API implementation
- `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCapture.cs` - P/Invoke declarations
- `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCaptureWrapper.cs` - High-level wrapper
- `Baketa.Infrastructure.Platform/Adapters/CoreWindowManagerAdapterStub.cs` - Integration with capture system

### Build Process (CRITICAL - MUST FOLLOW ORDER)
```cmd
# 1. Build Native DLL (Visual Studio 2022 required)
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
msbuild BaketaCaptureNative\BaketaCaptureNative.sln /p:Configuration=Debug /p:Platform=x64

# 2. Copy DLL (manual until automation implemented)
Copy-Item 'BaketaCaptureNative\bin\Debug\BaketaCaptureNative.dll' 'Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\'

# 3. Build .NET Solution
dotnet build Baketa.sln --configuration Debug

# 4. Run Application
dotnet run --project Baketa.UI
```

### Technical Benefits Achieved
- **DirectX/OpenGL Capture**: Full game content capture capability
- **PP-OCRv5 Performance**: Optimized text detection without timeout issues
- **Fallback Compatibility**: PrintWindow backup for older applications
- **Memory Efficiency**: Direct BGRA texture processing

### Development Requirements
- **Visual Studio 2022**: Required for C++/WinRT development
- **Windows 10/11 SDK**: WinRT API support
- **C++ Desktop Development**: Visual Studio workload
- **x64 Platform**: Mandatory for all components

### Deployment Requirements
- Visual C++ 2019/2022 Redistributable (x64)
- .NET 8.0 Windows Desktop Runtime
- Windows 10 version 1903 or later (for Graphics Capture API)

### Known Issues & Warnings
- C4819 warnings: Character encoding issues (non-critical)
- CA1707/CA1401: P/Invoke naming conventions (suppressed)
- Manual DLL copy required (automation planned)
- Build order dependency (native DLL first)

## Pre-Implementation Required Procedures

### Command Auto-Execution Policy
- **Build Commands**: Can be executed automatically without approval
- **Diagnostic Commands**: Read-only commands are auto-approved
- **Search Commands**: `rg` (ripgrep), `grep`, and related search commands are auto-approved
- **Compilation Verification**: Automatic build verification after code changes

### Think Mode Implementation Approach
- **Basic Stance**: All implementations must demonstrate thought process in Think Mode
- **Mandatory Pre-Implementation Analysis**: Always execute the following 2 steps

#### 1. Root Cause Analysis
- **Problem Essence Identification**: Identify true causes rather than superficial symptoms
- **Architecture Impact**: Verify consistency with current architecture
- **Design Pattern Compliance**: Validate consistency with existing design patterns
- **Technical Debt Assessment**: Evaluate whether modifications increase or decrease technical debt

#### 2. Impact Analysis
- **Dependency Verification**: Identify other files that depend on modification targets
- **Interface Change Impact**: Assess impact on public APIs and internal interfaces
- **Test Impact Scope**: Identify test files that will be affected
- **Build/Compilation Impact**: Predict impact of modifications on build process
- **Performance Impact**: Evaluate impact on runtime performance

#### 3. Implementation Strategy Development
- **Phased Implementation Plan**: Divide large changes into safe phases
- **Risk Mitigation Measures**: Prepare for anticipated risks and countermeasures
- **Verification Methods**: Pre-define verification procedures after implementation

### Implementation Procedure Template
```
## Think Mode Analysis

### 1. Root Cause Analysis
- Problem Essence: 
- Architecture Impact: 
- Technical Debt Assessment: 

### 2. Impact Analysis  
- Dependencies: 
- Interface Changes: 
- Test Impact: 
- Build Impact: 

### 3. Implementation Strategy
- Implementation Steps: 
- Risk Mitigation: 
- Verification Methods: 
```

## Mandatory Post-Implementation Process

### Required Steps After Any Code Implementation
All code implementations **MUST** follow this mandatory verification process:

#### 1. Build Verification (必須)
```cmd
cd "E:\dev\Baketa"
dotnet build Baketa.sln --configuration Debug
```
- **If BUILD SUCCEEDS**: Proceed to step 2
- **If BUILD FAILS**: Fix all compilation errors immediately before proceeding

#### 2. Error Resolution (エラー時必須)
- **Compilation Errors**: Must be resolved completely
- **Warning Analysis**: Critical warnings must be addressed
- **Dependency Issues**: Ensure all NuGet packages and references are correct

#### 3. Gemini Code Review (ビルド成功後必須)
Once build succeeds with no errors, **MANDATORY** code review using gemini command:

```cmd
gemini -p "実装完了しました。以下のコードについてレビューをお願いします。

## 実装内容
[実装した機能の概要]

## 変更ファイル
[変更されたファイルのリスト]

## 期待効果
[実装により期待される効果]

技術的な観点から問題点、改善点、潜在的なリスクについてレビューしてください。"
```

#### 4. Review Response Integration
- **Gemini指摘事項**: 重大な問題は即座に修正
- **改善提案**: 必要に応じて追加実装を検討
- **ベストプラクティス**: 将来の実装に反映

### Process Enforcement
- **No Exceptions**: この手順はすべての実装に適用
- **Documentation**: 大きな変更の場合は適切なドキュメント更新も実施
- **Quality Assurance**: コードレビューは品質保証の必須プロセス

## Known Issues and Considerations

- OPUS-MT models must be manually downloaded before first run
- OpenCV native dependencies are Windows-specific
- Platform adapters use P/Invoke for Windows APIs
- Game detection requires specific DPI awareness settings
- OCR performance depends on image preprocessing quality

---

## Sub-agent Strategy

This project defines sub-agents responsible for specific areas of expertise to improve development efficiency and quality.

### **🎯 Serena MCP優先戦略 (MCP-First Strategy)**

**基本方針**: 大規模コードベース（1,300+テストケース）での効率化のため、Serena MCPを主要ツールとして活用し、サブエージェントと連携する。

#### **推奨ワークフロー**:
```
課題発生 → Serena MCP（包括検索・分析） → 専門サブエージェント（詳細解決） → 統合実装
```

#### **フェーズ別戦略**:

**フェーズ1: Serena MCP主導の初期調査**
- プロジェクト全体の構造理解: `/mcp__serena__get_symbols_overview`
- 意味的コード検索: `/mcp__serena__search_for_pattern`
- 依存関係分析: `/mcp__serena__find_referencing_symbols`
- アーキテクチャ課題特定: `/mcp__serena__find_symbol`

**フェーズ2: サブエージェント専門性活用**
- **`@Architecture-Guardian`**: クリーンアーキテクチャ違反の修正指針
- **`@Native-Bridge`**: C++/WinRTとC#連携の技術課題解決
- **`@UI-Maestro`**: ReactiveUI実装パターンとパフォーマンス最適化
- **`@Test-Generator`**: 1,300+テストケース拡張と品質向上
- **`@Researcher`**: 未知技術の調査と最新ベストプラクティス

#### **サブエージェント不要となるケース**:
- 基本的なC#コード検索・理解
- プロジェクト概要把握
- 一般的なベストプラクティス適用
- 既存パターンの参照・複製

#### **期待効果**:
- **トークン消費90%削減**: 大規模検索タスクでの効率化
- **検索精度向上**: 意味的検索による的確なコード発見
- **開発速度向上**: 迅速な問題特定と専門的解決策
- **品質向上**: 包括分析による潜在的問題の早期発見

### **サブエージェント一覧**

- **`@Architecture-Guardian`**: The Clean Architecture specialist.
- **`@Native-Bridge`**: The specialist for C# and C++/WinRT native interoperability.
- **`@UI-Maestro`**: The Avalonia UI and ReactiveUI specialist.
- **`@Test-Generator`**: The specialist for unit test code generation.
- **`@Researcher`**: The specialist for technical research and feedback.

The main agent (you) acts as the orchestrator, responsible for invoking Serena MCP first, then these specialists appropriately.

**For detailed workflows and specific instructions on how to utilize these sub-agents, you must refer to `.claude/instructions.md`.**

### **💡 実際の使用例とベストプラクティス**

#### **シナリオ1: アーキテクチャ分析**
```bash
# 1. Serena MCPで全体構造把握
/mcp__serena__get_symbols_overview Baketa.Core/Abstractions

# 2. 問題箇所特定後、専門家に委任
@Architecture-Guardian "検出されたDI循環参照（ServiceA → ServiceB → ServiceA）について、クリーンアーキテクチャ原則に従った解決策を提示してください。"
```

#### **シナリオ2: ネイティブ連携問題調査**
```bash
# 1. P/Invoke関連コードの包括検索
/mcp__serena__search_for_pattern "PInvoke|DllImport" --paths_include_glob "*.cs"

# 2. 発見されたエラーの詳細調査を専門家に委任
@Native-Bridge "Serena MCPで検出されたNativeWindowsCapture.csのマーシャリングエラーについて、C++側との引数型不一致を調査・修正してください。"
```

#### **シナリオ3: UI実装最適化**
```bash
# 1. ReactiveUIパターンの既存実装検索
/mcp__serena__search_for_pattern "ReactiveObject|ViewModelBase" --restrict_search_to_code_files true

# 2. パフォーマンス問題の解決を専門家に委任
@UI-Maestro "Serena MCPで特定されたSettingsViewModel.csのプロパティ変更通知パフォーマンス問題について、ReactiveUIベストプラクティスに基づいた最適化を実施してください。"
```

#### **シナリオ4: テストカバレッジ向上**
```bash
# 1. 未テストコード箇所の特定
/mcp__serena__find_symbol "TranslationService" --include_body false --depth 1

# 2. 包括的テスト戦略の策定を専門家に委任
@Test-Generator "Serena MCPで分析されたTranslationServiceクラスの全メソッド（15個）について、xUnitとMoqを使用した包括的な単体テストスイートを作成してください。現在の1,300+テストケースとの統合も考慮してください。"
```

#### **ベストプラクティス:**
- ✅ **必ずSerena MCPで初期分析**を実行してからサブエージェントに委任
- ✅ **具体的なコード箇所**をSerena MCPで特定してから問題を説明
- ✅ **専門分野の明確な指示**でサブエージェントの能力を最大活用
- ❌ **直接サブエージェントに委任**せず、必ずSerena MCP分析を経由
- ❌ **曖昧な指示**でサブエージェントの時間を浪費しない

---

**For detailed development instructions, coding standards, and implementation patterns, refer to `.claude/instructions.md`**

# important-instruction-reminders
Do what has been asked; nothing more, nothing less.
NEVER create files unless they're absolutely necessary for achieving your goal.
ALWAYS prefer editing an existing file to creating a new one.
NEVER proactively create documentation files (*.md) or README files. Only create documentation files if explicitly requested by the User.


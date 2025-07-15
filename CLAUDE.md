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
# Build entire solution
dotnet build

# Build for Release
dotnet build --configuration Release

# Build for specific platform (x64 required for most projects)
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
- **x64 Architecture**: Required for OCR and OpenCV components
- **.NET 8 Windows**: Target framework is `net8.0-windows`
- **Visual Studio 2022**: Recommended for development

### OCR and Translation Pipeline
1. **Screen Capture**: GDI-based capture with difference detection
2. **Image Processing**: OpenCV filters and preprocessing
3. **OCR**: PaddleOCR for text detection
4. **Translation**: Multiple engines (OPUS-MT local, Gemini cloud)
5. **Overlay Display**: Transparent Avalonia windows

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
- **OCR Engine**: PaddleOCR (native integration)
- **Image Processing**: OpenCV (Windows wrapper)
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

## Known Issues and Considerations

- OPUS-MT models must be manually downloaded before first run
- OpenCV native dependencies are Windows-specific
- Platform adapters use P/Invoke for Windows APIs
- Game detection requires specific DPI awareness settings
- OCR performance depends on image preprocessing quality

---

**For detailed development instructions, coding standards, and implementation patterns, refer to `.claude/instructions.md`**

# important-instruction-reminders
Do what has been asked; nothing more, nothing less.
NEVER create files unless they're absolutely necessary for achieving your goal.
ALWAYS prefer editing an existing file to creating a new one.
NEVER proactively create documentation files (*.md) or README files. Only create documentation files if explicitly requested by the User.


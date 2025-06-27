# Baketa Project Context Configuration

## Priority File Loading

### Project Configuration & Documentation
- `README.md` - Project overview
- `CLAUDE.md` - Claude Code specific guide
- `docs/Baketa プロジェクトナレッジベース（完全版）.md` - Complete technical specifications (Japanese)
- `.editorconfig` - Coding standards
- `Directory.Build.props` - Build configuration

### Architecture Definition Files
- `Baketa.Core/Abstractions/` - All interface definitions
- `Baketa.Core/Events/` - Event definitions
- `Baketa.Core/Settings/` - Settings class definitions

### DI & Module Configuration
- `*/DI/Modules/*Module.cs` - DI modules for each layer
- `Baketa.Core/DI/ServiceModuleBase.cs` - DI module base class

### Key Service Implementations
- `Baketa.Infrastructure/OCR/` - OCR implementations
- `Baketa.Infrastructure/Translation/` - Translation engine implementations
- `Baketa.Infrastructure/Imaging/` - Image processing implementations
- `Baketa.Application/Services/` - Application services

### Platform Implementations
- `Baketa.Infrastructure.Platform/Windows/` - Windows-specific implementations
- `Baketa.Infrastructure.Platform/Adapters/` - Adapter implementations

### UI Implementation
- `Baketa.UI/ViewModels/` - ViewModel implementations
- `Baketa.UI/Views/` - View implementations (.axaml)
- `Baketa.UI/DI/Modules/UIModule.cs` - UI DI module

## Important Configuration Files

### Build & Runtime Configuration
- `Baketa.sln` - Solution definition
- `*/Baketa.*.csproj` - Project definitions
- `appsettings.json` - Application configuration
- `appsettings.SentencePiece.json` - Translation model configuration

### Scripts
- `scripts/download_opus_mt_models.ps1` - Model download
- `scripts/verify_opus_mt_models.ps1` - Model verification
- `scripts/run_sentencepiece_tests.ps1` - Test execution

## Excluded Files & Directories

### Build Artifacts
- `bin/`
- `obj/`
- `packages/`

### Model & Data Files
- `Models/` - Excluded due to large size
- `*.onnx` - Machine learning model files

### Temporary Files
- `*.backup*`
- `*.old*`
- `*removed*`
- `*.deleted`
- `.vs/`

### Test Artifacts
- `TestResults/`
- `coverage/`

## File Priorities

### Highest Priority (Always Load)
1. Project configuration files
2. Architecture definitions
3. Key interfaces

### High Priority (Load Based on Work Context)
1. Related service implementations
2. Corresponding test files
3. DI module definitions

### Medium Priority (Load as Reference)
1. Similar functionality implementation examples
2. Related documentation
3. Configuration files

### Low Priority (Load Only When Needed)
1. View implementations (.axaml)
2. Resource files
3. Script files

## Context Optimization Tips

- Always check related interface definitions when starting work
- Reference existing similar implementations when implementing new features
- Check existing examples in corresponding test projects when implementing tests
- Reference ViewModel patterns and ReactiveUI usage examples when implementing UI

## Language-Specific Context Loading

### Technical Documentation (English Priority)
- Architecture specifications
- API documentation
- Implementation patterns
- Code standards

### Business Documentation (Japanese Preserved)
- Game industry domain knowledge
- Japanese-specific requirements
- User interface localization
- Cultural considerations

### Code Comments Strategy
- **Interface definitions**: English for international compatibility
- **Business logic**: English with Japanese domain context when needed
- **Complex algorithms**: English for technical clarity
- **UI strings**: Japanese for user-facing content

This configuration optimizes Claude Code's understanding while preserving essential Japanese domain knowledge.
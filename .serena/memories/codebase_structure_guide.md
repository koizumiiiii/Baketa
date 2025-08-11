# Baketa Codebase Structure Guide

## Solution Overview
Baketa follows a 5-layer Clean Architecture pattern with additional native components:

```
Baketa.sln
├── Baketa.Core/                    # Core abstractions and business logic
├── Baketa.Infrastructure/          # Infrastructure implementations
├── Baketa.Infrastructure.Platform/ # Platform-specific implementations
├── Baketa.Application/            # Application services and orchestration
├── Baketa.UI/                     # User interface (Avalonia + ReactiveUI)
├── BaketaCaptureNative/           # C++/WinRT native DLL
└── tests/                         # Test projects for each layer
```

## Layer Detailed Structure

### 1. Baketa.Core (Platform-Independent Foundation)
```
Baketa.Core/
├── Abstractions/           # Abstract interfaces (formerly Interfaces/)
│   ├── Capture/           # Screen capture abstractions
│   ├── Common/            # Common utility interfaces
│   ├── Events/            # Event system interfaces
│   ├── Factories/         # Factory pattern interfaces
│   ├── Imaging/           # Image processing abstractions
│   ├── OCR/               # OCR engine abstractions
│   ├── Platform/          # Platform service abstractions
│   ├── Services/          # Core service interfaces
│   └── Translation/       # Translation engine abstractions
├── DI/                    # Dependency injection infrastructure
│   ├── ServiceModuleBase.cs      # Base class for DI modules
│   └── EnhancedServiceModuleBase.cs
├── Events/                # Event definitions
├── Settings/              # Configuration and settings classes
├── Services/              # Core services implementation
├── Extensions/            # Extension methods
└── Utilities/             # Utility classes
```

**Key Interfaces:**
- `IOcrEngine` - OCR processing interface
- `ITranslationEngine` - Translation service interface
- `IEventAggregator` - Event system coordination
- `ISettingsService` - Configuration management

### 2. Baketa.Infrastructure (Cross-Platform Infrastructure)
```
Baketa.Infrastructure/
├── DI/                    # Infrastructure DI modules
│   ├── Modules/           # Feature-specific modules
│   ├── PaddleOcrModule.cs # OCR engine registration
│   └── BatchOcrModule.cs  # Batch processing registration
├── OCR/                   # OCR implementations
│   └── PaddleOCR/         # PaddleOCR integration
├── Translation/           # Translation engine implementations
│   ├── MockTranslationEngine.cs
│   └── DefaultTranslationService.cs
├── Imaging/               # Image processing services
│   ├── Services/          # Image processing implementations
│   └── Filters/           # Image filter implementations
├── Services/              # Infrastructure services
└── Performance/           # Performance analysis utilities
```

**Key Implementations:**
- `PaddleOcrEngine` - PP-OCRv5 implementation
- `MockTranslationEngine` - Development/testing engine
- Image processing pipeline with OpenCV integration

### 3. Baketa.Infrastructure.Platform (Windows-Specific)
```
Baketa.Infrastructure.Platform/
├── Adapters/              # Platform adapter implementations
│   ├── CaptureAdapter.cs         # Screen capture adaptation
│   ├── WindowsImageAdapter.cs    # Windows image handling
│   └── WindowManagerAdapter.cs   # Window management
├── Windows/               # Windows-specific implementations
│   ├── WindowsImage.cs           # Windows image representation
│   ├── GdiWindowsCapturer.cs     # GDI-based capture
│   └── WindowsGraphicsCapturer.cs # Graphics Capture API
├── DI/                    # Platform DI modules
└── Resources/             # Platform resources
```

**Key Components:**
- Windows Graphics Capture API integration
- GDI fallback capture mechanism
- Platform-specific image format handling

### 4. Baketa.Application (Business Logic Orchestration)
```
Baketa.Application/
├── DI/                    # Application-level DI modules
│   └── Modules/           # Feature orchestration modules
├── Services/              # Application services
│   ├── CompositeOcrEngine.cs     # OCR engine composition
│   ├── CachedOcrEngine.cs        # OCR caching wrapper
│   └── OcrEngineInitializerService.cs # Background initialization
├── Translation/           # Translation orchestration
│   ├── StandardTranslationPipeline.cs
│   └── TranslationTransactionManager.cs
├── EventHandlers/         # Application event handlers
└── Models/                # Application-specific models
```

**Key Features:**
- **Gemini 3-Stage OCR Optimization**:
  - Stage 1: OCR Engine Pooling
  - Stage 2: Staged OCR Strategy with background initialization
  - Stage 3: Advanced Caching with SHA256 hashing and LRU eviction

### 5. Baketa.UI (User Interface Layer)
```
Baketa.UI/
├── ViewModels/            # ReactiveUI ViewModels
│   ├── MainViewModel.cs          # Primary application VM
│   ├── SettingsViewModel.cs      # Settings configuration
│   └── AccessibilitySettingsViewModel.cs # Accessibility features
├── Views/                 # Avalonia Views (.axaml + .axaml.cs)
│   ├── MainOverlayView.axaml     # Translation overlay
│   ├── SettingsView.axaml        # Settings interface
│   └── CaptureView.axaml         # Capture configuration
├── Controls/              # Custom UI controls
├── Converters/            # Value converters for data binding
├── Styles/                # AXAML styling resources
├── DI/                    # UI-specific DI modules
├── Services/              # UI services
└── Framework/             # UI framework extensions
```

**UI Architecture:**
- **MVVM Pattern**: Strict separation using ReactiveUI
- **Avalonia 11.2.7**: Cross-platform UI framework
- **Custom Styling**: Game-optimized overlay styling
- **Accessibility**: Full accessibility support implementation

### 6. BaketaCaptureNative (C++/WinRT Native DLL)
```
BaketaCaptureNative/
├── src/
│   ├── BaketaCaptureNative.cpp   # DLL entry point
│   ├── WindowsCaptureSession.cpp # Core capture implementation
│   ├── WindowsCaptureSession.h   # Session management
│   └── pch.h                     # Precompiled headers
├── include/
│   └── BaketaCaptureNative.h     # Public API definitions
├── bin/                          # Built DLL output
└── BaketaCaptureNative.vcxproj   # Visual Studio project
```

**Native Component Purpose:**
- Bypass .NET 8 MarshalDirectiveException with Windows Graphics Capture API
- Direct DirectX/OpenGL content capture
- High-performance BGRA texture processing
- Memory-efficient capture session management

## Test Project Structure
```
tests/
├── Baketa.Core.Tests/             # Core layer tests
├── Baketa.Infrastructure.Tests/   # Infrastructure tests
├── Baketa.Infrastructure.Platform.Tests/ # Platform-specific tests
├── Baketa.Application.Tests/      # Application layer tests
├── Baketa.UI.Tests/               # UI unit tests
├── Baketa.UI.IntegrationTests/    # UI integration tests
├── Baketa.Integration.Tests/      # Cross-layer integration tests
└── test_data/                     # Shared test resources
```

## Configuration and Scripts

### Configuration Structure
```
Project Root/
├── appsettings.json              # Main configuration
├── appsettings.Development.json  # Development overrides
├── appsettings.SentencePiece.json # OPUS-MT model configuration
├── Directory.Build.props         # MSBuild global properties
└── .editorconfig                 # Code style enforcement
```

### Scripts Directory
```
scripts/
├── build_all.ps1                 # Complete build automation
├── run_build.ps1                 # Standard build script
├── download_opus_mt_models.ps1   # Model setup automation
├── verify_opus_mt_models.ps1     # Model integrity verification
├── current_bottleneck_analysis.py # Performance analysis
└── diagnose_capture_issues.ps1   # Debug utilities
```

## Model and Data Files
```
Models/
├── ONNX/                         # ONNX model files
│   ├── helsinki-opus-mt-ja-en.onnx
│   └── opus-mt-ja-en.onnx
├── SentencePiece/                # Tokenizer models
│   ├── opus-mt-ja-en.model
│   └── source.spm
├── pp-ocrv5/                     # PaddleOCR models
└── HuggingFace/                  # Alternative model sources
```

## Build and Deployment Structure
```
bin/                              # Build outputs
obj/                              # Intermediate build files
packages/                         # NuGet package cache (if local)
.vs/                             # Visual Studio files
.vscode/                         # VS Code configuration
```

## Documentation Structure
```
docs/
├── 1-project/                    # Project overview documentation
├── 2-development/                # Development guidelines
├── 3-architecture/               # Architecture documentation
├── 4-implementation/             # Implementation guides
└── performance-analysis-detailed-report.md # Performance analysis
```

## Key Architectural Decisions

### Namespace Migration
- **Old**: `Baketa.Core.Interfaces` 
- **New**: `Baketa.Core.Abstractions`
- Use new namespace for all new code

### Dependency Flow
```
UI → Application → Infrastructure → Core
     ↓              ↓
Platform ← ← ← ← ← ← ← 
```

### Module Registration Order
1. CoreModule (base services)
2. InfrastructureModule (core implementations)
3. PlatformModule (Windows-specific)
4. ApplicationModule (business logic)
5. StagedOcrStrategyModule (Gemini Stage 2)
6. AdvancedCachingModule (Gemini Stage 3)
7. UIModule (interface layer)
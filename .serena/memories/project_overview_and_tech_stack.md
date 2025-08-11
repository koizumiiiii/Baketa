# Baketa Project Overview and Tech Stack

## Project Purpose
Baketa is a Windows-specific real-time text translation overlay application for games. It uses OCR technology to detect text from game screens and displays translation results as a transparent overlay. The application features advanced image processing and OCR optimization for effective text detection and translation across various gaming scenarios.

## Core Technology Stack

### .NET Framework
- **Target Framework**: .NET 8 Windows (`net8.0-windows`)
- **Language**: C# 12 with latest features enabled
- **Platform**: Windows-only (x64 architecture required)

### UI Framework
- **Primary UI**: Avalonia 11.2.7 with ReactiveUI
- **Architecture Pattern**: MVVM with ReactiveUI
- **Styling**: Custom AXAML styles with FluentUI influences

### OCR and Image Processing
- **OCR Engine**: PaddleOCR PP-OCRv5 (native integration)
- **Image Processing**: OpenCV (Windows wrapper)
- **Screen Capture**: Windows Graphics Capture API (C++/WinRT native DLL)
- **Fallback Capture**: PrintWindow API

### Translation Engines
- **Local**: OPUS-MT (ONNX runtime)
- **Cloud**: Google Gemini API
- **Mock Engine**: For development and testing

### Native Components
- **Native DLL**: C++/WinRT implementation for Windows Graphics Capture API
- **Purpose**: Bypass .NET 8 MarshalDirectiveException limitations
- **Benefits**: DirectX/OpenGL content capture, better game compatibility

### Dependency Injection
- **Container**: Microsoft.Extensions.DependencyInjection
- **Architecture**: Modular DI with ServiceModuleBase pattern
- **Features**: Circular dependency detection, priority-based loading

### Logging and Configuration
- **Logging**: Microsoft.Extensions.Logging with Console output
- **Configuration**: JSON-based with environment-specific overrides
- **Settings**: Hierarchical settings with validation and migration

## Architecture Layers

### 1. Baketa.Core
- Platform-independent core functionality and abstractions
- Event aggregation system (EventAggregator)
- Service module base classes (ServiceModuleBase)
- Abstract interfaces in Abstractions/ namespace
- Settings management and validation

### 2. Baketa.Infrastructure
- Infrastructure layer (OCR, translation, services)
- PaddleOCR integration
- Translation engines (OPUS-MT, Gemini, mock engines)
- Image processing pipelines
- Settings persistence (JSON-based)

### 3. Baketa.Infrastructure.Platform
- Windows-specific platform implementations
- GDI screen capture
- OpenCV wrapper for Windows
- Windows overlay system
- Monitor management
- P/Invoke wrappers for native DLL

### 4. Baketa.Application
- Business logic and feature integration
- Capture services
- Translation orchestration
- Event handlers
- Service coordination

### 5. Baketa.UI
- User interface (Avalonia UI)
- ReactiveUI-based ViewModels
- Settings screens
- Overlay components
- Navigation and theming

### 6. BaketaCaptureNative
- C++/WinRT native DLL for Windows Graphics Capture API
- Native Windows Graphics Capture API implementation
- DirectX/OpenGL content capture
- BGRA pixel format conversion
- Memory-efficient texture processing

## Key Architectural Patterns

### Event Aggregation
- Loosely coupled inter-module communication via IEventAggregator
- Events in Baketa.Core/Events/
- Event processors implement IEventProcessor<TEvent>
- Automatic subscription through DI modules

### Modular Dependency Injection
- Feature-based DI modules extending ServiceModuleBase
- Modules in each layer's DI/Modules/ directory
- Automatic dependency resolution with circular dependency detection
- Priority-based module loading

### Adapter Pattern
- Interface compatibility between layers
- Platform adapters in Infrastructure.Platform/Adapters/
- Factory pattern for adapter creation
- Stub implementations for testing

### Settings Management
- Hierarchical settings with validation and migration
- Settings classes in Baketa.Core/Settings/
- Automatic JSON serialization/deserialization
- Version-based migration system
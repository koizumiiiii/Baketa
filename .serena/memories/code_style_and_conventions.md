# Baketa Code Style and Conventions

## C# Language Features and Standards

### C# 12 Features (Mandatory)
- **File-scoped namespaces**: Required for all new files
- **Primary constructors**: Use for simple classes
- **Collection expressions**: Use `[]` syntax instead of `new List<>()`
- **Pattern matching enhancements**: Utilize latest pattern matching features
- **Nullable reference types**: Enabled throughout the project

### .NET 8 Compliance
- Target framework: `net8.0-windows`
- Prioritize latest .NET 8 features over backward compatibility
- Use performance improvements and new APIs where applicable

### Language Configuration
```xml
<LangVersion>12.0</LangVersion>
<Features>InterceptorsPreview</Features>
<AnalysisLevel>latest</AnalysisLevel>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

## Code Style Requirements

### Async Programming
- **Always use `ConfigureAwait(false)`** in library code
- Tests are exempt from ConfigureAwait(false) requirement
- Proper cancellation token propagation throughout async chains
- Use async/await for all I/O operations

### Reactive Programming
- ReactiveUI for UI layer state management
- Observable patterns for reactive state changes
- Validation through ReactiveUI.Validation framework
- Use Observer.Create for exception handling

### Namespace Conventions
- **Migration in progress**: `Baketa.Core.Interfaces` → `Baketa.Core.Abstractions`
- Use `Baketa.Core.Abstractions.*` for new code
- Legacy `Interfaces` namespace may still exist in some files

### Performance Considerations
- Use `Span<T>` and `Memory<T>` for memory-efficient operations
- Implement `IDisposable` pattern correctly with DisposableBase
- Use object pooling for frequently allocated objects
- Unsafe code allowed for performance-critical image processing

## Project Structure Conventions

### File Organization
```
ProjectRoot/
├── Abstractions/          # Abstract interfaces and contracts
├── DI/                   # Dependency injection modules
│   └── Modules/         # Feature-specific DI modules
├── Events/              # Event definitions
├── Services/            # Service implementations
├── Settings/            # Configuration and settings
└── Extensions/          # Extension methods
```

### Naming Conventions
- **Interfaces**: Start with `I` (e.g., `IOcrEngine`)
- **Events**: End with `Event` (e.g., `TranslationTriggeredEvent`)
- **Services**: End with `Service` (e.g., `TranslationService`)
- **ViewModels**: End with `ViewModel` (e.g., `SettingsViewModel`)
- **Modules**: End with `Module` (e.g., `CoreModule`)

## Testing Conventions

### Test Framework
- **Unit Testing**: xUnit with Moq for mocking
- **UI Testing**: Avalonia test framework
- **Performance Testing**: Custom benchmarking utilities
- **Integration Testing**: Separate test projects for integration scenarios

### Test Organization
```
tests/
├── Baketa.Core.Tests/
├── Baketa.Infrastructure.Tests/
├── Baketa.Infrastructure.Platform.Tests/
├── Baketa.Application.Tests/
├── Baketa.UI.Tests/
├── Baketa.UI.IntegrationTests/
└── Baketa.Integration.Tests/
```

### Test Patterns
- Use descriptive test method names
- Follow Arrange-Act-Assert pattern
- Mock external dependencies using Moq
- Use test data builders for complex objects

## Documentation Standards

### Code Comments
- **DO NOT ADD COMMENTS** unless explicitly requested
- XML documentation for public APIs only
- Focus on self-documenting code through clear naming

### Documentation Files
- **NEVER proactively create documentation files**
- Only create .md files when explicitly requested
- Maintain existing documentation files when editing

## Build and Quality Standards

### Code Analysis
- **EnableNETAnalyzers**: true
- **AnalysisMode**: Recommended
- EditorConfig rules enforced at build time
- Warnings treated as errors where appropriate

### Build Requirements
- Must build successfully on Windows x64
- Support for both Debug and Release configurations
- Visual Studio 2022 required for native DLL development
- .NET 8 SDK required

### Performance Standards
- OCR processing: Target < 2 seconds (achieved through 3-stage optimization)
- UI responsiveness: < 100ms for user interactions
- Memory management: Proper disposal of unmanaged resources
- Startup time: < 20 seconds (optimized from 2 minutes)

## Platform-Specific Considerations

### Windows Requirements
- Windows 10 version 1903 or later (for Graphics Capture API)
- Visual C++ 2019/2022 Redistributable (x64)
- Windows SDK for WinRT development
- x64 platform mandatory for all components

### Interop Standards
- Use P/Invoke for Windows API calls
- Implement proper marshaling for complex types
- Handle Win32 errors appropriately
- Use safe handles for native resources
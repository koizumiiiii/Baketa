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
- `Baketa.Core/Services/` - Core service implementations (Privacy, Feedback, Update)

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
- `appsettings.Development.json` - Development overrides
- `Directory.Build.props` - Global build properties
- `.github/workflows/ci.yml` - CI/CD pipeline configuration

### Scripts
- `scripts/build-release.ps1` - Release package build
- `scripts/run_app.ps1` - Application execution
- `scripts/run_tests.ps1` - Test execution automation
- `scripts/check-environment.ps1` - Environment check
- `scripts/code-review-simple.ps1` - Static code review
- `scripts/download-ppocrv5-models.ps1` - OCR model download

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

## Current Implementation Status (v0.2.x)

### Recently Implemented Features
- **Privacy Management**: GDPR-compliant consent system in `Baketa.Core/Services/PrivacyConsentService.cs`
- **Feedback System**: GitHub Issues API integration in `Baketa.Core/Services/FeedbackService.cs`
- **Update System**: GitHub Releases API with SparkleUpdater (Ed25519 signatures)
- **Surya OCR Integration**: gRPC-based Python server with ONNX INT8 detection and PyTorch recognition
- **NLLB-200 Translation**: CTranslate2-optimized multilingual translation (80% memory reduction)
- **Cloud AI Translation**: Gemini API integration via Cloudflare Workers relay
- **ROI Manager**: Learning-based text detection optimization (Issue #293)
- **Security Enhancements**: CodeQL-compliant exception handling patterns
- **CI/CD Pipeline**: GitHub Actions with Windows Server 2022, sequential test execution
- **Comprehensive Testing**: 2,100+ test cases with extensive coverage

### Key Implementation Patterns
- **Exception Handling**: Specific exception types instead of generic `catch (Exception)`
- **Security**: OutOfMemoryException and StackOverflowException protection
- **Async Programming**: Consistent `ConfigureAwait(false)` usage
- **Dependency Injection**: Modular service registration with `ServiceModuleBase`
- **Event Aggregation**: Loose coupling via `IEventAggregator`

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

---

# Log Management Rules

## **📋 Log File Management Policy**

### **Essential Log Files**
Only the following log files should be retained and used for analysis:

1. **`debug_app_logs.txt`** - Main application log (69.7KB)
   - **Purpose**: Latest OCR results, general application behavior
   - **Priority**: Highest
   - **Retention**: Continuously updated, maintain latest state

2. **`translation_debug_output.txt`** - Translation debug log (47KB) 
   - **Purpose**: Translation engine debug information
   - **Priority**: High
   - **Retention**: Reference during translation issue investigation

3. **`debug_translation_errors.txt`** - Translation error tracking (2.3KB)
   - **Purpose**: Detailed translation failure error information
   - **Priority**: High  
   - **Retention**: Reference during error investigation

4. **`debug_batch_ocr.txt`** - OCR performance analysis (571 bytes)
   - **Purpose**: OCR batch processing performance measurement
   - **Priority**: Medium
   - **Retention**: Reference during performance analysis

### **Deleted Obsolete Log Files**
The following log files were deleted on 2025/08/14 and are prohibited from future creation:

- ❌ `latest_run.txt` (190KB) - Redundant execution log
- ❌ `translation_flow_analysis.txt` (435KB) - Outdated analysis log
- ❌ `debug_memory_usage.txt` (3.8KB) - Outdated memory usage
- ❌ `debug_performance_analysis.txt` (8.5KB) - Outdated performance analysis
- ❌ `debug_captured_*.png` (4 files) - Outdated captured images
- ❌ `debug_*.txt.old` (backup files) - Outdated backups

### **Log Reference Rules**

#### **Log File Selection by Analysis Purpose**

| Analysis Purpose | Log Files to Reference | Description |
|------------------|------------------------|-------------|
| **OCR Results Verification** | `debug_app_logs.txt` | Latest OCR detection results and text |
| **Translation Results Verification** | `debug_app_logs.txt` | Translation outputs and overlay display results |
| **Translation Error Investigation** | `debug_translation_errors.txt` → `debug_app_logs.txt` | Error details → Overall flow verification |
| **Application Behavior Analysis** | `debug_app_logs.txt` | General application operation |
| **Performance Analysis** | `debug_app_logs.txt` + `debug_batch_ocr.txt` | Overall operation + OCR-specific analysis |
| **Translation Engine Investigation** | `translation_debug_output.txt` | Translation engine internal operation |

#### **Log Content Verification Rules**

1. **Required Verification Steps**:
   - Verify log file last modification time
   - Check file size validity  
   - Confirm consistency between actual file content and reported content

2. **Handling Inconsistencies**:
   - Immediately correct when reported content differs from actual log content
   - Re-read log files and re-verify content
   - Apologize to user for inconsistencies and provide accurate information

3. **Information Quality Assurance**:
   - Prohibit information provision based on speculation or assumptions
   - Base responses only on actual log file content
   - Clearly state "Verifying log file" when uncertain

### **Implementation Guidelines**

1. **Log Output Control**:
   - Delete/disable output code to deleted log files
   - Output only to essential log files in new implementations
   - Consider implementing log rotation functionality

2. **Disk Space Management**:
   - Total size of essential log files: ~120KB (significant reduction achieved)
   - Saved 640KB+ disk space through deletion
   - Regular file size monitoring

3. **Development Efficiency Improvement**:
   - Analysis efficiency improved through significant reduction in log file count
   - Reduced problem identification time through important information consolidation
   - Eliminated confusion from unnecessary logs

---

**Established**: 2025-08-14  
**Last Updated**: 2025-08-14  
**Established by**: Claude Code Assistant  
**Scope**: Baketa project-wide log management
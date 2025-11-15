# Language Configuration Refactoring Plan

## 📋 Overview

This document outlines the comprehensive refactoring plan to eliminate language configuration redundancy and establish a clean, unified language management architecture.

## 🎯 Current Problems

### Problem Analysis
- **Configuration Duplication**: `DefaultSourceLanguage`/`DefaultTargetLanguage` exist in both `appsettings.json` and UI settings
- **Setting Conflicts**: appsettings.json has `en→ja` while UI settings have `Japanese→English`
- **Clean Architecture Violations**: Multiple handlers directly access `IConfiguration` instead of using proper abstraction
- **Maintenance Issues**: Adding new languages requires modifications in multiple locations

### Affected Components
1. **PriorityAwareOcrCompletedHandler.cs** - Lines 70-71
2. **CaptureCompletedHandler.cs** - Lines 431-432
3. **TranslationExecutionStageStrategy.cs** - Lines 62-63
4. **TranslationCompletedHandler.cs** - Lines 52-53
5. **appsettings.json** - Translation section
6. **Baketa.UI/appsettings.json** - Translation section

## 🏗️ Proposed Architecture

### Design Principles
- **Single Source of Truth**: UI settings become the only language configuration source
- **Clean Architecture Compliance**: Proper dependency inversion and separation of concerns
- **Type Safety**: Replace string-based configuration with strongly-typed objects
- **Testability**: Mockable service injection for unit testing

### Core Components

#### 1. Strongly-Typed Language Models
```csharp
// Location: Baketa.Core/Models/Translation/LanguagePair.cs
public sealed record LanguagePair(Language Source, Language Target)
{
    public string SourceCode => Source.Code;
    public string TargetCode => Target.Code;

    public static LanguagePair Default => new(
        Language.Japanese,
        Language.English);

    public bool IsValidForTranslation() =>
        !Source.Equals(Target);
}

// Location: Baketa.Core/Models/Translation/Language.cs
public sealed record Language(string Code, string DisplayName)
{
    public static Language Japanese => new("ja", "Japanese");
    public static Language English => new("en", "English");

    public static Language FromCode(string code) => code?.ToLowerInvariant() switch
    {
        "ja" or "ja-jp" or "jpn_jpan" or "japanese" => Japanese,
        "en" or "en-us" or "eng_latn" or "english" => English,
        _ => throw new ArgumentException($"Unsupported language code: {code}")
    };
}
```

#### 2. Language Configuration Service Interface
```csharp
// Location: Baketa.Core/Abstractions/Translation/ILanguageConfigurationService.cs
public interface ILanguageConfigurationService
{
    /// <summary>
    /// Gets the current language pair from UI settings
    /// </summary>
    LanguagePair GetCurrentLanguagePair();

    /// <summary>
    /// Gets the current language pair asynchronously (for I/O operations)
    /// </summary>
    Task<LanguagePair> GetLanguagePairAsync();

    /// <summary>
    /// Indicates if automatic source language detection is enabled
    /// </summary>
    bool IsAutoDetectionEnabled { get; }

    /// <summary>
    /// Updates the language pair in persistent storage
    /// </summary>
    Task UpdateLanguagePairAsync(LanguagePair pair);

    /// <summary>
    /// Event fired when language configuration changes
    /// </summary>
    event EventHandler<LanguagePair> LanguagePairChanged;
}
```

#### 3. Unified Implementation
```csharp
// Location: Baketa.Infrastructure/Services/Translation/UnifiedLanguageConfigurationService.cs
public sealed class UnifiedLanguageConfigurationService : ILanguageConfigurationService
{
    private readonly IUnifiedSettingsService _settingsService;
    private readonly ILogger<UnifiedLanguageConfigurationService> _logger;
    private LanguagePair? _cachedLanguagePair;
    private readonly object _cacheLock = new();

    public event EventHandler<LanguagePair>? LanguagePairChanged;

    public LanguagePair GetCurrentLanguagePair()
    {
        lock (_cacheLock)
        {
            if (_cachedLanguagePair is not null)
                return _cachedLanguagePair;

            var settings = _settingsService.GetTranslationSettings();
            _cachedLanguagePair = new LanguagePair(
                Language.FromCode(settings.DefaultSourceLanguage),
                Language.FromCode(settings.DefaultTargetLanguage));

            return _cachedLanguagePair;
        }
    }

    public async Task<LanguagePair> GetLanguagePairAsync()
    {
        // For future async settings retrieval
        return await Task.FromResult(GetCurrentLanguagePair());
    }

    public bool IsAutoDetectionEnabled =>
        _settingsService.GetTranslationSettings().AutoDetectSourceLanguage;

    public async Task UpdateLanguagePairAsync(LanguagePair pair)
    {
        var settings = _settingsService.GetTranslationSettings();

        // Update settings
        var updatedSettings = settings with
        {
            DefaultSourceLanguage = pair.SourceCode,
            DefaultTargetLanguage = pair.TargetCode
        };

        await _settingsService.UpdateTranslationSettingsAsync(updatedSettings);

        // Update cache and notify
        lock (_cacheLock)
        {
            _cachedLanguagePair = pair;
        }

        LanguagePairChanged?.Invoke(this, pair);

        _logger.LogInformation("Language pair updated: {Source} → {Target}",
            pair.Source.DisplayName, pair.Target.DisplayName);
    }
}
```

## 🔄 Implementation Plan

### Phase 1: Infrastructure Setup (Day 1)

#### 1.1 Create Core Models
- [ ] `Language.cs` - Language representation with type safety
- [ ] `LanguagePair.cs` - Language pair with validation
- [ ] Unit tests for models

#### 1.2 Create Service Interface
- [ ] `ILanguageConfigurationService.cs` - Service abstraction
- [ ] Documentation and XML comments

#### 1.3 Implementation
- [ ] `UnifiedLanguageConfigurationService.cs` - Core implementation
- [ ] Integration with existing `IUnifiedSettingsService`
- [ ] Caching mechanism with thread safety

#### 1.4 Dependency Injection Setup
```csharp
// Location: Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs
services.AddScoped<ILanguageConfigurationService, UnifiedLanguageConfigurationService>();
```

### Phase 2: Handler Refactoring (Day 1-2)

#### 2.1 PriorityAwareOcrCompletedHandler
```csharp
// Before:
var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

// After:
var languagePair = await _languageConfig.GetLanguagePairAsync();
await ProcessPrioritizedTranslationsAsync(prioritizedTexts,
    languagePair.SourceCode, languagePair.TargetCode);
```

#### 2.2 CaptureCompletedHandler
```csharp
// Before:
var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

// After:
var languagePair = _languageConfig.GetCurrentLanguagePair();
var translationEvent = new TranslationCompletedEvent(
    sourceText: result.OcrResult?.DetectedText ?? "",
    translatedText: result.TranslationResult?.TranslatedText ?? "",
    sourceLanguage: languagePair.Source.DisplayName,
    targetLanguage: languagePair.Target.DisplayName,
    // ... other parameters
);
```

#### 2.3 TranslationExecutionStageStrategy
```csharp
// Before:
var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

// After:
var languagePair = await _languageConfig.GetLanguagePairAsync();
var translationRequest = new CoreTranslationRequest
{
    SourceText = ocrResult.DetectedText,
    SourceLanguage = Language.FromCode(languagePair.SourceCode),
    TargetLanguage = Language.FromCode(languagePair.TargetCode),
    // ... other properties
};
```

#### 2.4 TranslationCompletedHandler
```csharp
// Before:
var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

// After:
var languagePair = _languageConfig.GetCurrentLanguagePair();
// Use languagePair.SourceCode and languagePair.TargetCode
```

### Phase 3: Configuration Cleanup (Day 2)

#### 3.1 Remove from appsettings.json
```json
{
  "Translation": {
    // ❌ Remove these lines:
    // "DefaultSourceLanguage": "en",
    // "DefaultTargetLanguage": "ja",

    // ✅ Keep engine-specific settings:
    "LogSkippedTranslations": true,
    "SameLanguageDetectionMode": "Strict",
    "EnableServerAutoRestart": true,
    "MaxConsecutiveFailures": 3,
    "TimeoutSeconds": 120
  }
}
```

#### 3.2 Remove from Baketa.UI/appsettings.json
```json
{
  "Translation": {
    // ❌ Remove these lines:
    // "DefaultSourceLanguage": "en",
    // "DefaultTargetLanguage": "ja",

    // ✅ Keep other settings...
  }
}
```

#### 3.3 Update UnifiedSettingsService
- Remove `DefaultSourceLanguage` and `DefaultTargetLanguage` from configuration models
- Ensure UI settings remain as the primary source

## 🧪 Testing Strategy

### Unit Tests
```csharp
// Location: tests/Baketa.Core.Tests/Models/Translation/LanguagePairTests.cs
[Test]
public void LanguagePair_WithSameLanguages_ShouldIndicateInvalidForTranslation()
{
    var pair = new LanguagePair(Language.English, Language.English);
    Assert.False(pair.IsValidForTranslation());
}

// Location: tests/Baketa.Infrastructure.Tests/Services/Translation/UnifiedLanguageConfigurationServiceTests.cs
[Test]
public async Task GetLanguagePairAsync_ShouldReturnUISettingsLanguagePair()
{
    // Arrange
    var mockSettings = new Mock<IUnifiedSettingsService>();
    mockSettings.Setup(s => s.GetTranslationSettings())
        .Returns(new UnifiedTranslationSettings(...) {
            DefaultSourceLanguage = "ja",
            DefaultTargetLanguage = "en"
        });

    var service = new UnifiedLanguageConfigurationService(mockSettings.Object, Mock.Of<ILogger<...>>());

    // Act
    var result = await service.GetLanguagePairAsync();

    // Assert
    Assert.Equal(Language.Japanese, result.Source);
    Assert.Equal(Language.English, result.Target);
}
```

### Integration Tests
- Test that all handlers use the new service correctly
- Verify UI setting changes are properly reflected
- Ensure configuration consistency across the application

## 📈 Expected Benefits

### Immediate Benefits
- **Consistency**: No more conflicting language settings
- **Maintainability**: Single point of language configuration management
- **Type Safety**: Compile-time error detection for language-related issues
- **Testability**: Easy mocking and testing of language configuration

### Long-term Benefits
- **Scalability**: Easy addition of new languages
- **Clean Architecture**: Proper separation of concerns and dependency inversion
- **Performance**: Cached language pairs reduce repetitive setting lookups
- **Robustness**: Centralized validation and error handling

## ⚠️ Risk Mitigation

### Potential Risks and Mitigation Strategies

1. **Missing Configuration References**
   - **Risk**: Some handlers might still reference old configuration keys
   - **Mitigation**: Comprehensive codebase search for `"DefaultSourceLanguage"` and `"DefaultTargetLanguage"` strings
   - **Verification**: Use IDE "Find All References" and code analysis tools

2. **Breaking Changes**
   - **Risk**: Existing functionality might break during refactoring
   - **Mitigation**: Incremental implementation with thorough testing at each step
   - **Rollback Plan**: Keep feature branches for easy rollback if issues arise

3. **Performance Impact**
   - **Risk**: Service layer might introduce latency
   - **Mitigation**: Implement caching mechanism and async patterns
   - **Monitoring**: Performance benchmarks before and after implementation

4. **DI Container Configuration**
   - **Risk**: Incorrect service lifetime or registration
   - **Mitigation**: Use `Scoped` lifetime to handle UI setting changes properly
   - **Testing**: Integration tests to verify DI resolution

## 🎯 Success Criteria

- [ ] All handlers use `ILanguageConfigurationService` instead of direct `IConfiguration` access
- [ ] No references to `"DefaultSourceLanguage"` or `"DefaultTargetLanguage"` strings in codebase
- [ ] appsettings.json files cleaned of redundant language settings
- [ ] UI language setting changes immediately reflected in translation behavior
- [ ] All existing functionality preserved with improved consistency
- [ ] 100% test coverage for new language configuration components
- [ ] No performance regression in translation pipeline

## 📅 Timeline

| Phase | Duration | Tasks | Deliverables |
|-------|----------|-------|--------------|
| **Phase 1** | 1 Day | Infrastructure setup | Core models, service interface, DI registration |
| **Phase 2** | 1-2 Days | Handler refactoring | Updated handlers, integration tests |
| **Phase 3** | 1 Day | Configuration cleanup | Cleaned config files, documentation |
| **Testing** | 1 Day | Comprehensive testing | Test suite, validation |

**Total Estimated Duration: 3-4 Days**

## ✅ Approval Status

- [x] **UltraThink Design Review**: Completed
- [x] **Gemini AI Architecture Review**: ✅ Excellent rating with strong support
- [ ] **Implementation Ready**: Pending approval to proceed

---

*This refactoring plan addresses the fundamental issues with language configuration management and establishes a robust, scalable architecture that follows Clean Architecture principles and eliminates configuration conflicts.*
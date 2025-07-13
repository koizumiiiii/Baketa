using System;
using Xunit;
using Microsoft.Extensions.Logging;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Validation;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// AlphaTestSettingsValidatorのテスト
/// </summary>
public class AlphaTestSettingsValidatorTests
{
    private readonly AlphaTestSettingsValidator _validator;

    public AlphaTestSettingsValidatorTests()
    {
        var logger = new TestLogger<AlphaTestSettingsValidator>();
        _validator = new AlphaTestSettingsValidator(logger);
    }

    #region 翻訳エンジン検証テスト

    [Fact]
    public void Validate_ValidLocalEngine_ShouldSucceed()
    {
        // Arrange
        var settings = new AppSettings
        {
            Translation = new TranslationSettings
            {
                DefaultEngine = TranslationEngine.Local
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_InvalidGeminiEngine_ShouldFail()
    {
        // Arrange
        var settings = new AppSettings
        {
            Translation = new TranslationSettings
            {
                DefaultEngine = TranslationEngine.Gemini
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.ErrorMessage!.Contains("Local (OPUS-MT)"));
    }

    #endregion

    #region 言語ペア検証テスト

    [Fact]
    public void Validate_ValidJapaneseToEnglish_ShouldSucceed()
    {
        // Arrange
        var settings = new AppSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "ja",
                DefaultTargetLanguage = "en"
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SameLanguagePair_ShouldFail()
    {
        // Arrange
        var settings = new AppSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "ja",
                DefaultTargetLanguage = "Japanese"
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage!.Contains("同じ言語"));
    }

    [Fact]
    public void Validate_UnsupportedLanguage_ShouldFail()
    {
        // Arrange
        var settings = new AppSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "fr", // フランス語（αテストでは非対応）
                DefaultTargetLanguage = "en"
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage!.Contains("日本語↔英語"));
    }

    #endregion

    #region パネルサイズ検証テスト

    [Fact]
    public void Validate_SmallPanelSize_ShouldSucceed()
    {
        // Arrange
        var settings = new AppSettings
        {
            MainUi = new MainUiSettings
            {
                PanelSize = UiSize.Small
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_LargePanelSize_ShouldSucceedWithWarning()
    {
        // Arrange
        var settings = new AppSettings
        {
            MainUi = new MainUiSettings
            {
                PanelSize = UiSize.Large
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.WarningMessage!.Contains("画面を多く占有"));
    }

    #endregion

    #region 透明度検証テスト

    [Fact]
    public void Validate_ValidOpacity_ShouldSucceed()
    {
        // Arrange
        var settings = new AppSettings
        {
            MainUi = new MainUiSettings
            {
                PanelOpacity = 0.8 // 80%
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TooLowOpacity_ShouldFail()
    {
        // Arrange
        var settings = new AppSettings
        {
            MainUi = new MainUiSettings
            {
                PanelOpacity = 0.05 // 5%（範囲外）
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage!.Contains("10%以上90%以下"));
    }

    [Fact]
    public void Validate_TooHighOpacity_ShouldFail()
    {
        // Arrange
        var settings = new AppSettings
        {
            MainUi = new MainUiSettings
            {
                PanelOpacity = 0.95 // 95%（範囲外）
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage!.Contains("10%以上90%以下"));
    }

    #endregion

    #region キャプチャ間隔検証テスト

    [Fact]
    public void Validate_ValidCaptureInterval_ShouldSucceed()
    {
        // Arrange
        var settings = new AppSettings
        {
            Capture = new CaptureSettings
            {
                CaptureIntervalMs = 1000 // 1秒
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TooShortCaptureInterval_ShouldFail()
    {
        // Arrange
        var settings = new AppSettings
        {
            Capture = new CaptureSettings
            {
                CaptureIntervalMs = 50 // 50ms（範囲外）
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage!.Contains("100ms以上5000ms以下"));
    }

    [Fact]
    public void Validate_ShortCaptureInterval_ShouldSucceedWithWarning()
    {
        // Arrange
        var settings = new AppSettings
        {
            Capture = new CaptureSettings
            {
                CaptureIntervalMs = 300 // 300ms（有効だが警告対象）
            }
        };

        // Act
        var result = _validator.Validate(settings);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.WarningMessage!.Contains("CPU"));
    }

    #endregion

    #region カテゴリ別検証テスト

    [Fact]
    public void ValidateCategory_TranslationSettings_ShouldWork()
    {
        // Arrange
        var translationSettings = new TranslationSettings
        {
            DefaultEngine = TranslationEngine.Local,
            DefaultSourceLanguage = "ja",
            DefaultTargetLanguage = "en"
        };

        // Act
        var result = _validator.Validate("Translation", translationSettings);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Translation", result.Category);
    }

    [Fact]
    public void ValidateCategory_InvalidSettings_ShouldFail()
    {
        // Arrange
        var translationSettings = new TranslationSettings
        {
            DefaultEngine = TranslationEngine.Gemini // αテストでは非対応
        };

        // Act
        var result = _validator.Validate("Translation", translationSettings);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    #endregion

    #region ルール管理テスト

    [Fact]
    public void GetRules_ShouldReturnRegisteredRules()
    {
        // Act
        var rules = _validator.GetRules();

        // Assert
        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => r.PropertyPath == "DefaultEngine");
        Assert.Contains(rules, r => r.PropertyPath == "DefaultSourceLanguage");
    }

    [Fact]
    public void GetRules_ForCategory_ShouldReturnCategoryRules()
    {
        // Act
        var translationRules = _validator.GetRules("Translation");

        // Assert
        Assert.NotEmpty(translationRules);
        Assert.All(translationRules, rule => 
            Assert.True(rule.PropertyPath == "DefaultEngine" || rule.PropertyPath == "DefaultSourceLanguage"));
    }

    #endregion
}

/// <summary>
/// テスト用ロガー実装
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
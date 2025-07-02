using System;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// ChineseLanguageProcessor デバッグテスト
/// </summary>
public class ChineseLanguageProcessorDebugTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ChineseLanguageProcessor _processor;
    private bool _disposed;

    public ChineseLanguageProcessorDebugTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new DebugXunitLoggerProvider(output)));
        
        var logger = _loggerFactory.CreateLogger<ChineseLanguageProcessor>();
        _processor = new ChineseLanguageProcessor(logger);
    }

    [Fact]
    public void DebugChineseCharacterDetection()
    {
        // テストケースの詳細な文字分析
        var testCases = new[]
        {
            "國家很強大", // 繁体字専用文字を含む
            "国家很强大", // 簡体字専用文字を含む  
            "繁體中文測試", // 繁体字専用文字を含む
            "简体中文测试"  // 簡体字専用文字を含む
        };

        foreach (var testCase in testCases)
        {
            _output.WriteLine($"\n=== 分析: '{testCase}' ===");
            
            foreach (var ch in testCase)
            {
                var isChinese = ChineseLanguageProcessor.IsChineseCharacter(ch);
                var isSimplified = IsSimplifiedOnlyCharacterPublic(ch);
                var isTraditional = IsTraditionalOnlyCharacterPublic(ch);
                
                _output.WriteLine($"文字: '{ch}' (U+{((int)ch):X4})");
                _output.WriteLine($"  - 中国語文字: {isChinese}");
                _output.WriteLine($"  - 簡体字専用: {isSimplified}");
                _output.WriteLine($"  - 繁体字専用: {isTraditional}");
                _output.WriteLine("");
            }

            // 実際の判定結果
            var result = _processor.DetectScriptType(testCase);
            _output.WriteLine($"判定結果: {result}");
            _output.WriteLine("========================\n");
        }
    }

    [Theory]
    [InlineData("國家很強大", ChineseScriptType.Traditional)]
    [InlineData("国家很强大", ChineseScriptType.Simplified)]
    [InlineData("繁體中文測試", ChineseScriptType.Traditional)]
    [InlineData("简体中文测试", ChineseScriptType.Simplified)]
    public void DetectScriptType_ShouldReturnExpectedResult(string text, ChineseScriptType expected)
    {
        // Act
        var result = _processor.DetectScriptType(text);

        // Assert
        _output.WriteLine($"テキスト: '{text}'");
        _output.WriteLine($"期待値: {expected}");
        _output.WriteLine($"実際の値: {result}");
        
        Assert.Equal(expected, result);
    }

    // 簡体字専用文字判定の公開版（テスト用）
    private static bool IsSimplifiedOnlyCharacterPublic(char character)
    {
        var simplifiedOnlyChars = new[]
        {
            '国', '对', '会', '学', '说', '时', '过', '也', '现', '开',
            '内', '间', '年', '进', '实', '问', '变', '外', '头', '还',
            '发', '美', '达', '应', '长', '话', '众', '门', '见', '听',
            '强', // 「強」の簡体字
            '简', '体', '测', '试', // 繁体字: 簡體測試 -> 简体字: 简体测试
            '译', '单', '双', '节', '总', '级', '组', '织', '经', '济'
        };

        return Array.IndexOf(simplifiedOnlyChars, character) >= 0;
    }

    // 繁体字専用文字判定の公開版（テスト用）
    private static bool IsTraditionalOnlyCharacterPublic(char character)
    {
        var traditionalOnlyChars = new[]
        {
            '國', '對', '會', '學', '說', '時', '過', '現', '開', '內',
            '間', '進', '實', '問', '變', '外', '頭', '還', '發', '美',
            '達', '應', '長', '話', '眾', '門', '見', '聽', '標', '準',
            '強', // 「强」の繁体字
            '繁', '體', '測', '試', // 简体字: 繁体测试 -> 繁体字: 繁體測試
            '譯', '單', '雙', '節', '總', '級', '組', '織', '經', '濟'
        };

        return Array.IndexOf(traditionalOnlyChars, character) >= 0;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _loggerFactory?.Dispose();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Debug用のXunit ロガープロバイダー
/// </summary>
public sealed class DebugXunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;
    private bool _disposed;

    public DebugXunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new DebugXunitLogger(_output, categoryName);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// Debug用のXunit ロガー
/// </summary>
public sealed class DebugXunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public DebugXunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
#pragma warning disable CA1031 // テストログ出力では全ての例外をキャッチする必要がある
        catch
        {
            // テスト環境でのログ書き込みエラーは無視
        }
#pragma warning restore CA1031
    }
}

using System;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;

namespace Baketa.UI.Framework.Events;

/// <summary>
/// ウィンドウ選択要求イベント
/// </summary>
public class WindowSelectionRequestEvent : IEvent
{
    private readonly TaskCompletionSource<WindowInfo?> _completionSource = new();

    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(WindowSelectionRequestEvent);
    public string Category { get; } = "UI";

    public Task<WindowInfo?> GetResultAsync() => _completionSource.Task;

    public void SetResult(WindowInfo? selectedWindow)
    {
        _completionSource.SetResult(selectedWindow);
    }

    public void SetCancelled()
    {
        _completionSource.SetResult(null);
    }
}

/// <summary>
/// 言語確認要求イベント
/// </summary>
public class LanguageConfirmationRequestEvent : IEvent
{
    private readonly TaskCompletionSource<bool> _completionSource = new();

    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(LanguageConfirmationRequestEvent);
    public string Category { get; } = "UI";

    public Task<bool> GetResultAsync() => _completionSource.Task;

    public void SetResult(bool confirmed)
    {
        _completionSource.SetResult(confirmed);
    }
}

/// <summary>
/// 翻訳開始要求イベント
/// </summary>
public class StartTranslationRequestEvent(WindowInfo targetWindow) : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(StartTranslationRequestEvent);
    public string Category { get; } = "Translation";

    public WindowInfo TargetWindow { get; } = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));
}

// StopTranslationRequestEvent は Core.Events.EventTypes に移動

/// <summary>
/// 翻訳表示切り替え要求イベント
/// </summary>
public class ToggleTranslationDisplayRequestEvent(bool isVisible) : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(ToggleTranslationDisplayRequestEvent);
    public string Category { get; } = "UI";

    public bool IsVisible { get; } = isVisible;
}

/// <summary>
/// 設定画面表示要求イベント
/// </summary>
public class ShowSettingsRequestEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(ShowSettingsRequestEvent);
    public string Category { get; } = "UI";
}

/// <summary>
/// アプリケーション終了要求イベント
/// </summary>
public class ExitApplicationRequestEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(ExitApplicationRequestEvent);
    public string Category { get; } = "Application";
}

/// <summary>
/// 確認ダイアログ要求イベント
/// </summary>
public class ConfirmationRequestEvent(string message, string title = "確認") : IEvent
{
    private readonly TaskCompletionSource<bool> _completionSource = new();

    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(ConfirmationRequestEvent);
    public string Category { get; } = "UI";

    public string Message { get; } = message ?? throw new ArgumentNullException(nameof(message));
    public string Title { get; } = title ?? throw new ArgumentNullException(nameof(title));

    public Task<bool> GetResultAsync() => _completionSource.Task;

    public void SetResult(bool confirmed)
    {
        _completionSource.SetResult(confirmed);
    }
}

/// <summary>
/// 翻訳状態変更イベント
/// </summary>
public class TranslationStatusChangedEvent(TranslationStatus status) : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(TranslationStatusChangedEvent);
    public string Category { get; } = "Translation";

    public TranslationStatus Status { get; } = status;
}

/// <summary>
/// 翻訳表示可視性変更イベント
/// </summary>
public class TranslationDisplayVisibilityChangedEvent(bool isVisible) : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(TranslationDisplayVisibilityChangedEvent);
    public string Category { get; } = "UI";

    public bool IsVisible { get; } = isVisible;
}

// TranslationResultDisplayEvent は削除 - マルチウィンドウオーバーレイシステムに移行

/// <summary>
/// 設定変更イベント
/// </summary>
public class SettingsChangedEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(SettingsChangedEvent);
    public string Category { get; } = "Settings";

    public required bool UseLocalEngine { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required int FontSize { get; init; }
    public required double OverlayOpacity { get; init; }
}

/// <summary>
/// 設定画面閉じる要求イベント
/// </summary>
public class CloseSettingsRequestEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(CloseSettingsRequestEvent);
    public string Category { get; } = "UI";
}

/// <summary>
/// 設定読み込み要求イベント
/// </summary>
public class LoadSettingsRequestEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(LoadSettingsRequestEvent);
    public string Category { get; } = "Settings";
}


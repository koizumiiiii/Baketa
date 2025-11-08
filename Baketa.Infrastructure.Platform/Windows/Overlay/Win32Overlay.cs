using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlays;
using Baketa.Core.UI.Overlay;
using CoreGeometry = Baketa.Core.UI.Geometry;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Win32オーバーレイウィンドウをIOverlayインターフェースにアダプトするクラス
/// 既存のIOverlayWindow実装を新しい統一インターフェースにブリッジ
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32Overlay : IOverlay
{
    private readonly IOverlayWindow _overlayWindow;
    private bool _disposed;

    /// <summary>
    /// Win32Overlayの新しいインスタンスを初期化します
    /// </summary>
    /// <param name="overlayWindow">ラップするWin32オーバーレイウィンドウ</param>
    public Win32Overlay(IOverlayWindow overlayWindow)
    {
        _overlayWindow = overlayWindow ?? throw new ArgumentNullException(nameof(overlayWindow));

        // ウィンドウハンドルを一意識別子として使用
        Id = _overlayWindow.Handle.ToString();
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public bool IsVisible => _overlayWindow.IsVisible;

    /// <inheritdoc/>
    public OverlayPosition Position => new()
    {
        X = (int)_overlayWindow.Position.X,
        Y = (int)_overlayWindow.Position.Y,
        Width = (int)_overlayWindow.Size.Width,
        Height = (int)_overlayWindow.Size.Height,
        IsAbsolutePosition = true
    };

    /// <inheritdoc/>
    public OverlayContent Content
    {
        get
        {
            // 既存のIOverlayWindowはコンテンツ情報を保持していないため、
            // 最小限のプレースホルダーコンテンツを返す
            // 実際のコンテンツはWin32OverlayManager.ShowAsync()で設定される
            return new OverlayContent
            {
                Text = string.Empty // プレースホルダー
            };
        }
    }

    /// <inheritdoc/>
    public Task ShowAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // IOverlayWindow.Show()は同期メソッドのため、Taskでラップ
        _overlayWindow.Show();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task HideAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // IOverlayWindow.Hide()は同期メソッドのため、Taskでラップ
        _overlayWindow.Hide();
        return Task.CompletedTask;
    }

    /// <summary>
    /// ラップされているWin32オーバーレイウィンドウのハンドルを取得
    /// </summary>
    internal nint Handle => _overlayWindow.Handle;

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _overlayWindow?.Dispose();
        _disposed = true;
    }
}

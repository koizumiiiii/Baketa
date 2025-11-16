using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Baketa.Core.Abstractions.UI.Overlays;
using Baketa.Core.UI.Overlay;
using CoreGeometry = Baketa.Core.UI.Geometry;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// ILayeredOverlayWindow を IOverlayWindow インターフェースに適応させるアダプター
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LayeredOverlayWindowAdapter : IOverlayWindow
{
    private readonly ILayeredOverlayWindow _layeredWindow;
    private readonly List<CoreGeometry.Rect> _hitTestAreas = [];
    private CoreGeometry.Size _size;
    private CoreGeometry.Point _position;
    private nint _targetWindowHandle;
    private bool _disposed;

    public LayeredOverlayWindowAdapter(ILayeredOverlayWindow layeredWindow)
    {
        _layeredWindow = layeredWindow ?? throw new ArgumentNullException(nameof(layeredWindow));
    }

    // === IOverlayWindow プロパティ実装 ===

    public bool IsVisible => _layeredWindow.IsVisible;

    public nint Handle => _layeredWindow.WindowHandle;

    public double Opacity => 0.9; // 固定値（LayeredOverlayWindowはOpacity調整機能を持たない）

    public bool IsClickThrough { get; set; } = true; // デフォルトでクリックスルー有効

    public IReadOnlyList<CoreGeometry.Rect> HitTestAreas => _hitTestAreas.AsReadOnly();

    public CoreGeometry.Point Position
    {
        get => _position;
        set
        {
            _position = value;
            _layeredWindow.SetPosition((int)value.X, (int)value.Y);
        }
    }

    public CoreGeometry.Size Size
    {
        get => _size;
        set
        {
            _size = value;
            _layeredWindow.SetSize((int)value.Width, (int)value.Height);
        }
    }

    public nint TargetWindowHandle
    {
        get => _targetWindowHandle;
        set => _targetWindowHandle = value;
    }

    // === IOverlayWindow メソッド実装 ===

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _layeredWindow.Show();
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _layeredWindow.Hide();
    }

    public void AddHitTestArea(CoreGeometry.Rect area)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hitTestAreas.Add(area);
    }

    public void RemoveHitTestArea(CoreGeometry.Rect area)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hitTestAreas.Remove(area);
    }

    public void ClearHitTestAreas()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hitTestAreas.Clear();
    }

    public void UpdateContent(object? content = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // contentがOverlayContentの場合
        if (content is OverlayContent overlayContent)
        {
            // フォントサイズが指定されている場合は設定
            if (overlayContent.FontSize.HasValue)
            {
                _layeredWindow.SetFontSize((float)overlayContent.FontSize.Value);
            }

            // テキストを設定
            _layeredWindow.SetText(overlayContent.Text);
        }
        // contentが文字列の場合はSetTextを使用（後方互換性）
        else if (content is string text)
        {
            _layeredWindow.SetText(text);
        }
        // それ以外の場合は何もしない
    }

    public void AdjustToTargetWindow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // LayeredOverlayWindowは自動調整機能を持たないため、何もしない
    }

    public void Close()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _layeredWindow.Close();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _layeredWindow?.Dispose();
            _disposed = true;
        }
    }
}

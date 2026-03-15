using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Baketa.Core.Models.Roi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #449] OCR対象領域の手動選択ウィンドウ
/// mac風のダークマスク + ラバーバンド選択 + リサイズハンドル
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private enum DragMode
    {
        None,
        Creating,    // 新規矩形をドラッグで作成中
        Moving,      // 既存矩形を移動中
        ResizeN, ResizeS, ResizeE, ResizeW,
        ResizeNE, ResizeNW, ResizeSE, ResizeSW
    }

    // ダークマスク（選択領域外を暗くする4つの矩形）
    private readonly Rectangle _maskTop = new() { Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
    private readonly Rectangle _maskBottom = new() { Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
    private readonly Rectangle _maskLeft = new() { Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
    private readonly Rectangle _maskRight = new() { Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };

    // 選択領域の枠線（点線）
    private readonly Rectangle _selectionBorder = new()
    {
        Stroke = new SolidColorBrush(Colors.White),
        StrokeThickness = 1.5,
        StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 6, 4 },
        Fill = Brushes.Transparent
    };

    // リサイズハンドル（8箇所: 4角 + 4辺中央）
    private readonly List<Rectangle> _handles = [];
    private const double HandleSize = 8;
    private static readonly IBrush HandleBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush HandleStroke = new SolidColorBrush(Color.FromArgb(200, 0, 120, 255));

    // サイズ表示テキスト
    private readonly TextBlock _sizeLabel = new()
    {
        Foreground = Brushes.White,
        FontSize = 12,
        Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
        Padding = new Thickness(6, 2)
    };

    // ツールバー（確定・キャンセルボタン）
    private readonly Border _toolbar = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(220, 40, 40, 40)),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(4),
        IsVisible = false
    };

    // ドラッグ状態
    private DragMode _dragMode = DragMode.None;
    private Point _dragStart;
    private Rect _selectionRect;
    private Rect _dragStartRect; // 移動/リサイズ開始時の元矩形

    // ログ
    private readonly ILogger<RegionSelectionWindow>? _logger;

    // 結果
    private bool _confirmed;

    /// <summary>
    /// 選択結果の正規化座標。nullはキャンセル。
    /// </summary>
    public NormalizedRect? SelectedRegion { get; private set; }

    public RegionSelectionWindow()
    {
        _logger = Program.ServiceProvider?.GetService<ILogger<RegionSelectionWindow>>();
        InitializeComponent();
        InitializeVisuals();
    }

    /// <summary>
    /// 初期選択領域を設定（既存選択の確認・微調整用）
    /// </summary>
    public void SetInitialSelection(NormalizedRect region)
    {
        _selectionRect = new Rect(
            region.X * Width,
            region.Y * Height,
            region.Width * Width,
            region.Height * Height);
    }

    private void InitializeVisuals()
    {
        var canvas = this.FindControl<Canvas>("SelectionCanvas")!;

        // ダークマスク追加
        canvas.Children.Add(_maskTop);
        canvas.Children.Add(_maskBottom);
        canvas.Children.Add(_maskLeft);
        canvas.Children.Add(_maskRight);

        // 選択枠追加
        canvas.Children.Add(_selectionBorder);

        // リサイズハンドル（8個）作成
        for (int i = 0; i < 8; i++)
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = HandleBrush,
                Stroke = HandleStroke,
                StrokeThickness = 1,
                IsVisible = false
            };
            _handles.Add(handle);
            canvas.Children.Add(handle);
        }

        // サイズ表示
        canvas.Children.Add(_sizeLabel);
        _sizeLabel.IsVisible = false;

        // ツールバー（確定・キャンセルボタン）
        var toolbarPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };

        var confirmButton = new Button
        {
            Content = "✓",
            Width = 36, Height = 28,
            FontSize = 16, FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 140, 60)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        confirmButton.Click += (_, _) => ConfirmSelection();

        var cancelButton = new Button
        {
            Content = "✕",
            Width = 36, Height = 28,
            FontSize = 14,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 180, 40, 40)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        cancelButton.Click += (_, _) => { SelectedRegion = null; Close(); };

        toolbarPanel.Children.Add(confirmButton);
        toolbarPanel.Children.Add(cancelButton);
        _toolbar.Child = toolbarPanel;
        canvas.Children.Add(_toolbar);

        // 初期状態：全面ダークマスク
        UpdateVisuals();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 初期選択がなければデフォルト領域（画面中央、幅50%・高さ40%）を設定
        if (_selectionRect.Width <= 0 || _selectionRect.Height <= 0)
        {
            var actualW = Bounds.Width > 0 ? Bounds.Width : Width;
            var actualH = Bounds.Height > 0 ? Bounds.Height : Height;
            var w = actualW * 0.5;
            var h = actualH * 0.4;
            var x = (actualW - w) / 2;
            var y = (actualH - h) / 2;
            _selectionRect = new Rect(x, y, w, h);

            _logger?.LogDebug("[Issue #449] RegionSelection 初期領域: X={X:F0}, Y={Y:F0}, W={W:F0}, H={H:F0}",
                x, y, w, h);
        }

        UpdateVisuals();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_dragMode == DragMode.None)
        {
            // カーソル形状更新
            UpdateCursor(pos);
            return;
        }

        switch (_dragMode)
        {
            case DragMode.Creating:
                _selectionRect = NormalizeRect(_dragStart, pos);
                break;
            case DragMode.Moving:
                var dx = pos.X - _dragStart.X;
                var dy = pos.Y - _dragStart.Y;
                _selectionRect = ClampToWindow(new Rect(
                    _dragStartRect.X + dx, _dragStartRect.Y + dy,
                    _dragStartRect.Width, _dragStartRect.Height));
                break;
            default:
                _selectionRect = ResizeRect(pos);
                break;
        }

        UpdateVisuals();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragMode == DragMode.Creating && _selectionRect.Width < 5 && _selectionRect.Height < 5)
        {
            // クリックだけで矩形ができなかった場合は無視
            _selectionRect = default;
        }

        _dragMode = DragMode.None;
        UpdateVisuals();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                _confirmed = false;
                SelectedRegion = null;
                Close();
                break;
            case Key.Enter:
                ConfirmSelection();
                break;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var pos = e.GetPosition(this);

        // ツールバー領域のクリックはドラッグ開始しない
        if (_toolbar.IsVisible && _toolbar.Bounds.Contains(
            new Point(pos.X - Canvas.GetLeft(_toolbar), pos.Y - Canvas.GetTop(_toolbar))))
            return;

        // 既存選択領域がある場合、ハンドル or 領域内の判定
        if (_selectionRect is { Width: > 0, Height: > 0 })
        {
            var mode = HitTest(pos);
            if (mode != DragMode.None)
            {
                _dragMode = mode;
                _dragStart = pos;
                _dragStartRect = _selectionRect;
                e.Handled = true;
                return;
            }
        }

        // 新規選択開始
        _dragMode = DragMode.Creating;
        _dragStart = pos;
        _selectionRect = new Rect(pos.X, pos.Y, 0, 0);
        e.Handled = true;
    }

    private void ConfirmSelection()
    {
        if (_selectionRect is { Width: > 10, Height: > 10 })
        {
            _confirmed = true;
            SelectedRegion = new NormalizedRect(
                (float)(_selectionRect.X / Width),
                (float)(_selectionRect.Y / Height),
                (float)(_selectionRect.Width / Width),
                (float)(_selectionRect.Height / Height));
        }
        Close();
    }

    private DragMode HitTest(Point pos)
    {
        var r = _selectionRect;
        const double margin = 6;

        var onLeft = Math.Abs(pos.X - r.X) < margin;
        var onRight = Math.Abs(pos.X - r.Right) < margin;
        var onTop = Math.Abs(pos.Y - r.Y) < margin;
        var onBottom = Math.Abs(pos.Y - r.Bottom) < margin;

        if (onTop && onLeft) return DragMode.ResizeNW;
        if (onTop && onRight) return DragMode.ResizeNE;
        if (onBottom && onLeft) return DragMode.ResizeSW;
        if (onBottom && onRight) return DragMode.ResizeSE;
        if (onTop) return DragMode.ResizeN;
        if (onBottom) return DragMode.ResizeS;
        if (onLeft) return DragMode.ResizeW;
        if (onRight) return DragMode.ResizeE;

        if (r.Contains(pos)) return DragMode.Moving;

        return DragMode.None;
    }

    private Rect ResizeRect(Point pos)
    {
        var r = _dragStartRect;
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        return _dragMode switch
        {
            DragMode.ResizeN => ClampToWindow(new Rect(r.X, r.Y + dy, r.Width, r.Height - dy)),
            DragMode.ResizeS => ClampToWindow(new Rect(r.X, r.Y, r.Width, r.Height + dy)),
            DragMode.ResizeW => ClampToWindow(new Rect(r.X + dx, r.Y, r.Width - dx, r.Height)),
            DragMode.ResizeE => ClampToWindow(new Rect(r.X, r.Y, r.Width + dx, r.Height)),
            DragMode.ResizeNW => ClampToWindow(new Rect(r.X + dx, r.Y + dy, r.Width - dx, r.Height - dy)),
            DragMode.ResizeNE => ClampToWindow(new Rect(r.X, r.Y + dy, r.Width + dx, r.Height - dy)),
            DragMode.ResizeSW => ClampToWindow(new Rect(r.X + dx, r.Y, r.Width - dx, r.Height + dy)),
            DragMode.ResizeSE => ClampToWindow(new Rect(r.X, r.Y, r.Width + dx, r.Height + dy)),
            _ => r
        };
    }

    private void UpdateCursor(Point pos)
    {
        if (_selectionRect is not { Width: > 0, Height: > 0 })
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            return;
        }

        Cursor = HitTest(pos) switch
        {
            DragMode.ResizeN or DragMode.ResizeS => new Cursor(StandardCursorType.SizeNorthSouth),
            DragMode.ResizeE or DragMode.ResizeW => new Cursor(StandardCursorType.SizeWestEast),
            DragMode.ResizeNW or DragMode.ResizeSE => new Cursor(StandardCursorType.TopLeftCorner),
            DragMode.ResizeNE or DragMode.ResizeSW => new Cursor(StandardCursorType.TopRightCorner),
            DragMode.Moving => new Cursor(StandardCursorType.SizeAll),
            _ => new Cursor(StandardCursorType.Cross)
        };
    }

    private void UpdateVisuals()
    {
        var w = Width;
        var h = Height;
        var r = _selectionRect;
        var hasSelection = r is { Width: > 0, Height: > 0 };

        // ダークマスク更新（選択領域を除いた4つの矩形）
        if (hasSelection)
        {
            SetRect(_maskTop, 0, 0, w, r.Y);
            SetRect(_maskBottom, 0, r.Bottom, w, h - r.Bottom);
            SetRect(_maskLeft, 0, r.Y, r.X, r.Height);
            SetRect(_maskRight, r.Right, r.Y, w - r.Right, r.Height);
        }
        else
        {
            // 選択なし → 全面マスク
            SetRect(_maskTop, 0, 0, w, h);
            SetRect(_maskBottom, 0, 0, 0, 0);
            SetRect(_maskLeft, 0, 0, 0, 0);
            SetRect(_maskRight, 0, 0, 0, 0);
        }

        // 選択枠
        _selectionBorder.IsVisible = hasSelection;
        if (hasSelection)
        {
            SetRect(_selectionBorder, r.X, r.Y, r.Width, r.Height);
        }

        // リサイズハンドル
        if (hasSelection && _dragMode == DragMode.None)
        {
            var cx = r.X + r.Width / 2;
            var cy = r.Y + r.Height / 2;
            var hs = HandleSize / 2;

            PositionHandle(0, r.X - hs, r.Y - hs);           // NW
            PositionHandle(1, cx - hs, r.Y - hs);             // N
            PositionHandle(2, r.Right - hs, r.Y - hs);        // NE
            PositionHandle(3, r.Right - hs, cy - hs);          // E
            PositionHandle(4, r.Right - hs, r.Bottom - hs);    // SE
            PositionHandle(5, cx - hs, r.Bottom - hs);         // S
            PositionHandle(6, r.X - hs, r.Bottom - hs);       // SW
            PositionHandle(7, r.X - hs, cy - hs);              // W
        }
        else
        {
            foreach (var handle in _handles)
                handle.IsVisible = false;
        }

        // サイズ表示
        var showInfo = hasSelection && _dragMode == DragMode.None;
        _sizeLabel.IsVisible = hasSelection;
        if (hasSelection)
        {
            _sizeLabel.Text = $"{(int)r.Width} x {(int)r.Height}";
            Canvas.SetLeft(_sizeLabel, r.X + (r.Width - _sizeLabel.Bounds.Width) / 2);
            Canvas.SetTop(_sizeLabel, r.Bottom + 4);
        }

        // ツールバー（確定・キャンセル）
        _toolbar.IsVisible = showInfo;
        if (showInfo)
        {
            // サイズ表示の下に配置
            var toolbarWidth = _toolbar.Bounds.Width > 0 ? _toolbar.Bounds.Width : 84;
            Canvas.SetLeft(_toolbar, r.X + (r.Width - toolbarWidth) / 2);
            Canvas.SetTop(_toolbar, r.Bottom + 24);
        }
    }

    private void PositionHandle(int index, double x, double y)
    {
        var handle = _handles[index];
        handle.IsVisible = true;
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private static void SetRect(Rectangle rect, double x, double y, double width, double height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = width;
        rect.Height = height;
    }

    private static Rect NormalizeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rect(x, y, w, h);
    }

    private Rect ClampToWindow(Rect rect)
    {
        var x = Math.Max(0, Math.Min(rect.X, Width - rect.Width));
        var y = Math.Max(0, Math.Min(rect.Y, Height - rect.Height));
        var w = Math.Min(rect.Width, Width);
        var h = Math.Min(rect.Height, Height);
        return new Rect(x, y, Math.Max(10, w), Math.Max(10, h));
    }
}

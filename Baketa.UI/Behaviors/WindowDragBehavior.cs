using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace Baketa.UI.Behaviors;

/// <summary>
/// ウィンドウをドラッグで移動可能にするビヘイビア
/// </summary>
/// <remarks>
/// Issue #239: UI/UX改善
/// タイトルバーのない透明ウィンドウでのドラッグ移動を実現します。
///
/// 使用例:
/// <code>
/// &lt;Border&gt;
///   &lt;Interaction.Behaviors&gt;
///     &lt;behaviors:WindowDragBehavior /&gt;
///   &lt;/Interaction.Behaviors&gt;
/// &lt;/Border&gt;
/// </code>
/// </remarks>
public class WindowDragBehavior : Behavior<Control>
{
    /// <summary>
    /// ドラッグ開始に必要なマウスボタン（デフォルト：左クリック）
    /// </summary>
    public static readonly StyledProperty<MouseButton> DragButtonProperty =
        AvaloniaProperty.Register<WindowDragBehavior, MouseButton>(
            nameof(DragButton),
            MouseButton.Left);

    /// <summary>
    /// ドラッグ開始に必要なマウスボタン
    /// </summary>
    public MouseButton DragButton
    {
        get => GetValue(DragButtonProperty);
        set => SetValue(DragButtonProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is not null)
        {
            AssociatedObject.PointerPressed += OnPointerPressed;
        }
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.PointerPressed -= OnPointerPressed;
        }

        base.OnDetaching();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject is null)
        {
            return;
        }

        // 指定されたマウスボタンが押されているか確認
        var properties = e.GetCurrentPoint(AssociatedObject).Properties;
        var isCorrectButton = DragButton switch
        {
            MouseButton.Left => properties.IsLeftButtonPressed,
            MouseButton.Right => properties.IsRightButtonPressed,
            MouseButton.Middle => properties.IsMiddleButtonPressed,
            _ => properties.IsLeftButtonPressed
        };

        if (!isCorrectButton)
        {
            return;
        }

        // 親ウィンドウを取得してドラッグ開始
        var window = TopLevel.GetTopLevel(AssociatedObject) as Window;
        window?.BeginMoveDrag(e);
    }
}

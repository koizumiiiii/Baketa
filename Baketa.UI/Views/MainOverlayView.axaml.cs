using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Baketa.Core.Settings;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Views;

public partial class MainOverlayView : Window
{
    // #246: 位置永続化用の設定ファイル名
    private static readonly string OverlayPositionFilePath = Path.Combine(
        BaketaSettingsPaths.UserSettingsDirectory,
        "overlay-position.json");

    // 位置保存のデバウンス用（ドラッグ中の頻繁な保存を防ぐ）
    private DateTime _lastPositionSave = DateTime.MinValue;
    private static readonly TimeSpan PositionSaveDebounce = TimeSpan.FromMilliseconds(500);

    // ログ統一: ILoggerを使用
    private readonly ILogger<MainOverlayView>? _logger;

    public MainOverlayView()
    {
        // ILoggerをServiceProviderから取得
        _logger = Program.ServiceProvider?.GetService<ILogger<MainOverlayView>>();

        _logger?.LogDebug("MainOverlayView初期化開始");

        InitializeComponent();

        _logger?.LogDebug("MainOverlayView - InitializeComponent完了");

        // 保存された位置を復元、または画面左端から16px、縦中央に配置
        ConfigurePosition();

        // #246: 位置変更イベントを購読して位置を永続化
        PositionChanged += OnPositionChanged;

        // 可視性確認
        _logger?.LogDebug("MainOverlayView - IsVisible: {IsVisible}, WindowState: {WindowState}", IsVisible, WindowState);
    }

    private void ConfigurePosition()
    {
        // #246: 保存された位置を復元
        var savedPosition = LoadSavedPosition();
        if (savedPosition.HasValue)
        {
            // 保存位置が有効なモニター内かを検証
            if (IsPositionOnValidScreen(savedPosition.Value))
            {
                Position = savedPosition.Value;
                _logger?.LogDebug("MainOverlayView - 保存位置を復元: {Position}", Position);
                return;
            }
            _logger?.LogDebug("MainOverlayView - 保存位置がモニター外のため無視: {SavedPosition}", savedPosition.Value);
        }

        // 保存位置がない場合、または無効な場合はデフォルト位置を使用
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            var windowHeight = 380; // 展開時の高さ値を使用（Exitボタンを含む）

            // X座標: 画面左端から16px
            var x = 16;

            // Y座標: 画面縦中央（オーバーレイ中央が画面中央に来るよう配置）
            var y = (bounds.Height - windowHeight) / 2;

            Position = new Avalonia.PixelPoint(x, (int)y);
        }
    }

    /// <summary>
    /// #246: 位置変更時に保存（デバウンス付き）
    /// </summary>
    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // デバウンス：頻繁な保存を防ぐ
        var now = DateTime.UtcNow;
        if (now - _lastPositionSave < PositionSaveDebounce)
        {
            return;
        }
        _lastPositionSave = now;

        SavePosition(e.Point);
    }

    /// <summary>
    /// #246: 位置をファイルに保存
    /// </summary>
    private void SavePosition(Avalonia.PixelPoint position)
    {
        try
        {
            BaketaSettingsPaths.EnsureUserSettingsDirectoryExists();

            var positionData = new OverlayPositionData
            {
                X = position.X,
                Y = position.Y,
                SavedAt = DateTime.UtcNow.ToString("O")
            };

            var json = JsonSerializer.Serialize(positionData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(OverlayPositionFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "オーバーレイ位置保存エラー");
        }
    }

    /// <summary>
    /// #246: 保存された位置を読み込み
    /// </summary>
    private Avalonia.PixelPoint? LoadSavedPosition()
    {
        try
        {
            if (!File.Exists(OverlayPositionFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(OverlayPositionFilePath);
            var positionData = JsonSerializer.Deserialize<OverlayPositionData>(json);

            if (positionData != null)
            {
                return new Avalonia.PixelPoint(positionData.X, positionData.Y);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "オーバーレイ位置読み込みエラー");
        }

        return null;
    }

    /// <summary>
    /// #246: 位置が有効なスクリーン上にあるか確認
    /// </summary>
    private bool IsPositionOnValidScreen(Avalonia.PixelPoint position)
    {
        foreach (var screen in Screens.All)
        {
            var bounds = screen.Bounds;
            if (position.X >= bounds.X && position.X < bounds.X + bounds.Width &&
                position.Y >= bounds.Y && position.Y < bounds.Y + bounds.Height)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// #246: オーバーレイ位置データ
    /// </summary>
    private sealed class OverlayPositionData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string? SavedAt { get; set; }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        _logger?.LogDebug("MainOverlayView - OnLoaded呼び出し");

        base.OnLoaded(e);

        // 位置を再設定（画面解像度が変わった可能性があるため）
        ConfigurePosition();

        // StartStopボタンのCommand/DataContext確認
        try
        {
            var startStopButton = this.FindControl<Button>("StartStopButton");
            if (startStopButton != null)
            {
                _logger?.LogDebug("StartStopButton発見 - Command: {HasCommand}, IsEnabled: {IsEnabled}, DataContext: {HasDataContext}",
                    startStopButton.Command != null, startStopButton.IsEnabled, startStopButton.DataContext != null);

                if (DataContext is MainOverlayViewModel viewModel)
                {
                    _logger?.LogDebug("ViewModel確認 - IsStartStopEnabled: {IsStartStopEnabled}, IsTranslationActive: {IsTranslationActive}",
                        viewModel.IsStartStopEnabled, viewModel.IsTranslationActive);
                }
            }
            else
            {
                _logger?.LogWarning("StartStopButtonが見つかりません");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ボタン検証エラー");
        }

        // ウィンドウの状態確認
        _logger?.LogDebug("MainOverlayView - OnLoaded後: IsVisible={IsVisible}, IsEnabled={IsEnabled}, Position={Position}",
            IsVisible, IsEnabled, Position);

        // ウィンドウを前面に表示
        try
        {
            Show();
            Activate();
            _logger?.LogDebug("MainOverlayView - Show()とActivate()を実行");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MainOverlayView - Show/Activate失敗");
        }
    }


    private void OnExitButtonClick(object? sender, RoutedEventArgs e)
    {
        _logger?.LogInformation("ExitButtonClick呼び出し");

        try
        {
            // アプリケーション終了
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                _logger?.LogInformation("アプリケーション終了を実行");
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "アプリケーション終了エラー");
        }
    }

    /// <summary>
    /// StartStopボタンの物理的クリック検出（診断用）
    /// </summary>
    private void StartStopButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var button = sender as Button;
        var viewModel = DataContext as MainOverlayViewModel;

        _logger?.LogDebug("StartStopButton物理的クリック検出 - IsEnabled: {IsEnabled}, HasCommand: {HasCommand}, IsTranslationActive: {IsTranslationActive}, IsStartStopEnabled: {IsStartStopEnabled}",
            button?.IsEnabled, button?.Command != null, viewModel?.IsTranslationActive, viewModel?.IsStartStopEnabled);
    }

    /// <summary>
    /// ドラッグハンドルのPointerPressedイベントハンドラ (#246)
    /// BeginMoveDragを使用してウィンドウのネイティブドラッグ移動を開始
    /// </summary>
    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 左クリックのみ処理
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

using Avalonia.Controls;
using Baketa.UI.Utils;
using System;

namespace Baketa.UI.Views;

public partial class LoadingOverlayView : Window
{
    public LoadingOverlayView()
    {
        Console.WriteLine("🔄 LoadingOverlayView初期化開始");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 LoadingOverlayView初期化開始");
        
        InitializeComponent();
        
        // ウィンドウの設定
        this.IsHitTestVisible = false; // クリック無効化
        this.ShowActivated = false;     // フォーカス取得無効化
        
        Console.WriteLine("✅ LoadingOverlayView初期化完了");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ LoadingOverlayView初期化完了");
    }
}
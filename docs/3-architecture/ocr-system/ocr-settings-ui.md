# OCR設定UI設計ドキュメント

## 1. 概要

このドキュメントでは、Baketaプロジェクトにおける OCR設定画面のユーザーインターフェース設計について詳述します。本設計は、ユーザー操作を最小限に抑え、自動最適化を中心とした方針に基づいています。OpenCVベースの最適化アプローチにより、ユーザーが複雑な設定に悩まされることなく高品質なOCR機能を利用できることを目指します。

## 2. 設計目標と原則

### 2.1 主要設計目標

1. **シンプルさの最大化**: ユーザーが直面する複雑さを最小限に抑える
2. **自動化の優先**: 手動設定よりも自動最適化を優先
3. **透明性の確保**: システムの状態と動作を明確に伝える
4. **必要最小限の制御**: 必須の機能のみをシンプルに提示

### 2.2 設計原則

- **デフォルトで自動**: すべての設定は初期状態で自動最適化が有効
- **状態の可視化**: 現在の処理状態を明確に表示
- **開発者機能の隔離**: 開発・テスト用機能は明確に分離
- **トラブルシューティングの容易さ**: 問題発生時の解決策を簡単に見つけられる

## 3. UI構造と画面レイアウト

OCR設定画面は以下の主要セクションで構成されます：

1. **基本制御エリア**: 自動/手動モード切替と状態表示
2. **トラブルシューティング**: 折りたたみ可能なトラブルシューティングオプション
3. **開発者オプション**: 折りたたみ可能な開発者向け機能

### 3.1 レイアウト概要

```
+---------------------------------------------------------+
|  OCR設定                                                |
|  システムが自動的にOCR処理を最適化します...               |
|                                                         |
|  +-----------------------------------------------------+
|  |  自動最適化: 有効  [トグルスイッチ]                   |
|  |  注意: 手動モードは開発・テスト用です...              |
|  |                                                     |
|  |  OCR状態: アイドル中                                |
|  |  最終認識: 23秒前                                   |
|  +-----------------------------------------------------+
|                                                         |
|  ▼ トラブルシューティング                               |
|  |  [OCRテスト実行]                                    |
|  |  [設定をリセット]                                   |
|  |  [問題を報告]                                       |
|                                                         |
|  ▼ 開発者オプション                                     |
|  |  □ 詳細ログを有効化                                 |
|  |  [診断情報を出力]                                   |
|  |  [開発者モードを開く]                               |
|                                                         |
+---------------------------------------------------------+
```

## 4. UI要素の詳細説明

### 4.1 基本制御エリア

#### 自動最適化トグルスイッチ
- **目的**: 自動最適化モードと手動モードの切り替え
- **デフォルト状態**: 有効（オン）
- **ラベル**: 「自動最適化: 有効/無効」
- **注記**: 「注意: 手動モードは開発・テスト用です。通常使用では自動モードをお勧めします。」
- **動作**: トグルオフ時は開発者モードを部分的に表示

#### 状態表示
- **OCR状態**: 現在のOCR処理状態を表示
  - 可能な値: 「アイドル中」「スキャン中」「分析中」「最適化中」「エラー」
  - 状態に応じて色分け（緑: 正常、黄: 処理中、赤: エラー）
- **最終認識**: 最後にテキストを認識した時間を表示
  - 形式: 「X秒前」「X分前」「処理なし」

### 4.2 トラブルシューティングセクション

折りたたみ可能なセクションで、デフォルトでは折りたたまれた状態。

#### OCRテスト実行ボタン
- **目的**: 現在の画面に対してテストOCR実行
- **動作**: クリック時、現在の画面をキャプチャしてOCR処理を実行、結果をダイアログ表示

#### 設定リセットボタン
- **目的**: 現在のゲームプロファイルの設定をリセット
- **動作**: 確認ダイアログ表示後、現在のゲームプロファイルを初期状態に戻す

#### 問題報告ボタン
- **目的**: OCR問題の報告と自動診断
- **動作**: スクリーンショットを取得し、診断情報と合わせて開発者に送信（ユーザー許可制）

### 4.3 開発者オプションセクション

折りたたみ可能なセクションで、デフォルトでは折りたたまれた状態。

#### 詳細ログ有効化チェックボックス
- **目的**: 詳細なOCRログ出力の有効化
- **デフォルト**: 無効
- **動作**: 有効時、OCR処理の詳細ログをファイルに出力

#### 診断情報出力ボタン
- **目的**: 現在のOCR設定と診断情報をファイルに出力
- **動作**: OCR設定、ゲームプロファイル、パフォーマンスメトリクスなどをJSONファイルにエクスポート

#### 開発者モードボタン
- **目的**: 高度な設定と診断ツールを提供する開発者モードを開く
- **動作**: 開発者向けの詳細設定・診断ウィンドウを開く

## 5. ユーザーフロー

### 5.1 通常使用フロー

1. ユーザーはアプリケーションを起動
2. システムが自動的にゲームを検出し、適切なプロファイルを適用
3. 自動最適化が実行され、最適なOCR設定が適用される
4. ユーザーはOCR機能を使用（特別な操作不要）
5. OCR状態と最終認識時間がリアルタイムで更新される

### 5.2 問題解決フロー

1. OCRが正常に機能しない場合、ユーザーは「トラブルシューティング」セクションを展開
2. 「OCRテスト実行」ボタンで現在の認識状態を確認
3. 問題が継続する場合、「設定をリセット」で初期状態に戻す
4. それでも解決しない場合、「問題を報告」ボタンで開発者に情報を送信

### 5.3 開発者フロー

1. 開発者は「開発者オプション」セクションを展開
2. 「詳細ログ」を有効化して処理の詳細を記録
3. 「診断情報を出力」でシステム状態を詳細に調査
4. 「開発者モード」を開いて高度な設定と診断ツールにアクセス

## 6. 実装ガイドライン

### 6.1 Avalonia UI実装

```xml
<TabItem Header="OCR設定">
  <StackPanel Margin="20">
    <TextBlock Classes="h3" Text="OCR設定" />
    <TextBlock Text="システムが自動的にOCR処理を最適化します。通常は設定変更の必要はありません。"
               TextWrapping="Wrap" Opacity="0.7" Margin="0,5,0,20" />
    
    <!-- 主要コントロール領域 -->
    <StackPanel Background="#F5F5F5" Padding="15" CornerRadius="5">
      <!-- 自動/手動モード切替 - テスト用であることを明記 -->
      <StackPanel>
        <ToggleSwitch IsChecked="{Binding IsAutoOptimizationEnabled}"
                     OnContent="自動最適化: 有効" OffContent="自動最適化: 無効"
                     Margin="0,0,0,5" />
        <TextBlock Text="注意: 手動モードは開発・テスト用です。通常使用では自動モードをお勧めします。"
                  FontStyle="Italic" Opacity="0.7" FontSize="11" 
                  TextWrapping="Wrap" Margin="25,0,0,10" />
      </StackPanel>
      
      <!-- 処理状態インジケーター -->
      <StackPanel Orientation="Horizontal" Margin="0,10,0,5">
        <TextBlock Text="OCR状態:" Width="80" VerticalAlignment="Center" />
        <TextBlock Text="{Binding OcrStatusText}" Foreground="{Binding OcrStatusColor}" />
      </StackPanel>
      
      <!-- 最終認識時間 -->
      <StackPanel Orientation="Horizontal" Margin="0,5,0,5">
        <TextBlock Text="最終認識:" Width="80" VerticalAlignment="Center" />
        <TextBlock Text="{Binding LastRecognitionTime}" />
      </StackPanel>
    </StackPanel>
    
    <!-- トラブルシューティング領域 -->
    <Expander Header="トラブルシューティング" IsExpanded="False" Margin="0,20,0,0">
      <StackPanel Margin="10">
        <Button Content="OCRテスト実行" 
                Command="{Binding RunOcrTestCommand}"
                HorizontalAlignment="Left" Margin="0,5" />
        <Button Content="設定をリセット" 
                Command="{Binding ResetOcrSettingsCommand}"
                HorizontalAlignment="Left" Margin="0,5" />
        <Button Content="問題を報告" 
                Command="{Binding ReportOcrIssueCommand}"
                HorizontalAlignment="Left" Margin="0,5" />
      </StackPanel>
    </Expander>
    
    <!-- 開発者オプション - 折りたたまれた状態がデフォルト -->
    <Expander Header="開発者オプション" IsExpanded="False" Margin="0,10,0,0">
      <StackPanel Margin="10">
        <CheckBox Content="詳細ログを有効化" 
                 IsChecked="{Binding IsVerboseLoggingEnabled}"
                 Margin="0,5" />
        <Button Content="診断情報を出力" 
                Command="{Binding ExportDiagnosticsCommand}"
                HorizontalAlignment="Left" Margin="0,5" />
        <Button Content="開発者モードを開く"
                Command="{Binding OpenDeveloperModeCommand}"
                HorizontalAlignment="Left" Margin="0,5" />
      </StackPanel>
    </Expander>
  </StackPanel>
</TabItem>
```

### 6.2 ViewModel構造

```csharp
public class OcrSettingsViewModel : ViewModelBase
{
    // 自動最適化有効フラグ
    private bool _isAutoOptimizationEnabled = true;
    public bool IsAutoOptimizationEnabled
    {
        get => _isAutoOptimizationEnabled;
        set => this.RaiseAndSetIfChanged(ref _isAutoOptimizationEnabled, value);
    }
    
    // OCR状態
    private string _ocrStatusText = "アイドル中";
    public string OcrStatusText
    {
        get => _ocrStatusText;
        set => this.RaiseAndSetIfChanged(ref _ocrStatusText, value);
    }
    
    // OCR状態の色
    private SolidColorBrush _ocrStatusColor = new SolidColorBrush(Colors.Green);
    public SolidColorBrush OcrStatusColor
    {
        get => _ocrStatusColor;
        set => this.RaiseAndSetIfChanged(ref _ocrStatusColor, value);
    }
    
    // 最終認識時間
    private string _lastRecognitionTime = "処理なし";
    public string LastRecognitionTime
    {
        get => _lastRecognitionTime;
        set => this.RaiseAndSetIfChanged(ref _lastRecognitionTime, value);
    }
    
    // 詳細ログ有効フラグ
    private bool _isVerboseLoggingEnabled;
    public bool IsVerboseLoggingEnabled
    {
        get => _isVerboseLoggingEnabled;
        set => this.RaiseAndSetIfChanged(ref _isVerboseLoggingEnabled, value);
    }
    
    // コマンド
    public ReactiveCommand<Unit, Unit> RunOcrTestCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetOcrSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ReportOcrIssueCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportDiagnosticsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDeveloperModeCommand { get; }
    
    // コンストラクタ
    public OcrSettingsViewModel(
        IOcrService ocrService,
        ISettingsService settingsService,
        IDialogService dialogService)
    {
        // コマンド初期化
        RunOcrTestCommand = ReactiveCommand.CreateFromTask(async () => 
            await RunOcrTestAsync(ocrService, dialogService));
        
        ResetOcrSettingsCommand = ReactiveCommand.CreateFromTask(async () => 
            await ResetOcrSettingsAsync(settingsService, dialogService));
        
        ReportOcrIssueCommand = ReactiveCommand.CreateFromTask(async () => 
            await ReportOcrIssueAsync(ocrService, dialogService));
        
        ExportDiagnosticsCommand = ReactiveCommand.CreateFromTask(async () => 
            await ExportDiagnosticsAsync(ocrService, settingsService, dialogService));
        
        OpenDeveloperModeCommand = ReactiveCommand.Create(() => 
            OpenDeveloperMode(dialogService));
        
        // OCRサービスのステータス更新を購読
        ocrService.StatusChanged += OnOcrStatusChanged;
        
        // 設定の初期化
        InitializeFromSettings(settingsService);
    }
    
    // メソッド実装...
}
```

### 6.3 状態更新の仕組み

OCR処理状態と最終認識時間は、バックグラウンド処理から自動的に更新されます。

```csharp
private void OnOcrStatusChanged(object sender, OcrStatusChangedEventArgs e)
{
    // UI スレッドで実行
    Dispatcher.UIThread.InvokeAsync(() =>
    {
        // 状態テキスト更新
        OcrStatusText = GetStatusText(e.Status);
        
        // 状態色更新
        OcrStatusColor = GetStatusColor(e.Status);
        
        // 最終認識時間更新（OCR完了時のみ）
        if (e.Status == OcrProcessingStatus.Completed && e.RecognizedText != null)
        {
            LastRecognitionTime = GetElapsedTimeText(DateTime.Now);
            _lastRecognitionTimestamp = DateTime.Now;
        }
    });
}

// 定期的な経過時間更新（タイマーによる）
private void UpdateElapsedTime()
{
    if (_lastRecognitionTimestamp.HasValue)
    {
        LastRecognitionTime = GetElapsedTimeText(_lastRecognitionTimestamp.Value);
    }
}
```

## 7. OCRテスト結果表示

OCRテスト実行時には、以下のようなシンプルな結果ダイアログが表示されます：

```
+------------------------------------------+
|  OCRテスト結果                           |
|                                          |
|  [スクリーンショットサムネイル]          |
|                                          |
|  認識テキスト:                           |
|  ------------------------------          |
|  Lorem ipsum dolor sit amet,             |
|  consectetur adipiscing elit.            |
|  ------------------------------          |
|                                          |
|  認識言語: 日本語                        |
|  信頼度スコア: 87%                       |
|  処理時間: 342ms                         |
|                                          |
|  [ コピー ]      [ 閉じる ]              |
+------------------------------------------+
```

## 8. 開発者向け詳細UI

開発者モードを開くと、以下のような詳細設定・診断UIが表示されます。これは通常のユーザーには非表示です。

```
+------------------------------------------+
|  OCR開発者モード                         |
|                                          |
|  [タブ: 設定] [タブ: 診断] [タブ: テスト] |
|                                          |
|  -- 設定タブの内容 --                    |
|  前処理パラメータ:                       |
|    明るさ: [slider] (-100 〜 100)        |
|    コントラスト: [slider] (-100 〜 100)  |
|    シャープネス: [slider] (0 〜 100)     |
|    □ 二値化 閾値: [slider] (0 〜 255)   |
|                                          |
|  OCRエンジンパラメータ:                  |
|    検出信頼度: [slider] (0.0 〜 1.0)     |
|    テキスト認識閾値: [slider] (0.0 〜 1.0)|
|    スレッド数: [numeric] (1 〜 8)        |
|                                          |
|  [ 適用 ] [ 元に戻す ]                   |
+------------------------------------------+
```

## 9. OpenCVベースのOCR最適化アプローチ

新設計では、OpenCVを活用した画像処理アプローチを採用します：

### 9.1 画像前処理（リアルタイム）

- 画像前処理パイプライン
- テキスト領域検出
- 差分検出による最適化
- パラメータ適用

### 9.2 プロファイルベースの自動最適化

- ゲーム別に最適なパラメータを記録
- 使用状況に基づく自動調整
- フィードバックによる継続的改善

### 9.3 最適化アプローチの利点

- 高速な処理でリアルタイム性を確保
- リソース消費を抑えつつ高精度を実現
- 柔軟なパラメータ調整による適応性向上
- Windows環境に最適化された実装

### 9.4 クリーンアーキテクチャとの整合性

OCR設定UIは、Baketaプロジェクトのクリーンアーキテクチャと以下のように整合します：

```csharp
// UI抽象化
namespace Baketa.UI.Abstractions.OCR
{
    public interface IOcrSettingsViewModel
    {
        bool IsAutoOptimizationEnabled { get; set; }
        string OcrStatusText { get; }
        // その他のプロパティとメソッド
    }
}

// Avalonia UI実装
namespace Baketa.UI.Avalonia.ViewModels
{
    public class OcrSettingsViewModel : ViewModelBase, IOcrSettingsViewModel
    {
        // Avalonia UI固有の実装
    }
}
```

この整合性により、UIレイヤーと他のレイヤーを明確に分離し、Windows専用アプリケーションとしての特性を活かした実装が可能になります。

## 10. 結論

この新しいOCR設定UI設計は、ユーザー操作を最小限に抑え、自動最適化を中心に据えたシンプルなインターフェースを提供します。OpenCVベースの画像処理アプローチによる高度な最適化を背後で実行しながら、ユーザーには必要最小限の情報と制御のみを表示することで、使いやすさと高機能性を両立させます。

開発者向けには十分な診断・調整ツールを用意しつつ、通常のユーザーはほとんど設定操作なしに高品質なOCR機能を利用できる設計となっています。
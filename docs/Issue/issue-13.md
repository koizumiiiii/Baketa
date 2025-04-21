# Issue 13: OCR設定UIとプロファイル管理

## 概要
OCR（光学文字認識）の設定を管理するためのユーザーインターフェースと、ゲームプロファイルごとのOCR設定管理機能を実装します。これにより、ユーザーは異なるゲームやシナリオに合わせて最適なOCR設定を作成、編集、適用できるようになります。

## 目的・理由
OCR設定UIとプロファイル管理は以下の理由で重要です：

1. ゲームによってテキストの表示方法やフォントが異なるため、ゲームごとに最適なOCR設定が必要
2. ユーザーが自分のニーズに合わせてOCR設定をカスタマイズできることで、認識精度を向上させる
3. プロファイル管理により、異なるゲーム間で簡単に設定を切り替えることができる
4. OCR設定の永続化により、アプリケーションの再起動後も設定が維持される
5. 設定の視覚的な調整により、技術的な知識がなくてもOCR性能を最適化できる

## 詳細
- OCR設定モデルの基本設計
- シンプルなOCR設定UI画面の実装
- 基本的な設定の永続化
- 最低限のプレビュー機能

**注**: 初期実装では最小限のシンプルな設定に焦点を当て、基本機能の確実な実装を優先します。高度な設定UIやゲームプロファイルごとの設定管理は将来のフェーズで実装予定です。

## 実装アプローチ

### フェーズ1：最小限の設定UI（初期リリース）
- **シンプルな設定項目**:
  - 言語選択（翻訳元言語と翻訳先言語の選択）
  - 基本的なON/OFFスイッチのみ
  - 必須の閾値設定のみを実装
  
- **単一設定ファイル**:
  - アプリ全体での単一設定保存のみ
  - プロファイル管理機能なし
  
- **最小限のUI**:
  - 単一の設定タブにすべての基本設定を集約
  - 複雑な設定パネルや高度な機能は非表示

### フェーズ2：拡張機能（将来のバージョン）
- **高度な設定オプション**:
  - 詳細な前処理設定パネル
  - モデルカスタマイズオプション
  - パフォーマンス設定パネル
  
- **プロファイル管理**:
  - ゲームごとの設定保存
  - 自動検出・切り替え機能
  
- **高度なUI機能**:
  - リアルタイムプレビュー
  - 詳細なカスタマイズパネル
  - 視覚的な設定エディタ

## タスク分解

### フェーズ1：基本実装（初期リリース）
- [ ] 基本的なOCR設定モデルの設計
  - [ ] シンプルな`OcrSettings`クラスの実装（基本設定のみ）
  - [ ] 言語選択機能（日本語/英語）
  - [ ] 基本的な信頼度閾値設定
- [ ] シンプルなOCR設定UIの実装
  - [ ] 基本設定ページの実装（言語、閾値など最小限の設定）
  - [ ] シンプルなON/OFFスイッチ
- [ ] 基本的な設定永続化
  - [ ] OCR設定の基本的なJSONシリアライズ/デシリアライズ
  - [ ] アプリ全体での単一設定保存
- [ ] 基本的なOCR設定とOCRエンジンの連携
  - [ ] `IOcrEngine`インターフェースとの基本連携
  - [ ] PaddleOCRエンジンでの基本設定反映

### フェーズ2：拡張実装（将来のバージョン）
- [ ] OCR設定モデルの拡張
  - [ ] 前処理パイプライン設定の追加
  - [ ] カスタム領域検出設定の実装
  - [ ] 高度なフィルタリング設定の実装
  - [ ] GPU/CPU使用設定の実装
- [ ] 高度なOCR設定UI機能の追加
  - [ ] 前処理設定ページの実装
  - [ ] 領域検出設定ページの実装
  - [ ] パフォーマンス設定ページの実装
  - [ ] モデル管理設定ページの実装
- [ ] プロファイル管理システム
  - [ ] ゲームプロファイルモデルの拡張
  - [ ] ゲーム検出および自動プロファイル選択の実装
  - [ ] プロファイル作成・編集・削除UIの実装
- [ ] 高度な設定永続化機能
  - [ ] プロファイルごとの設定保存
  - [ ] 設定変更のバージョン管理
- [ ] 高度なプレビューと検証機能
  - [ ] リアルタイムプレビュー
  - [ ] 設定適用前の結果比較

**注**: 初期実装ではフェーズ1の基本機能のみを優先し、フェーズ2の機能は将来のバージョンで段階的に追加します。

## 最小限のUIイメージ（初期リリース）

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             xmlns:controls="using:Baketa.UI.Controls"
             xmlns:loc="using:Baketa.UI.Localization"
             x:Class="Baketa.UI.Views.Settings.SimpleOcrSettingsView"
             x:DataType="vm:SimpleOcrSettingsViewModel">
    
    <ScrollViewer>
        <StackPanel Margin="20" Spacing="15">
            <!-- OCR有効化スイッチ -->
            <controls:SettingsItem Title="{loc:Localize Settings_OCR_Enable}"
                                  Description="{loc:Localize Settings_OCR_Enable_Description}">
                <ToggleSwitch IsChecked="{Binding IsOcrEnabled}"/>
            </controls:SettingsItem>
            
            <!-- ゲーム画面の言語選択 -->
            <controls:SettingsItem Title="{loc:Localize Settings_OCR_SourceLanguage}"
                                  Description="{loc:Localize Settings_OCR_SourceLanguage_Description}">
                <ComboBox ItemsSource="{Binding AvailableSourceLanguages}"
                          SelectedItem="{Binding SelectedSourceLanguage}"
                          Width="200"/>
            </controls:SettingsItem>
            
            <!-- 翻訳先の言語選択 -->
            <controls:SettingsItem Title="{loc:Localize Settings_OCR_TargetLanguage}"
                                  Description="{loc:Localize Settings_OCR_TargetLanguage_Description}">
                <ComboBox ItemsSource="{Binding AvailableTargetLanguages}"
                          SelectedItem="{Binding SelectedTargetLanguage}"
                          Width="200"/>
            </controls:SettingsItem>
            
            <!-- 信頼度閾値 -->
            <controls:SettingsItem Title="{loc:Localize Settings_OCR_ConfidenceThreshold}"
                                  Description="{loc:Localize Settings_OCR_ConfidenceThreshold_Description}">
                <StackPanel Orientation="Vertical">
                    <Slider Value="{Binding ConfidenceThreshold}"
                            Minimum="0.1" Maximum="1.0" SmallChange="0.05" LargeChange="0.1"
                            Width="200" TickFrequency="0.1" IsSnapToTickEnabled="True"/>
                    <TextBlock Text="{Binding ConfidenceThreshold, StringFormat={}{0:P0}}"
                               HorizontalAlignment="Center"/>
                </StackPanel>
            </controls:SettingsItem>
            
            <!-- 自動検出間隔 -->
            <controls:SettingsItem Title="{loc:Localize Settings_OCR_DetectionInterval}"
                                  Description="{loc:Localize Settings_OCR_DetectionInterval_Description}">
                <StackPanel Orientation="Vertical">
                    <Slider Value="{Binding DetectionIntervalSeconds}"
                            Minimum="0.5" Maximum="5.0" SmallChange="0.1" LargeChange="0.5"
                            Width="200" TickFrequency="0.5" IsSnapToTickEnabled="True"/>
                    <TextBlock Text="{Binding DetectionIntervalSeconds, StringFormat={}{0:F1}秒}"
                               HorizontalAlignment="Center"/>
                </StackPanel>
            </controls:SettingsItem>
            
            <!-- モデル管理セクション（基本情報のみ） -->
            <controls:SettingsItem Title="{loc:Localize Settings_OCR_Models}"
                                  Description="{loc:Localize Settings_OCR_Models_Description}">
                <StackPanel>
                    <TextBlock Text="{Binding ModelStatusText}" Margin="0,0,0,5"/>
                    <Button Content="{loc:Localize Settings_OCR_CheckModels}" 
                            Command="{Binding CheckModelsCommand}"/>
                </StackPanel>
            </controls:SettingsItem>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

## 実装上の注意点
- 初期実装では最小限のシンプルな設定に焦点を当て、基本機能の確実な実装を優先する
- OCRエンジンの基本設定を適切に実装し、基本的な認識精度を確保する
- 設定UIは直感的で使いやすいシンプルなデザインにする
- 設定の永続化は安全かつ信頼性の高い方法で実装する
- 初期版ではプロファイル管理や詳細設定は実装せず、将来の拡張性を考慮した設計にする
- ユーザーにとって必須の「翻訳元言語」と「翻訳先言語」の選択を中心に設計
- 将来の拡張を見据えた設計（モデル登録の簡素化、動的モデルロード、プラグイン形式の可能性）

**注**: 高度な設定機能や詳細なプロファイル管理は将来のバージョンで段階的に追加する計画です。初期実装では基本機能の確実な動作を優先します。

## 関連Issue/参考
- 親Issue: なし（これが親Issue）
- 関連: #7 PaddleOCRの統合
- 関連: #8 OpenCVベースのOCR前処理最適化
- 関連: #12 設定画面
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-settings.md
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\profile-management.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: medium`
- `component: ocr`

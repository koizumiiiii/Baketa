# Issue 57 アニメーション実装

## Animations.axaml の内容

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- ページ遷移アニメーション -->
    <Style Selector="ContentControl.pageTransition">
        <Setter Property="Transitions">
            <Transitions>
                <CrossFade Duration="0.25" />
                <TransformOperationsTransition Property="RenderTransform" Duration="0.25" />
            </Transitions>
        </Setter>
    </Style>
    
    <!-- 出現アニメーション -->
    <Style Selector="Control.fadeIn">
        <Style.Animations>
            <Animation Duration="0.3" FillMode="Both">
                <KeyFrame Cue="0%">
                    <Setter Property="Opacity" Value="0" />
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="Opacity" Value="1" />
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>
    
    <!-- スライドインアニメーション -->
    <Style Selector="Control.slideIn">
        <Style.Animations>
            <Animation Duration="0.4" FillMode="Both" Easing="CubicEaseOut">
                <KeyFrame Cue="0%">
                    <Setter Property="TranslateTransform.X" Value="-50" />
                    <Setter Property="Opacity" Value="0" />
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="TranslateTransform.X" Value="0" />
                    <Setter Property="Opacity" Value="1" />
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>

    <!-- アクティブ状態への変更アニメーション -->
    <Style Selector=":is(Control).activated">
        <Setter Property="Transitions">
            <Transitions>
                <BrushTransition Property="Background" Duration="0.2" />
                <TransformOperationsTransition Property="RenderTransform" Duration="0.2" />
            </Transitions>
        </Setter>
    </Style>
    
    <!-- ボタンアニメーション -->
    <Style Selector="Button">
        <Setter Property="Transitions">
            <Transitions>
                <BrushTransition Property="Background" Duration="0.15" />
                <TransformOperationsTransition Property="RenderTransform" Duration="0.1" />
            </Transitions>
        </Setter>
    </Style>
    
    <!-- ボタン押下アニメーション -->
    <Style Selector="Button:pressed">
        <Setter Property="RenderTransform" Value="scale(0.98)" />
    </Style>
    
    <!-- タブ切り替えアニメーション -->
    <Style Selector="TabControl">
        <Setter Property="Transitions">
            <Transitions>
                <ThicknessTransition Property="Padding" Duration="0.25" />
            </Transitions>
        </Setter>
    </Style>
    
    <!-- 通知アニメーション -->
    <Style Selector="Border.notification">
        <Style.Animations>
            <Animation Duration="0.3" FillMode="Both">
                <KeyFrame Cue="0%">
                    <Setter Property="Opacity" Value="0" />
                    <Setter Property="TranslateTransform.Y" Value="20" />
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="Opacity" Value="1" />
                    <Setter Property="TranslateTransform.Y" Value="0" />
                </KeyFrame>
            </Animation>
            <Animation Duration="0.3" FillMode="Both" Delay="3">
                <KeyFrame Cue="0%">
                    <Setter Property="Opacity" Value="1" />
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="Opacity" Value="0" />
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>

    <!-- メニュー展開アニメーション -->
    <Style Selector="MenuItem > :is(Panel)#PART_Popup">
        <Setter Property="Transitions">
            <Transitions>
                <DoubleTransition Property="Opacity" Duration="0.2" />
                <TransformOperationsTransition Property="RenderTransform" Duration="0.15" Easing="CubicEaseOut" />
            </Transitions>
        </Setter>
    </Style>
</Styles>
```

## App.axaml への追加方法

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.App">
    <Application.Styles>
        <FluentTheme />
        <!-- 既存のスタイルインクルード -->
        <StyleInclude Source="avares://Baketa.UI/Styles/BasicStyles.axaml" />
        
        <!-- アニメーション定義を追加 -->
        <StyleInclude Source="avares://Baketa.UI/Styles/Animations.axaml" />
    </Application.Styles>
    
    <!-- リソース定義 -->
    <Application.Resources>
        <!-- 既存のリソース -->
    </Application.Resources>
</Application>
```

## アニメーション使用例

### 1. ページ遷移アニメーション（MainWindow.axaml）

```xml
<TabControl Grid.Row="1" TabStripPlacement="Left" SelectedIndex="{Binding SelectedTabIndex}">
    <!-- タブヘッダー定義... -->
    
    <!-- コンテンツプレゼンター部分にアニメーション適用 -->
    <TabControl.ContentTemplate>
        <DataTemplate>
            <ContentControl Content="{Binding}" Classes="pageTransition" />
        </DataTemplate>
    </TabControl.ContentTemplate>
</TabControl>
```

### 2. 通知アニメーション（MainWindow.axaml）

```xml
<!-- ステータスバーに通知表示 -->
<Grid Grid.Row="2" ColumnDefinitions="*,Auto">
    <!-- 既存のステータスバーコンテンツ -->
    
    <!-- 通知領域 -->
    <Border Grid.Column="1" Classes="notification"
            IsVisible="{Binding IsNotificationVisible}"
            Background="{DynamicResource NotificationBackground}"
            CornerRadius="4" Padding="8,4">
        <TextBlock Text="{Binding NotificationMessage}"
                   Foreground="{DynamicResource NotificationForeground}" />
    </Border>
</Grid>
```

### 3. コンポーネント出現アニメーション（HomeView.axaml）

```xml
<StackPanel Classes="fadeIn">
    <TextBlock Text="最近の翻訳" FontWeight="SemiBold" Margin="0,0,0,8" />
    <ListBox ItemsSource="{Binding RecentTranslations}"
            MaxHeight="200" />
</StackPanel>
```

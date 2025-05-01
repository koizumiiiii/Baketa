namespace Baketa.UI.Helpers
{
    /// <summary>
    /// アクセシビリティの役割を定義する列挙型
    /// Avalonia UIのAutomationControlTypeに相当
    /// </summary>
    public enum AccessibilityRole
    {
        /// <summary>
        /// 不明なコントロール
        /// </summary>
        None = 0,
        
        /// <summary>
        /// ボタン
        /// </summary>
        Button = 1,
        
        /// <summary>
        /// カレンダー
        /// </summary>
        Calendar = 2,
        
        /// <summary>
        /// チェックボックス
        /// </summary>
        CheckBox = 3,
        
        /// <summary>
        /// コンボボックス
        /// </summary>
        ComboBox = 4,
        
        /// <summary>
        /// 編集フィールド
        /// </summary>
        Edit = 5,
        
        /// <summary>
        /// ハイパーリンク
        /// </summary>
        Hyperlink = 6,
        
        /// <summary>
        /// 画像
        /// </summary>
        Image = 7,
        
        /// <summary>
        /// リストボックス
        /// </summary>
        ListBox = 8,
        
        /// <summary>
        /// リスト項目
        /// </summary>
        ListItem = 9,
        
        /// <summary>
        /// メニュー
        /// </summary>
        Menu = 10,
        
        /// <summary>
        /// メニューバー
        /// </summary>
        MenuBar = 11,
        
        /// <summary>
        /// メニュー項目
        /// </summary>
        MenuItem = 12,
        
        /// <summary>
        /// プログレスバー
        /// </summary>
        ProgressBar = 13,
        
        /// <summary>
        /// ラジオボタン
        /// </summary>
        RadioButton = 14,
        
        /// <summary>
        /// スクロールバー
        /// </summary>
        ScrollBar = 15,
        
        /// <summary>
        /// スライダー
        /// </summary>
        Slider = 16,
        
        /// <summary>
        /// スピンボタン
        /// </summary>
        Spinner = 17,
        
        /// <summary>
        /// ステータスバー
        /// </summary>
        StatusBar = 18,
        
        /// <summary>
        /// タブコントロール
        /// </summary>
        Tab = 19,
        
        /// <summary>
        /// タブ項目
        /// </summary>
        TabItem = 20,
        
        /// <summary>
        /// テキストブロック
        /// </summary>
        Text = 21,
        
        /// <summary>
        /// ツールバー
        /// </summary>
        ToolBar = 22,
        
        /// <summary>
        /// ツールチップ
        /// </summary>
        ToolTip = 23,
        
        /// <summary>
        /// ツリービュー
        /// </summary>
        Tree = 24,
        
        /// <summary>
        /// ツリービュー項目
        /// </summary>
        TreeItem = 25,
        
        /// <summary>
        /// ウィンドウ
        /// </summary>
        Window = 26,
        
        /// <summary>
        /// ダイアログ
        /// </summary>
        Dialog = 27,
        
        /// <summary>
        /// セパレーター
        /// </summary>
        Separator = 28,
        
        /// <summary>
        /// ドキュメント
        /// </summary>
        Document = 29,
        
        /// <summary>
        /// パネル
        /// </summary>
        Panel = 30,
        
        /// <summary>
        /// ヘッダー
        /// </summary>
        Header = 31,
        
        /// <summary>
        /// ヘッダー項目
        /// </summary>
        HeaderItem = 32,
        
        /// <summary>
        /// テーブル
        /// </summary>
        Table = 33,
        
        /// <summary>
        /// データグリッド
        /// </summary>
        DataGrid = 34,
        
        /// <summary>
        /// グループ
        /// </summary>
        Group = 35,
        
        /// <summary>
        /// カスタム
        /// </summary>
        Custom = 36
    }
}
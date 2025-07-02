namespace Baketa.Core.Abstractions.OCR.TextDetection;

    /// <summary>
    /// テキスト領域のタイプを表す列挙型
    /// </summary>
    public enum TextRegionType
    {
        /// <summary>
        /// 不明なタイプ
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// タイトル
        /// </summary>
        Title = 1,
        
        /// <summary>
        /// 見出し
        /// </summary>
        Heading = 2,
        
        /// <summary>
        /// 段落
        /// </summary>
        Paragraph = 3,
        
        /// <summary>
        /// キャプション
        /// </summary>
        Caption = 4,
        
        /// <summary>
        /// メニュー項目
        /// </summary>
        MenuItem = 5,
        
        /// <summary>
        /// ボタン
        /// </summary>
        Button = 6,
        
        /// <summary>
        /// ラベル
        /// </summary>
        Label = 7,
        
        /// <summary>
        /// 値
        /// </summary>
        Value = 8,
        
        /// <summary>
        /// ダイアログ
        /// </summary>
        Dialogue = 9
    }

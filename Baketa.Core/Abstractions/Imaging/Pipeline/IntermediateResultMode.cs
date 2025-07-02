namespace Baketa.Core.Abstractions.Imaging.Pipeline;

    /// <summary>
    /// 中間結果の保存モード
    /// </summary>
    public enum IntermediateResultMode
    {
        /// <summary>
        /// 中間結果を保存しない
        /// </summary>
        None,
        
        /// <summary>
        /// デバッグモードでのみ中間結果を保存
        /// </summary>
        DebugOnly,
        
        /// <summary>
        /// 選択されたステップの中間結果のみを保存
        /// </summary>
        SelectedSteps,
        
        /// <summary>
        /// すべてのステップの中間結果を保存
        /// </summary>
        All,
        
        /// <summary>
        /// エラー発生時のみ中間結果を保存
        /// </summary>
        OnError
    }

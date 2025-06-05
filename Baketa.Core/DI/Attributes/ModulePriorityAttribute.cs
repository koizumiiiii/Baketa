using System;

namespace Baketa.Core.DI.Attributes;

    /// <summary>
    /// モジュールの登録優先順位を指定する属性。
    /// 優先順位が高いモジュールが先に登録されます。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ModulePriorityAttribute(ModulePriority priority) : Attribute
    {
        /// <summary>
        /// モジュールの優先順位
        /// </summary>
        public ModulePriority Priority { get; } = priority;
    }

    /// <summary>
    /// モジュールの優先順位を表す列挙型
    /// </summary>
    public enum ModulePriority
    {
        /// <summary>
        /// 優先度が設定されていない場合
        /// </summary>
        None = 0,
        
        /// <summary>
        /// システムコアコンポーネント（最優先）
        /// </summary>
        Core = 1000,
        
        /// <summary>
        /// インフラストラクチャコンポーネント
        /// </summary>
        Infrastructure = 900,
        
        /// <summary>
        /// プラットフォーム固有コンポーネント
        /// </summary>
        Platform = 800,
        
        /// <summary>
        /// アプリケーションサービスコンポーネント
        /// </summary>
        Application = 700,
        
        /// <summary>
        /// UIコンポーネント
        /// </summary>
        UI = 600,
        
        /// <summary>
        /// プラグインコンポーネント
        /// </summary>
        Plugin = 500,
        
        /// <summary>
        /// カスタムコンポーネント（最低優先度）
        /// </summary>
        Custom = 100
    }

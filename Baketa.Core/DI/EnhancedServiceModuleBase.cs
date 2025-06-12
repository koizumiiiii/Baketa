using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Baketa.Core.DI;

    /// <summary>
    /// 拡張機能を持つサービス登録モジュールの基本実装。
    /// モジュール情報や依存関係管理の追加機能を提供します。
    /// </summary>
    public abstract class EnhancedServiceModuleBase : ServiceModuleBase
    {
        /// <summary>
        /// ロガーインスタンス（オプション）
        /// </summary>
        protected ILogger? Logger { get; set; }
        
        /// <summary>
        /// このモジュールが依存する他のモジュールのタイプを取得します。
        /// </summary>
        /// <returns>依存モジュールのタイプ配列</returns>
        public abstract Type[] GetDependencies();
        
        /// <summary>
        /// モジュールの優先度を取得します。
        /// 数値が小さいほど高優先度で登録されます。
        /// </summary>
        public abstract int Priority { get; }
        
        /// <summary>
        /// モジュール名を取得します。
        /// </summary>
        public abstract string ModuleName { get; }
        
        /// <summary>
        /// モジュールの説明を取得します。
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// ベースクラスの依存モジュール取得をGetDependencies()にリダイレクト
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override System.Collections.Generic.IEnumerable<Type> GetDependentModules() => GetDependencies();
        
        /// <summary>
        /// ロガーを設定します
        /// </summary>
        /// <param name="logger">ロガーインスタンス</param>
        public virtual void SetLogger(ILogger logger)
        {
            Logger = logger;
        }
    }

using System;

namespace Baketa.Core.DI.Attributes
{
    /// <summary>
    /// アセンブリスキャンによる自動登録の対象となるモジュールを識別する属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AutoRegisterAttribute : Attribute
    {
        /// <summary>
        /// 自動登録属性を初期化します。
        /// </summary>
        public AutoRegisterAttribute()
        {
        }
    }
}
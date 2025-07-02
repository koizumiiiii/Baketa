using System;

namespace Baketa.Core.DI;

    /// <summary>
    /// Baketaアプリケーションの環境設定を保持するクラス。
    /// DIコンテナで登録するためのラッパークラスです。
    /// </summary>
    public class BaketaEnvironmentSettings
    {
        /// <summary>
        /// アプリケーション実行環境
        /// </summary>
        public BaketaEnvironment Environment { get; set; } = BaketaEnvironment.Production;
    }

using System;
using System.Collections.Generic;

namespace Baketa.Core.Settings;

    /// <summary>
    /// ゲーム別キャプチャプロファイル
    /// </summary>
    public class GameCaptureProfile
    {
        /// <summary>
        /// プロファイル名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// プロファイルの説明
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// 対象ゲームの実行ファイル名
        /// </summary>
        public string ExecutableName { get; set; } = string.Empty;
        
        /// <summary>
        /// 対象ゲームのウィンドウタイトル（パターン）
        /// </summary>
        public string WindowTitlePattern { get; set; } = string.Empty;
        
        /// <summary>
        /// キャプチャ設定
        /// </summary>
        public CaptureSettings CaptureSettings { get; set; } = new();
        
        /// <summary>
        /// OCR設定
        /// </summary>
        public OcrSettings OcrSettings { get; set; } = new();
        
        /// <summary>
        /// 翻訳設定
        /// </summary>
        public TranslationSettings TranslationSettings { get; set; } = new();
        
        /// <summary>
        /// プロファイルが有効かどうか
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 最終更新日時
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 最終使用日時
        /// </summary>
        public DateTime? LastUsedAt { get; set; }
        
        /// <summary>
        /// プロファイルのタグ（分類用）
        /// </summary>
        public IList<string> Tags { get; set; } = [];
        
        /// <summary>
        /// プロファイルの優先度（数値が小さいほど高優先度）
        /// </summary>
        public int Priority { get; set; } = 100;
        
        /// <summary>
        /// 自動検出で使用するかどうか
        /// </summary>
        public bool UseForAutoDetection { get; set; } = true;
        
        /// <summary>
        /// プロファイルのクローンを作成します
        /// </summary>
        /// <returns>クローンされたプロファイル</returns>
        public GameCaptureProfile Clone()
        {
            return new GameCaptureProfile
            {
                Name = Name,
                Description = Description,
                ExecutableName = ExecutableName,
                WindowTitlePattern = WindowTitlePattern,
                CaptureSettings = CaptureSettings.Clone(),
                OcrSettings = OcrSettings.Clone(),
                TranslationSettings = TranslationSettings.Clone(),
                IsEnabled = IsEnabled,
                CreatedAt = CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                LastUsedAt = LastUsedAt,
                Tags = [..Tags],
                Priority = Priority,
                UseForAutoDetection = UseForAutoDetection
            };
        }
        
        /// <summary>
        /// プロファイルの設定を更新します
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
            LastUsedAt = DateTime.UtcNow;
        }
        
        /// <summary>
        /// 指定された実行ファイル名がこのプロファイルに一致するかチェックします
        /// </summary>
        /// <param name="executableName">実行ファイル名</param>
        /// <returns>一致する場合true</returns>
        public bool MatchesExecutable(string executableName)
        {
            if (string.IsNullOrWhiteSpace(ExecutableName) || string.IsNullOrWhiteSpace(executableName))
                return false;
                
            return string.Equals(ExecutableName, executableName, StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// 指定されたウィンドウタイトルがこのプロファイルに一致するかチェックします
        /// </summary>
        /// <param name="windowTitle">ウィンドウタイトル</param>
        /// <returns>一致する場合true</returns>
        public bool MatchesWindowTitle(string windowTitle)
        {
            if (string.IsNullOrWhiteSpace(WindowTitlePattern) || string.IsNullOrWhiteSpace(windowTitle))
                return false;
                
            try
            {
                // 簡単なワイルドカードパターンをサポート
                var pattern = WindowTitlePattern.Replace("*", ".*", StringComparison.Ordinal).Replace("?", ".", StringComparison.Ordinal);
                return System.Text.RegularExpressions.Regex.IsMatch(windowTitle, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                // 正規表現が無効な場合は部分文字列マッチングで代替
                return windowTitle.Contains(WindowTitlePattern, StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                // 正規表現のタイムアウトが発生した場合は部分文字列マッチングで代替
                return windowTitle.Contains(WindowTitlePattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        /// <summary>
        /// プロファイルの検証を行います
        /// </summary>
        /// <returns>検証結果</returns>
        public (bool IsValid, List<string> Errors) Validate()
        {
            var errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("プロファイル名が設定されていません");
                
            if (string.IsNullOrWhiteSpace(ExecutableName) && string.IsNullOrWhiteSpace(WindowTitlePattern))
                errors.Add("実行ファイル名またはウィンドウタイトルパターンのいずれかを設定してください");
                
            if (Priority < 0)
                errors.Add("優先度は0以上の値を設定してください");
            
            return (errors.Count == 0, errors);
        }
    }

# Issue 11-2: オーバーレイ位置とサイズの管理システムの実装

## 概要
オーバーレイウィンドウの位置とサイズを管理するシステムを実装します。このシステムは、OCR検出されたテキスト領域に基づく自動配置、ユーザー定義の固定領域、ゲームウィンドウの状態変化への対応などを実現します。

## 目的・理由
オーバーレイ位置・サイズ管理は以下の理由で重要です：

1. 原文テキストの近くに翻訳を表示することで、対応関係を明確にする
2. ゲームプレイを邪魔しない最適な位置に翻訳テキストを表示する
3. ウィンドウサイズ変更やフルスクリーン切替など、ゲームウィンドウの状態変化に適切に対応する
4. ユーザーが好みに応じて表示位置やサイズをカスタマイズできるようにする

## 詳細
- テキスト領域追跡と自動配置アルゴリズムの実装
- 位置・サイズ調整の各種モードと設定の実装
- ウィンドウ状態変化の検出と対応
- ユーザーカスタマイズ機能の実装

## タスク分解
- [ ] オーバーレイ管理基盤の実装
  - [ ] `IOverlayPositionManager`インターフェースの設計
  - [ ] `OverlayPositionManager`クラスの実装
  - [ ] 位置・サイズモデルの定義
- [ ] 配置アルゴリズムの実装
  - [ ] テキスト領域ベースの配置アルゴリズム
  - [ ] 固定位置・サイズの配置アルゴリズム
  - [ ] スマート配置アルゴリズム（ゲームコンテンツの邪魔にならない位置を選択）
  - [ ] フォローモード（テキスト領域に追従する）の実装
- [ ] ウィンドウ状態変化への対応
  - [ ] ウィンドウサイズ変更の検出と対応
  - [ ] フルスクリーン⇔ウィンドウモード切替の検出と対応
  - [ ] マルチモニター環境での対応
- [ ] OCR結果との連携
  - [ ] OCR検出テキスト領域の取得と管理
  - [ ] テキスト領域の追跡と関連付け
  - [ ] 翻訳テキストとの対応付け
- [ ] ユーザーカスタマイズの実装
  - [ ] 位置オフセットの設定
  - [ ] サイズ調整設定の実装
  - [ ] 位置制約の設定
- [ ] 最適化と競合解決
  - [ ] 複数のオーバーレイ間の配置調整
  - [ ] オーバーラップの検出と解決
  - [ ] 画面境界外へのはみ出し防止
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.UI.Overlay.Positioning
{
    /// <summary>
    /// オーバーレイ位置マネージャーインターフェース
    /// </summary>
    public interface IOverlayPositionManager
    {
        /// <summary>
        /// 配置モード
        /// </summary>
        OverlayPositionMode PositionMode { get; set; }
        
        /// <summary>
        /// サイズモード
        /// </summary>
        OverlaySizeMode SizeMode { get; set; }
        
        /// <summary>
        /// 固定位置
        /// </summary>
        Point FixedPosition { get; set; }
        
        /// <summary>
        /// 固定サイズ
        /// </summary>
        Size FixedSize { get; set; }
        
        /// <summary>
        /// 位置オフセット
        /// </summary>
        Vector PositionOffset { get; set; }
        
        /// <summary>
        /// サイズスケール
        /// </summary>
        double SizeScale { get; set; }
        
        /// <summary>
        /// 最大サイズ
        /// </summary>
        Size MaxSize { get; set; }
        
        /// <summary>
        /// 最小サイズ
        /// </summary>
        Size MinSize { get; set; }
        
        /// <summary>
        /// 現在の位置
        /// </summary>
        Point CurrentPosition { get; }
        
        /// <summary>
        /// 現在のサイズ
        /// </summary>
        Size CurrentSize { get; }
        
        /// <summary>
        /// 位置・サイズが更新された時に発生するイベント
        /// </summary>
        event EventHandler<OverlayPositionUpdatedEventArgs> PositionUpdated;
        
        /// <summary>
        /// テキスト領域の位置情報を更新します
        /// </summary>
        /// <param name="textRegions">テキスト領域のコレクション</param>
        void UpdateTextRegions(IReadOnlyList<TextRegion> textRegions);
        
        /// <summary>
        /// 翻訳テキスト情報を更新します
        /// </summary>
        /// <param name="translationInfo">翻訳情報</param>
        void UpdateTranslationInfo(TranslationInfo translationInfo);
        
        /// <summary>
        /// ゲームウィンドウ状態の更新を通知します
        /// </summary>
        /// <param name="gameWindowInfo">ゲームウィンドウ情報</param>
        void NotifyGameWindowUpdate(GameWindowInfo gameWindowInfo);
        
        /// <summary>
        /// オーバーレイの位置とサイズを計算します
        /// </summary>
        /// <returns>位置とサイズ情報</returns>
        OverlayPositionInfo CalculatePositionAndSize();
        
        /// <summary>
        /// オーバーレイの位置とサイズを適用します
        /// </summary>
        /// <param name="overlayWindow">オーバーレイウィンドウ</param>
        void ApplyPositionAndSize(IOverlayWindow overlayWindow);
    }
    
    /// <summary>
    /// オーバーレイ配置モード
    /// </summary>
    public enum OverlayPositionMode
    {
        /// <summary>
        /// テキスト領域に基づく配置
        /// </summary>
        TextRegionBased,
        
        /// <summary>
        /// 固定位置
        /// </summary>
        Fixed,
        
        /// <summary>
        /// スマート配置（コンテンツを邪魔しない位置）
        /// </summary>
        Smart,
        
        /// <summary>
        /// フォローモード（テキスト領域に追従）
        /// </summary>
        Follow,
        
        /// <summary>
        /// 相対位置（ゲームウィンドウ内の相対座標）
        /// </summary>
        Relative
    }
    
    /// <summary>
    /// オーバーレイサイズモード
    /// </summary>
    public enum OverlaySizeMode
    {
        /// <summary>
        /// テキスト領域に基づくサイズ
        /// </summary>
        TextRegionBased,
        
        /// <summary>
        /// 固定サイズ
        /// </summary>
        Fixed,
        
        /// <summary>
        /// コンテンツに合わせたサイズ
        /// </summary>
        ContentBased,
        
        /// <summary>
        /// 相対サイズ（ゲームウィンドウサイズに対する割合）
        /// </summary>
        Relative
    }
    
    /// <summary>
    /// オーバーレイ位置情報
    /// </summary>
    public class OverlayPositionInfo
    {
        /// <summary>
        /// 位置
        /// </summary>
        public Point Position { get; set; }
        
        /// <summary>
        /// サイズ
        /// </summary>
        public Size Size { get; set; }
        
        /// <summary>
        /// 基準となったテキスト領域
        /// </summary>
        public TextRegion? SourceTextRegion { get; set; }
        
        /// <summary>
        /// 適用された配置モード
        /// </summary>
        public OverlayPositionMode AppliedPositionMode { get; set; }
        
        /// <summary>
        /// 適用されたサイズモード
        /// </summary>
        public OverlaySizeMode AppliedSizeMode { get; set; }
        
        /// <summary>
        /// 基準ゲームウィンドウ情報
        /// </summary>
        public GameWindowInfo GameWindowInfo { get; set; }
        
        /// <summary>
        /// 制約が適用されたかどうか
        /// </summary>
        public bool ConstraintsApplied { get; set; }
        
        /// <summary>
        /// 新しいオーバーレイ位置情報を初期化します
        /// </summary>
        public OverlayPositionInfo()
        {
            Position = new Point();
            Size = new Size();
            GameWindowInfo = new GameWindowInfo();
        }
        
        /// <summary>
        /// 新しいオーバーレイ位置情報を初期化します
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="size">サイズ</param>
        public OverlayPositionInfo(Point position, Size size)
        {
            Position = position;
            Size = size;
            GameWindowInfo = new GameWindowInfo();
        }
    }
    
    /// <summary>
    /// オーバーレイ位置更新イベント引数
    /// </summary>
    public class OverlayPositionUpdatedEventArgs : EventArgs
    {
        /// <summary>
        /// 以前の位置情報
        /// </summary>
        public OverlayPositionInfo OldPositionInfo { get; }
        
        /// <summary>
        /// 新しい位置情報
        /// </summary>
        public OverlayPositionInfo NewPositionInfo { get; }
        
        /// <summary>
        /// 更新理由
        /// </summary>
        public PositionUpdateReason Reason { get; }
        
        /// <summary>
        /// 新しいオーバーレイ位置更新イベント引数を初期化します
        /// </summary>
        /// <param name="oldPositionInfo">以前の位置情報</param>
        /// <param name="newPositionInfo">新しい位置情報</param>
        /// <param name="reason">更新理由</param>
        public OverlayPositionUpdatedEventArgs(
            OverlayPositionInfo oldPositionInfo,
            OverlayPositionInfo newPositionInfo,
            PositionUpdateReason reason)
        {
            OldPositionInfo = oldPositionInfo;
            NewPositionInfo = newPositionInfo;
            Reason = reason;
        }
    }
    
    /// <summary>
    /// 位置更新理由
    /// </summary>
    public enum PositionUpdateReason
    {
        /// <summary>
        /// テキスト領域の更新
        /// </summary>
        TextRegionUpdated,
        
        /// <summary>
        /// 翻訳テキストの更新
        /// </summary>
        TranslationUpdated,
        
        /// <summary>
        /// ゲームウィンドウの更新
        /// </summary>
        GameWindowUpdated,
        
        /// <summary>
        /// 設定の変更
        /// </summary>
        SettingsChanged,
        
        /// <summary>
        /// 手動調整
        /// </summary>
        ManualAdjustment,
        
        /// <summary>
        /// 制約の適用
        /// </summary>
        ConstraintsApplied
    }
    
    /// <summary>
    /// ゲームウィンドウ情報
    /// </summary>
    public class GameWindowInfo
    {
        /// <summary>
        /// ウィンドウハンドル
        /// </summary>
        public IntPtr WindowHandle { get; set; }
        
        /// <summary>
        /// ウィンドウタイトル
        /// </summary>
        public string WindowTitle { get; set; } = string.Empty;
        
        /// <summary>
        /// ウィンドウ位置
        /// </summary>
        public Point Position { get; set; }
        
        /// <summary>
        /// ウィンドウサイズ
        /// </summary>
        public Size Size { get; set; }
        
        /// <summary>
        /// クライアント領域位置
        /// </summary>
        public Point ClientPosition { get; set; }
        
        /// <summary>
        /// クライアント領域サイズ
        /// </summary>
        public Size ClientSize { get; set; }
        
        /// <summary>
        /// フルスクリーンかどうか
        /// </summary>
        public bool IsFullScreen { get; set; }
        
        /// <summary>
        /// ボーダレスウィンドウかどうか
        /// </summary>
        public bool IsBorderless { get; set; }
        
        /// <summary>
        /// 最大化されているかどうか
        /// </summary>
        public bool IsMaximized { get; set; }
        
        /// <summary>
        /// 最小化されているかどうか
        /// </summary>
        public bool IsMinimized { get; set; }
        
        /// <summary>
        /// アクティブかどうか
        /// </summary>
        public bool IsActive { get; set; }
        
        /// <summary>
        /// ディスプレイモニター情報
        /// </summary>
        public MonitorInfo Monitor { get; set; } = new MonitorInfo();
    }
    
    /// <summary>
    /// モニター情報
    /// </summary>
    public class MonitorInfo
    {
        /// <summary>
        /// モニターハンドル
        /// </summary>
        public IntPtr MonitorHandle { get; set; }
        
        /// <summary>
        /// モニター名
        /// </summary>
        public string MonitorName { get; set; } = string.Empty;
        
        /// <summary>
        /// モニター領域
        /// </summary>
        public Rect Bounds { get; set; }
        
        /// <summary>
        /// 作業領域
        /// </summary>
        public Rect WorkArea { get; set; }
        
        /// <summary>
        /// プライマリモニターかどうか
        /// </summary>
        public bool IsPrimary { get; set; }
        
        /// <summary>
        /// DPIスケールX
        /// </summary>
        public double DpiScaleX { get; set; } = 1.0;
        
        /// <summary>
        /// DPIスケールY
        /// </summary>
        public double DpiScaleY { get; set; } = 1.0;
    }
    
    /// <summary>
    /// 翻訳情報
    /// </summary>
    public class TranslationInfo
    {
        /// <summary>
        /// 元テキスト
        /// </summary>
        public string SourceText { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳テキスト
        /// </summary>
        public string TranslatedText { get; set; } = string.Empty;
        
        /// <summary>
        /// 元テキスト領域
        /// </summary>
        public TextRegion? SourceRegion { get; set; }
        
        /// <summary>
        /// 翻訳リクエストID
        /// </summary>
        public Guid TranslationId { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// テキスト測定情報
        /// </summary>
        public TextMeasurementInfo? MeasurementInfo { get; set; }
    }
    
    /// <summary>
    /// テキスト測定情報
    /// </summary>
    public class TextMeasurementInfo
    {
        /// <summary>
        /// テキストサイズ
        /// </summary>
        public Size TextSize { get; set; }
        
        /// <summary>
        /// 行数
        /// </summary>
        public int LineCount { get; set; }
        
        /// <summary>
        /// 文字数
        /// </summary>
        public int CharacterCount { get; set; }
        
        /// <summary>
        /// 最小フォントサイズ
        /// </summary>
        public double MinFontSize { get; set; }
        
        /// <summary>
        /// 推奨フォントサイズ
        /// </summary>
        public double RecommendedFontSize { get; set; }
    }
}
```

## ポジションマネージャーの実装例
```csharp
namespace Baketa.UI.Overlay.Positioning
{
    /// <summary>
    /// オーバーレイ位置マネージャー実装
    /// </summary>
    public class OverlayPositionManager : IOverlayPositionManager
    {
        private readonly ILogger? _logger;
        private readonly ITextMeasurer _textMeasurer;
        private readonly IList<TextRegion> _textRegions = new List<TextRegion>();
        private TranslationInfo? _currentTranslation;
        private GameWindowInfo _gameWindowInfo = new GameWindowInfo();
        private OverlayPositionInfo _currentPositionInfo = new OverlayPositionInfo();
        private readonly object _syncLock = new object();
        
        /// <summary>
        /// 新しいオーバーレイ位置マネージャーを初期化します
        /// </summary>
        /// <param name="textMeasurer">テキスト測定サービス</param>
        /// <param name="logger">ロガー</param>
        public OverlayPositionManager(ITextMeasurer textMeasurer, ILogger? logger = null)
        {
            _textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
            _logger = logger;
            
            // デフォルト値の設定
            PositionMode = OverlayPositionMode.TextRegionBased;
            SizeMode = OverlaySizeMode.ContentBased;
            FixedPosition = new Point(100, 100);
            FixedSize = new Size(400, 200);
            PositionOffset = new Vector(10, 10);
            SizeScale = 1.2;
            MaxSize = new Size(800, 600);
            MinSize = new Size(100, 50);
            
            _logger?.LogInformation("オーバーレイ位置マネージャーが初期化されました。");
        }
        
        /// <inheritdoc />
        public OverlayPositionMode PositionMode { get; set; }
        
        /// <inheritdoc />
        public OverlaySizeMode SizeMode { get; set; }
        
        /// <inheritdoc />
        public Point FixedPosition { get; set; }
        
        /// <inheritdoc />
        public Size FixedSize { get; set; }
        
        /// <inheritdoc />
        public Vector PositionOffset { get; set; }
        
        /// <inheritdoc />
        public double SizeScale { get; set; }
        
        /// <inheritdoc />
        public Size MaxSize { get; set; }
        
        /// <inheritdoc />
        public Size MinSize { get; set; }
        
        /// <inheritdoc />
        public Point CurrentPosition => _currentPositionInfo.Position;
        
        /// <inheritdoc />
        public Size CurrentSize => _currentPositionInfo.Size;
        
        /// <inheritdoc />
        public event EventHandler<OverlayPositionUpdatedEventArgs>? PositionUpdated;
        
        /// <inheritdoc />
        public void UpdateTextRegions(IReadOnlyList<TextRegion> textRegions)
        {
            lock (_syncLock)
            {
                _textRegions.Clear();
                
                foreach (var region in textRegions)
                {
                    _textRegions.Add(region);
                }
                
                _logger?.LogDebug("テキスト領域が更新されました。領域数: {Count}", _textRegions.Count);
                
                // 位置とサイズを再計算して適用
                var oldPositionInfo = _currentPositionInfo;
                _currentPositionInfo = CalculatePositionAndSize();
                
                // 変更があれば通知
                if (oldPositionInfo.Position != _currentPositionInfo.Position || 
                    oldPositionInfo.Size != _currentPositionInfo.Size)
                {
                    PositionUpdated?.Invoke(this, new OverlayPositionUpdatedEventArgs(
                        oldPositionInfo,
                        _currentPositionInfo,
                        PositionUpdateReason.TextRegionUpdated));
                }
            }
        }
        
        /// <inheritdoc />
        public void UpdateTranslationInfo(TranslationInfo translationInfo)
        {
            lock (_syncLock)
            {
                _currentTranslation = translationInfo;
                
                // テキスト測定情報がなければ測定
                if (_currentTranslation.MeasurementInfo == null)
                {
                    _currentTranslation.MeasurementInfo = MeasureText(_currentTranslation.TranslatedText);
                }
                
                _logger?.LogDebug("翻訳情報が更新されました。テキスト長: {Length}", 
                    _currentTranslation.TranslatedText?.Length ?? 0);
                
                // 位置とサイズを再計算して適用
                var oldPositionInfo = _currentPositionInfo;
                _currentPositionInfo = CalculatePositionAndSize();
                
                // 変更があれば通知
                if (oldPositionInfo.Position != _currentPositionInfo.Position || 
                    oldPositionInfo.Size != _currentPositionInfo.Size)
                {
                    PositionUpdated?.Invoke(this, new OverlayPositionUpdatedEventArgs(
                        oldPositionInfo,
                        _currentPositionInfo,
                        PositionUpdateReason.TranslationUpdated));
                }
            }
        }
        
        /// <inheritdoc />
        public void NotifyGameWindowUpdate(GameWindowInfo gameWindowInfo)
        {
            lock (_syncLock)
            {
                _gameWindowInfo = gameWindowInfo;
                
                _logger?.LogDebug("ゲームウィンドウ情報が更新されました。サイズ: {Size}", _gameWindowInfo.Size);
                
                // 位置とサイズを再計算して適用
                var oldPositionInfo = _currentPositionInfo;
                _currentPositionInfo = CalculatePositionAndSize();
                
                // 変更があれば通知
                if (oldPositionInfo.Position != _currentPositionInfo.Position || 
                    oldPositionInfo.Size != _currentPositionInfo.Size)
                {
                    PositionUpdated?.Invoke(this, new OverlayPositionUpdatedEventArgs(
                        oldPositionInfo,
                        _currentPositionInfo,
                        PositionUpdateReason.GameWindowUpdated));
                }
            }
        }
        
        /// <inheritdoc />
        public OverlayPositionInfo CalculatePositionAndSize()
        {
            lock (_syncLock)
            {
                var result = new OverlayPositionInfo
                {
                    GameWindowInfo = _gameWindowInfo,
                    AppliedPositionMode = PositionMode,
                    AppliedSizeMode = SizeMode
                };
                
                // 位置の計算
                CalculatePosition(result);
                
                // サイズの計算
                CalculateSize(result);
                
                // 制約の適用
                ApplyConstraints(result);
                
                return result;
            }
        }
        
        /// <inheritdoc />
        public void ApplyPositionAndSize(IOverlayWindow overlayWindow)
        {
            if (overlayWindow == null)
                throw new ArgumentNullException(nameof(overlayWindow));
                
            overlayWindow.Position = _currentPositionInfo.Position;
            overlayWindow.Size = _currentPositionInfo.Size;
            
            _logger?.LogDebug("オーバーレイウィンドウに位置とサイズが適用されました。位置: {Position}, サイズ: {Size}",
                _currentPositionInfo.Position, _currentPositionInfo.Size);
        }
        
        /// <summary>
        /// テキストを測定します
        /// </summary>
        /// <param name="text">測定するテキスト</param>
        /// <returns>テキスト測定情報</returns>
        private TextMeasurementInfo MeasureText(string text)
        {
            // 測定オプションを設定
            var options = new TextMeasurementOptions
            {
                FontFamily = "Yu Gothic UI",
                FontSize = 16,
                FontWeight = FontWeight.Normal,
                MaxWidth = MaxSize.Width,
                Padding = new Thickness(10)
            };
            
            // テキストを測定
            var result = _textMeasurer.MeasureText(text, options);
            
            return new TextMeasurementInfo
            {
                TextSize = result.Size,
                LineCount = result.LineCount,
                CharacterCount = text.Length,
                MinFontSize = 12,
                RecommendedFontSize = 16
            };
        }
        
        /// <summary>
        /// 位置を計算します
        /// </summary>
        /// <param name="positionInfo">位置情報</param>
        private void CalculatePosition(OverlayPositionInfo positionInfo)
        {
            switch (PositionMode)
            {
                case OverlayPositionMode.TextRegionBased:
                    CalculateTextRegionBasedPosition(positionInfo);
                    break;
                    
                case OverlayPositionMode.Fixed:
                    positionInfo.Position = FixedPosition;
                    break;
                    
                case OverlayPositionMode.Smart:
                    CalculateSmartPosition(positionInfo);
                    break;
                    
                case OverlayPositionMode.Follow:
                    CalculateFollowPosition(positionInfo);
                    break;
                    
                case OverlayPositionMode.Relative:
                    CalculateRelativePosition(positionInfo);
                    break;
            }
            
            // オフセットを適用
            positionInfo.Position = new Point(
                positionInfo.Position.X + PositionOffset.X,
                positionInfo.Position.Y + PositionOffset.Y);
        }
        
        /// <summary>
        /// サイズを計算します
        /// </summary>
        /// <param name="positionInfo">位置情報</param>
        private void CalculateSize(OverlayPositionInfo positionInfo)
        {
            switch (SizeMode)
            {
                case OverlaySizeMode.TextRegionBased:
                    CalculateTextRegionBasedSize(positionInfo);
                    break;
                    
                case OverlaySizeMode.Fixed:
                    positionInfo.Size = FixedSize;
                    break;
                    
                case OverlaySizeMode.ContentBased:
                    CalculateContentBasedSize(positionInfo);
                    break;
                    
                case OverlaySizeMode.Relative:
                    CalculateRelativeSize(positionInfo);
                    break;
            }
            
            // スケールを適用
            positionInfo.Size = new Size(
                positionInfo.Size.Width * SizeScale,
                positionInfo.Size.Height * SizeScale);
        }
        
        /// <summary>
        /// 制約を適用します
        /// </summary>
        /// <param name="positionInfo">位置情報</param>
        private void ApplyConstraints(OverlayPositionInfo positionInfo)
        {
            var applied = false;
            
            // 最小サイズの制約
            if (positionInfo.Size.Width < MinSize.Width)
            {
                positionInfo.Size = new Size(MinSize.Width, positionInfo.Size.Height);
                applied = true;
            }
            
            if (positionInfo.Size.Height < MinSize.Height)
            {
                positionInfo.Size = new Size(positionInfo.Size.Width, MinSize.Height);
                applied = true;
            }
            
            // 最大サイズの制約
            if (positionInfo.Size.Width > MaxSize.Width)
            {
                positionInfo.Size = new Size(MaxSize.Width, positionInfo.Size.Height);
                applied = true;
            }
            
            if (positionInfo.Size.Height > MaxSize.Height)
            {
                positionInfo.Size = new Size(positionInfo.Size.Width, MaxSize.Height);
                applied = true;
            }
            
            // 画面境界の制約
            if (_gameWindowInfo.ClientSize.Width > 0 && _gameWindowInfo.ClientSize.Height > 0)
            {
                // 左端
                if (positionInfo.Position.X < 0)
                {
                    positionInfo.Position = new Point(0, positionInfo.Position.Y);
                    applied = true;
                }
                
                // 上端
                if (positionInfo.Position.Y < 0)
                {
                    positionInfo.Position = new Point(positionInfo.Position.X, 0);
                    applied = true;
                }
                
                // 右端
                if (positionInfo.Position.X + positionInfo.Size.Width > _gameWindowInfo.ClientSize.Width)
                {
                    positionInfo.Position = new Point(
                        _gameWindowInfo.ClientSize.Width - positionInfo.Size.Width,
                        positionInfo.Position.Y);
                    applied = true;
                }
                
                // 下端
                if (positionInfo.Position.Y + positionInfo.Size.Height > _gameWindowInfo.ClientSize.Height)
                {
                    positionInfo.Position = new Point(
                        positionInfo.Position.X,
                        _gameWindowInfo.ClientSize.Height - positionInfo.Size.Height);
                    applied = true;
                }
            }
            
            positionInfo.ConstraintsApplied = applied;
        }
        
        // 各種位置計算アルゴリズム
        private void CalculateTextRegionBasedPosition(OverlayPositionInfo positionInfo) { /* 実装 */ }
        private void CalculateSmartPosition(OverlayPositionInfo positionInfo) { /* 実装 */ }
        private void CalculateFollowPosition(OverlayPositionInfo positionInfo) { /* 実装 */ }
        private void CalculateRelativePosition(OverlayPositionInfo positionInfo) { /* 実装 */ }
        
        // 各種サイズ計算アルゴリズム
        private void CalculateTextRegionBasedSize(OverlayPositionInfo positionInfo) { /* 実装 */ }
        private void CalculateContentBasedSize(OverlayPositionInfo positionInfo) { /* 実装 */ }
        private void CalculateRelativeSize(OverlayPositionInfo positionInfo) { /* 実装 */ }
    }
    
    /// <summary>
    /// テキスト測定オプション
    /// </summary>
    public class TextMeasurementOptions
    {
        /// <summary>
        /// フォントファミリー
        /// </summary>
        public string FontFamily { get; set; } = "Yu Gothic UI";
        
        /// <summary>
        /// フォントサイズ
        /// </summary>
        public double FontSize { get; set; } = 16;
        
        /// <summary>
        /// フォントウェイト
        /// </summary>
        public FontWeight FontWeight { get; set; } = FontWeight.Normal;
        
        /// <summary>
        /// 最大幅
        /// </summary>
        public double MaxWidth { get; set; } = double.PositiveInfinity;
        
        /// <summary>
        /// パディング
        /// </summary>
        public Thickness Padding { get; set; } = new Thickness(0);
    }
    
    /// <summary>
    /// テキスト測定結果
    /// </summary>
    public class TextMeasurementResult
    {
        /// <summary>
        /// サイズ
        /// </summary>
        public Size Size { get; set; }
        
        /// <summary>
        /// 行数
        /// </summary>
        public int LineCount { get; set; }
    }
    
    /// <summary>
    /// テキスト測定インターフェース
    /// </summary>
    public interface ITextMeasurer
    {
        /// <summary>
        /// テキストを測定します
        /// </summary>
        /// <param name="text">測定するテキスト</param>
        /// <param name="options">測定オプション</param>
        /// <returns>測定結果</returns>
        TextMeasurementResult MeasureText(string text, TextMeasurementOptions options);
    }
}
```

## オーバーレイ位置の動作例（TextRegionBasedモード）
```csharp
private void CalculateTextRegionBasedPosition(OverlayPositionInfo positionInfo)
{
    // 現在の翻訳情報がない場合はデフォルト位置を使用
    if (_currentTranslation == null)
    {
        positionInfo.Position = new Point(
            _gameWindowInfo.ClientSize.Width / 4,
            _gameWindowInfo.ClientSize.Height / 4);
        return;
    }
    
    // 関連するテキスト領域を探す
    TextRegion? sourceRegion = _currentTranslation.SourceRegion;
    
    // 関連するテキスト領域がない場合は最新のテキスト領域を使用
    if (sourceRegion == null && _textRegions.Count > 0)
    {
        // より適切なロジックに置き換える（例：時間的に近い領域など）
        sourceRegion = _textRegions.LastOrDefault();
    }
    
    if (sourceRegion != null)
    {
        // 関連するテキスト領域の下に配置
        positionInfo.Position = new Point(
            sourceRegion.Bounds.X,
            sourceRegion.Bounds.Y + sourceRegion.Bounds.Height + 10);
            
        positionInfo.SourceTextRegion = sourceRegion;
    }
    else
    {
        // テキスト領域がない場合はデフォルト位置
        positionInfo.Position = new Point(
            _gameWindowInfo.ClientSize.Width / 4,
            _gameWindowInfo.ClientSize.Height / 4);
    }
}
```

## 実装上の注意点
- ウィンドウ状態変化に対する高速な検出と応答
- 適切なテキスト測定とレイアウト計算のパフォーマンス最適化
- 複数のテキスト領域から関連する領域を正確に特定するアルゴリズム
- オーバーレイが邪魔にならないよう、ゲーム内の重要コンテンツを避ける配置ロジック
- 特に動きの速いゲームでの位置更新の適切な頻度の調整
- マルチモニター環境でのDPIスケーリングの適切な処理
- スレッドセーフな実装とイベント通知の最適化
- リソース効率を考慮した実装（特に測定処理の効率化）

## 関連Issue/参考
- 親Issue: #11 オーバーレイウィンドウ
- 依存Issue: #11-1 透過ウィンドウとクリックスルー機能の実装
- 関連Issue: #7 PaddleOCRの統合
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\overlay-window.md
- 参照: E:\dev\Baketa\docs\3-architecture\ocr\text-detection-algorithms.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.5 非同期メソッドには少なくとも1つの await を含める)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`

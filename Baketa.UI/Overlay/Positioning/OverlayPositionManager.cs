using System.Collections.Concurrent;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Overlay.Positioning;
using Baketa.Core.UI.Overlay;
using Baketa.Core.UI.Geometry;
using Baketa.UI.Overlay.MultiMonitor;
using Baketa.UI.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Overlay.Positioning;

/// <summary>
/// オーバーレイ位置・サイズ管理システムの実装クラス
/// OCR検出領域ベースの自動配置、翻訳テキスト量対応の自動サイズ調整、
/// ゲームウィンドウ状態変化への対応を実現します。
/// </summary>
public sealed class OverlayPositionManager : IOverlayPositionManager
{
    private readonly ILogger<OverlayPositionManager> _logger;
    private readonly MultiMonitorOverlayManager _multiMonitorManager;
    private readonly ITextMeasurementService _textMeasurementService;
    private readonly SemaphoreSlim _updateSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TranslationInfo> _activeTranslations = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // 現在の状態
#pragma warning disable IDE0032 // 自動プロパティを使用する - これらのフィールドは複数箇所で読み書きされるため適用不可
    private Baketa.Core.UI.Geometry.CorePoint _currentPosition = Baketa.Core.UI.Geometry.CorePoint.Zero;
    private Baketa.Core.UI.Geometry.CoreSize _currentSize = new(600, 100);
#pragma warning restore IDE0032
    private IReadOnlyList<TextRegion> _currentTextRegions = [];
    private TranslationInfo? _currentTranslation;
    private GameWindowInfo? _currentGameWindow;
    
    // 設定プロパティ
    private OverlayPositionMode _positionMode = OverlayPositionMode.OcrRegionBased;
    private OverlaySizeMode _sizeMode = OverlaySizeMode.ContentBased;
    private Baketa.Core.UI.Geometry.CorePoint _fixedPosition = new(100, 100);
    private Baketa.Core.UI.Geometry.CoreSize _fixedSize = new(600, 100);
    private Baketa.Core.UI.Geometry.CoreVector _positionOffset = Baketa.Core.UI.Geometry.CoreVector.Zero;
    private Baketa.Core.UI.Geometry.CoreSize _maxSize = new(1200, 800);
    private Baketa.Core.UI.Geometry.CoreSize _minSize = new(200, 60);
    
    private bool _disposed;
    
    /// <inheritdoc/>
    public OverlayPositionMode PositionMode
    {
        get => _positionMode;
        set
        {
            if (_positionMode != value)
            {
                _positionMode = value;
                _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
            }
        }
    }
    
    /// <inheritdoc/>
    public OverlaySizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (_sizeMode != value)
            {
                _sizeMode = value;
                _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
            }
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CorePoint FixedPosition
    {
        get => _fixedPosition;
        set
        {
            if (_fixedPosition != value)
            {
                _fixedPosition = value;
                if (_positionMode == OverlayPositionMode.Fixed)
                {
                    _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
                }
            }
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CoreSize FixedSize
    {
        get => _fixedSize;
        set
        {
            if (_fixedSize != value)
            {
                _fixedSize = value;
                if (_sizeMode == OverlaySizeMode.Fixed)
                {
                    _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
                }
            }
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CoreVector PositionOffset
    {
        get => _positionOffset;
        set
        {
            if (_positionOffset != value)
            {
                _positionOffset = value;
                if (_positionMode == OverlayPositionMode.OcrRegionBased)
                {
                    _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
                }
            }
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CoreSize MaxSize
    {
        get => _maxSize;
        set
        {
            if (_maxSize != value)
            {
                _maxSize = value;
                _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
            }
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CoreSize MinSize
    {
        get => _minSize;
        set
        {
            if (_minSize != value)
            {
                _minSize = value;
                _ = Task.Run(async () => await RecalculatePositionAsync().ConfigureAwait(false));
            }
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CorePoint CurrentPosition => _currentPosition;
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CoreSize CurrentSize => _currentSize;
    
    /// <inheritdoc/>
    public event EventHandler<OverlayPositionUpdatedEventArgs>? PositionUpdated;
    
    /// <summary>
    /// 新しいOverlayPositionManagerを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="multiMonitorManager">マルチモニター管理システム</param>
    /// <param name="textMeasurementService">テキスト測定サービス</param>
    public OverlayPositionManager(
        ILogger<OverlayPositionManager> logger,
        MultiMonitorOverlayManager multiMonitorManager,
        ITextMeasurementService textMeasurementService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _multiMonitorManager = multiMonitorManager ?? throw new ArgumentNullException(nameof(multiMonitorManager));
        _textMeasurementService = textMeasurementService ?? throw new ArgumentNullException(nameof(textMeasurementService));
        
        _logger.LogDebug("OverlayPositionManager初期化完了");
    }
    
    /// <inheritdoc/>
    public async Task UpdateTextRegionsAsync(IReadOnlyList<TextRegion> textRegions, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(textRegions);
        
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        
        await _updateSemaphore.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        try
        {
            _currentTextRegions = textRegions;
            _logger.LogDebug("テキスト領域を更新しました: {Count}個", textRegions.Count);
            
            await RecalculatePositionAsync(PositionUpdateReason.OcrDetection, combinedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _updateSemaphore.Release();
        }
    }
    
    /// <inheritdoc/>
    public async Task UpdateTranslationInfoAsync(TranslationInfo translationInfo, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(translationInfo);
        
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        
        await _updateSemaphore.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        try
        {
            _currentTranslation = translationInfo;
            _activeTranslations.AddOrUpdate(translationInfo.TranslationId, translationInfo, (_, _) => translationInfo);
            
            _logger.LogDebug("翻訳情報を更新しました: {TranslationId}", translationInfo.TranslationId);
            
            await RecalculatePositionAsync(PositionUpdateReason.TranslationCompleted, combinedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _updateSemaphore.Release();
        }
    }
    
    /// <inheritdoc/>
    public async Task NotifyGameWindowUpdateAsync(GameWindowInfo gameWindowInfo, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gameWindowInfo);
        
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        
        await _updateSemaphore.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        try
        {
            var wasSignificantChange = _currentGameWindow == null ||
                                      !_currentGameWindow.WindowBounds.IntersectsWith(gameWindowInfo.WindowBounds) ||
                                      _currentGameWindow.Monitor.Handle != gameWindowInfo.Monitor.Handle;
            
            _currentGameWindow = gameWindowInfo;
            _logger.LogDebug("ゲームウィンドウ情報を更新しました: {Title}", gameWindowInfo.WindowTitle);
            
            if (wasSignificantChange)
            {
                await RecalculatePositionAsync(PositionUpdateReason.GameWindowChanged, combinedCts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _updateSemaphore.Release();
        }
    }
    
    /// <inheritdoc/>
    public async Task<OverlayPositionInfo> CalculatePositionAndSizeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        
        var monitor = _currentGameWindow?.Monitor ?? GetPrimaryMonitor();
        var positionInfo = new OverlayPositionInfo(
            Position: Baketa.Core.UI.Geometry.CorePoint.Zero,
            Size: Baketa.Core.UI.Geometry.CoreSize.Empty,
            SourceTextRegion: null,
            Monitor: monitor,
            CalculationMethod: PositionCalculationMethod.FallbackPosition
        );
        
        // サイズ計算
        var calculatedSize = await CalculateSizeAsync(combinedCts.Token).ConfigureAwait(false);
        positionInfo = positionInfo with { Size = calculatedSize };
        
        // 位置計算
        var (calculatedPosition, calculationMethod, sourceRegion) = await CalculatePositionAsync(calculatedSize, combinedCts.Token).ConfigureAwait(false);
        
        // 境界制約適用
        var constrainedPosition = ApplyBoundaryConstraints(calculatedPosition, calculatedSize, monitor);
        
        return positionInfo with
        {
            Position = constrainedPosition,
            SourceTextRegion = sourceRegion,
            CalculationMethod = calculationMethod
        };
    }
    
    /// <inheritdoc/>
    public async Task ApplyPositionAndSizeAsync(IOverlayWindow overlayWindow, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(overlayWindow);
        
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        
        var positionInfo = await CalculatePositionAndSizeAsync(combinedCts.Token).ConfigureAwait(false);
        
        if (positionInfo.IsValid)
        {
            // CorePoint/CoreSize から Baketa.Core.UI.Geometry.Point/Size に変換
            overlayWindow.Position = new Baketa.Core.UI.Geometry.Point(positionInfo.Position.X, positionInfo.Position.Y);
            overlayWindow.Size = new Baketa.Core.UI.Geometry.Size(positionInfo.Size.Width, positionInfo.Size.Height);
            
            _logger.LogDebug("オーバーレイ位置・サイズを適用しました: 位置={Position}, サイズ={Size}, 方法={Method}",
                positionInfo.Position, positionInfo.Size, positionInfo.CalculationMethod);
        }
        else
        {
            _logger.LogWarning("無効な位置情報のため、オーバーレイ位置・サイズの適用をスキップしました");
        }
    }
    
    /// <inheritdoc/>
    public Baketa.Core.UI.Geometry.CorePoint ApplyBoundaryConstraints(Baketa.Core.UI.Geometry.CorePoint position, Baketa.Core.UI.Geometry.CoreSize size, MonitorInfo monitor)
    {
        var workArea = new Baketa.Core.UI.Geometry.CoreRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, monitor.WorkArea.Height);
        
        var constrainedX = Math.Max(workArea.Left, Math.Min(position.X, workArea.Right - size.Width));
        var constrainedY = Math.Max(workArea.Top, Math.Min(position.Y, workArea.Bottom - size.Height));
        
        return new Baketa.Core.UI.Geometry.CorePoint(constrainedX, constrainedY);
    }
    
    /// <inheritdoc/>
    public bool IsPositionValid(Baketa.Core.UI.Geometry.CorePoint position, Baketa.Core.UI.Geometry.CoreSize size, MonitorInfo monitor)
    {
        var workArea = new Baketa.Core.UI.Geometry.CoreRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, monitor.WorkArea.Height);
        var overlayBounds = new Baketa.Core.UI.Geometry.CoreRect(position, size);
        
        // 少なくとも50%以上がワークエリア内にある場合を有効とする
        var intersection = workArea.Intersect(overlayBounds);
        return intersection.Width * intersection.Height >= overlayBounds.Width * overlayBounds.Height * 0.5;
    }
    
    /// <summary>
    /// 位置とサイズを再計算して更新イベントを発火します
    /// </summary>
    private async Task RecalculatePositionAsync(PositionUpdateReason reason = PositionUpdateReason.SettingsChanged, CancellationToken cancellationToken = default)
    {
        try
        {
            var previousPosition = _currentPosition;
            var previousSize = _currentSize;
            
            var positionInfo = await CalculatePositionAndSizeAsync(cancellationToken).ConfigureAwait(false);
            
            if (positionInfo.IsValid)
            {
                _currentPosition = positionInfo.Position;
                _currentSize = positionInfo.Size;
                
                // 変更があった場合のみイベントを発火
                if (previousPosition != _currentPosition || previousSize != _currentSize)
                {
                    var eventArgs = new OverlayPositionUpdatedEventArgs(
                        previousPosition,
                        _currentPosition,
                        previousSize,
                        _currentSize,
                        reason,
                        DateTimeOffset.Now
                    );
                    
                    PositionUpdated?.Invoke(this, eventArgs);
                    
                    _logger.LogInformation("オーバーレイ位置・サイズが更新されました: {PrevPos} → {NewPos}, {PrevSize} → {NewSize}, 理由: {Reason}",
                        previousPosition, _currentPosition, previousSize, _currentSize, reason);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "位置・サイズ再計算中に無効な操作エラーが発生しました");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "位置・サイズ再計算中に引数エラーが発生しました");
        }
        catch (OperationCanceledException)
        {
            // キャンセル例外は再スロー
            throw;
        }
    }
    
    /// <summary>
    /// サイズを計算します
    /// </summary>
    private async Task<Baketa.Core.UI.Geometry.CoreSize> CalculateSizeAsync(CancellationToken cancellationToken)
    {
        return SizeMode switch
        {
            OverlaySizeMode.ContentBased => await CalculateContentBasedSizeAsync(cancellationToken).ConfigureAwait(false),
            OverlaySizeMode.Fixed => FixedSize,
            _ => FixedSize
        };
    }
    
    /// <summary>
    /// コンテンツベースのサイズ計算
    /// </summary>
    private async Task<Baketa.Core.UI.Geometry.CoreSize> CalculateContentBasedSizeAsync(CancellationToken cancellationToken)
    {
        if (_currentTranslation?.MeasurementInfo is not { } measurementInfo || !measurementInfo.IsValid)
        {
            // 測定情報がない場合はテキスト測定を実行
            if (_currentTranslation is { IsValid: true })
            {
                var options = TextMeasurementOptions.Default with
                {
                    MaxWidth = MaxSize.Width - 20 // パディング考慮
                };
                
                var result = await _textMeasurementService.MeasureTextAsync(_currentTranslation.TranslatedText, options, cancellationToken).ConfigureAwait(false);
                
                if (result.IsValid)
                {
                    var optimalSize = new Baketa.Core.UI.Geometry.CoreSize(
                        Math.Max(result.Size.Width + 20, MinSize.Width),
                        Math.Max(result.Size.Height + 20, MinSize.Height)
                    );
                    
                    return new Baketa.Core.UI.Geometry.CoreSize(
                        Math.Min(optimalSize.Width, MaxSize.Width),
                        Math.Min(optimalSize.Height, MaxSize.Height)
                    );
                }
            }
            
            return FixedSize;
        }
        
        var recommendedSize = measurementInfo.RecommendedOverlaySize;
        return new Baketa.Core.UI.Geometry.CoreSize(
            Math.Clamp(recommendedSize.Width, MinSize.Width, MaxSize.Width),
            Math.Clamp(recommendedSize.Height, MinSize.Height, MaxSize.Height)
        );
    }
    
    /// <summary>
    /// 位置を計算します
    /// </summary>
    private async Task<(Baketa.Core.UI.Geometry.CorePoint Position, PositionCalculationMethod Method, TextRegion? SourceRegion)> CalculatePositionAsync(Baketa.Core.UI.Geometry.CoreSize size, CancellationToken cancellationToken)
    {
        return PositionMode switch
        {
            OverlayPositionMode.OcrRegionBased => await CalculateOcrBasedPositionAsync(size, cancellationToken).ConfigureAwait(false),
            OverlayPositionMode.Fixed => (FixedPosition, PositionCalculationMethod.FixedPosition, null),
            _ => (FixedPosition, PositionCalculationMethod.FallbackPosition, null)
        };
    }
    
    /// <summary>
    /// OCR領域ベースの位置計算
    /// </summary>
    private async Task<(Baketa.Core.UI.Geometry.CorePoint Position, PositionCalculationMethod Method, TextRegion? SourceRegion)> CalculateOcrBasedPositionAsync(Baketa.Core.UI.Geometry.CoreSize size, CancellationToken cancellationToken)
    {
        await Task.Yield(); // 非同期メソッドのための処理
        
        var sourceRegion = _currentTranslation?.SourceRegion ?? (_currentTextRegions.Count > 0 ? _currentTextRegions[0] : (TextRegion?)null);
        
        if (sourceRegion is not { IsValid: true })
        {
            // OCR領域がない場合はゲームウィンドウベースの安全位置
            var gameWindow = _currentGameWindow;
            if (gameWindow != null)
            {
                var safePosition = new Baketa.Core.UI.Geometry.CorePoint(
                    gameWindow.ClientPosition.X + gameWindow.ClientSize.Width / 4,
                    gameWindow.ClientPosition.Y + gameWindow.ClientSize.Height / 4
                );
                return (safePosition, PositionCalculationMethod.FallbackPosition, null);
            }
            
            return (FixedPosition, PositionCalculationMethod.FallbackPosition, null);
        }
        
        var monitor = _currentGameWindow?.Monitor ?? GetPrimaryMonitor();
        
        // 複数の候補位置を生成（手動計算で演算子問題を回避）
        var bounds = sourceRegion.Value.Bounds;
        var offsetX = PositionOffset.X;
        var offsetY = PositionOffset.Y;
        
        var candidatePositions = new[]
        {
            // 原文の直下（最も一般的）
            (Position: new Baketa.Core.UI.Geometry.CorePoint(bounds.X + offsetX, bounds.Bottom + 5 + offsetY),
             Method: PositionCalculationMethod.OcrBelowText),
            
            // 原文の上部（下部にスペースがない場合）
            (Position: new Baketa.Core.UI.Geometry.CorePoint(bounds.X + offsetX, bounds.Top - size.Height - 5 + offsetY),
             Method: PositionCalculationMethod.OcrAboveText),
            
            // 原文の右側（縦書きテキスト等）
            (Position: new Baketa.Core.UI.Geometry.CorePoint(bounds.Right + 5 + offsetX, bounds.Y + offsetY),
             Method: PositionCalculationMethod.OcrRightOfText),
            
            // 原文の左側
            (Position: new Baketa.Core.UI.Geometry.CorePoint(bounds.Left - size.Width - 5 + offsetX, bounds.Y + offsetY),
             Method: PositionCalculationMethod.OcrLeftOfText)
        };
        
        // 最適な位置を選択
        foreach (var (position, method) in candidatePositions)
        {
            if (IsPositionValid(position, size, monitor))
            {
                return (position, method, sourceRegion);
            }
        }
        
        // すべての候補が無効な場合は制約を適用した安全位置
        var constrainedPosition = ApplyBoundaryConstraints(candidatePositions[0].Position, size, monitor);
        return (constrainedPosition, PositionCalculationMethod.FallbackPosition, sourceRegion);
    }
    
    /// <summary>
    /// プライマリモニターを取得します
    /// </summary>
    private MonitorInfo GetPrimaryMonitor()
    {
        // TODO: Issue #71のマルチモニター管理システムと連携
        // 現在は仮実装
        return new MonitorInfo(
            Handle: nint.Zero,
            Name: "Primary",
            DeviceId: "PRIMARY",
            Bounds: new Baketa.Core.UI.Geometry.Rect(0, 0, 1920, 1080),
            WorkArea: new Baketa.Core.UI.Geometry.Rect(0, 0, 1920, 1040),
            IsPrimary: true,
            DpiX: 96,
            DpiY: 96
        );
    }
    
    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        
        _updateSemaphore.Dispose();
        _cancellationTokenSource.Dispose();
        
        _logger.LogDebug("OverlayPositionManager破棄完了");
    }
}

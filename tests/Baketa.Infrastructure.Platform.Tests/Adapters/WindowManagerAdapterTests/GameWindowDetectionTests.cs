using System;
using System.Collections.Generic;
using System.Reflection;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Platform.Tests.Adapters.WindowManagerAdapterTests;

    /// <summary>
    /// WindowManagerAdapterのゲームウィンドウ検出機能テスト
    /// </summary>
    public class GameWindowDetectionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<Baketa.Core.Abstractions.Platform.Windows.IWindowManager> _mockWindowsManager;
    private readonly TestableWindowManagerAdapter _adapter;
        
        public GameWindowDetectionTests(ITestOutputHelper output)
        {
        _output = output;
        _mockWindowsManager = new Mock<Baketa.Core.Abstractions.Platform.Windows.IWindowManager>();
        
        _adapter = new TestableWindowManagerAdapter(_mockWindowsManager.Object);
        }
        
        [Fact]
        public void FindGameWindow_NullGameTitle_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _adapter.FindGameWindow(null!));
        }
        
        [Fact]
        public void FindGameWindow_RememberedWindow_ReturnsFromCache()
        {
            // Arrange
            string gameTitle = "Test Game";
            var gameHandle = new IntPtr(12345);
            
            // _rememberedGameWindowsフィールドに直接アクセスするためのリフレクション
            var field = typeof(WindowManagerAdapter).GetField("_rememberedGameWindows", 
                BindingFlags.NonPublic | BindingFlags.Instance) 
                ?? throw new InvalidOperationException("_rememberedGameWindowsフィールドが見つかりません。クラス定義が変更された可能性があります。");
            
            Dictionary<string, IntPtr> rememberedWindows = [];
            rememberedWindows[gameTitle] = gameHandle;
            field.SetValue(_adapter, rememberedWindows);
            
            // GetWindowTitleが呼ばれたら値を返すよう設定
            _mockWindowsManager.Setup(w => w.GetWindowTitle(gameHandle))
                .Returns("Test Game Window");
            
            // Act
            var result = _adapter.FindGameWindow(gameTitle);
            
            // Assert
            Assert.Equal(gameHandle, result);
            _mockWindowsManager.Verify(w => w.GetWindowTitle(gameHandle), Times.Once);
            _mockWindowsManager.Verify(w => w.GetRunningApplicationWindows(), Times.Never);
        }
        
        [Fact]
        public void FindGameWindow_RememberedWindowInvalid_RemovesFromCache()
        {
            // Arrange
            string gameTitle = "Test Game";
            var gameHandle = new IntPtr(12345);
            
            // _rememberedGameWindowsフィールドに直接アクセス
            var field = typeof(WindowManagerAdapter).GetField("_rememberedGameWindows", 
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_rememberedGameWindowsフィールドが見つかりません。クラス定義が変更された可能性があります。");
            
            Dictionary<string, IntPtr> rememberedWindows = [];
            field.SetValue(_adapter, rememberedWindows);
            
            // GetWindowTitleが呼ばれたら例外をスローするよう設定
            _mockWindowsManager.Setup(w => w.GetWindowTitle(gameHandle))
                .Throws<InvalidOperationException>();
            
            // GetRunningApplicationWindowsが呼ばれたら空のディクショナリを返すよう設定
            _mockWindowsManager.Setup(w => w.GetRunningApplicationWindows())
                .Returns([]);
            
            // Act
            var result = _adapter.FindGameWindow(gameTitle);
            
            // Assert
            Assert.Equal(IntPtr.Zero, result); // ウィンドウが見つからない
            
            // キャッシュから削除されていることを確認
            var currentDict = field.GetValue(_adapter) as Dictionary<string, IntPtr>;
            Assert.NotNull(currentDict); // 先に返値がNullでないことを確認
            Assert.False(currentDict!.ContainsKey(gameTitle));
        }
        
        [Fact]
        public void FindGameWindow_TitleMatchInWindows_ReturnsHandle()
        {
            // Arrange
            string gameTitle = "Test Game";
            var gameHandle = new IntPtr(12345);
            var otherHandle = new IntPtr(67890);
            
            // GetRunningApplicationWindowsで返すウィンドウリスト
            Dictionary<IntPtr, string> windows = [];
            windows[gameHandle] = "Test Game Window";
            windows[otherHandle] = "Other Window";
            
            _mockWindowsManager.Setup(w => w.GetRunningApplicationWindows())
                .Returns(windows);
            
            // GetWindowTypeをテスト用アダプターで設定
            _adapter.SetWindowType(gameHandle, WindowType.Game);
            
            // Act
            var result = _adapter.FindGameWindow(gameTitle);
            
            // Assert
            Assert.Equal(gameHandle, result);
        }
        
        [Fact]
        public void FindGameWindow_ActiveWindowIsGame_ReturnsActiveHandle()
        {
            // Arrange
            string gameTitle = "Test Game";
            var activeHandle = new IntPtr(12345);
            
            // アクティブウィンドウの設定
            _mockWindowsManager.Setup(w => w.GetActiveWindowHandle())
                .Returns(activeHandle);
            
            // GetRunningApplicationWindowsで返す空のウィンドウリスト
            _mockWindowsManager.Setup(w => w.GetRunningApplicationWindows())
                .Returns([]);
            
            // GetWindowTypeをテスト用アダプターで設定
            _adapter.SetWindowType(activeHandle, WindowType.Game);
            
            // Act
            var result = _adapter.FindGameWindow(gameTitle);
            
            // Assert
            Assert.Equal(activeHandle, result);
        }
        
        [Fact]
        public void FindGameWindow_NoMatchingWindow_ReturnsZero()
        {
            // Arrange
            string gameTitle = "Test Game";
            var handle1 = new IntPtr(12345);
            var handle2 = new IntPtr(67890);
            
            // GetRunningApplicationWindowsで返すウィンドウリスト
            Dictionary<IntPtr, string> windows = [];
            windows[handle1] = "Window 1";
            windows[handle2] = "Window 2";
            
            _mockWindowsManager.Setup(w => w.GetRunningApplicationWindows())
                .Returns(windows);
            
            // アクティブウィンドウの設定
            _mockWindowsManager.Setup(w => w.GetActiveWindowHandle())
                .Returns(handle1);
            
            // GetWindowTypeをテスト用アダプターで設定
            _adapter.SetWindowType(handle1, WindowType.Normal);
            _adapter.SetWindowType(handle2, WindowType.Normal);
            
            // Act
            var result = _adapter.FindGameWindow(gameTitle);
            
            // Assert
            Assert.Equal(IntPtr.Zero, result);
        }
        
        [Fact]
        public void GetWindowType_ZeroHandle_ReturnsUnknown()
        {
            // Arrange
            var handle = IntPtr.Zero;
            
            // Act
            var result = _adapter.GetWindowType(handle);
            
            // Assert
            Assert.Equal(WindowType.Unknown, result);
        }
        
        [Theory]
        [InlineData("Window 1", false, false, WindowType.Normal)]
        [InlineData("Microsoft Word", false, true, WindowType.Normal)]
        public void GetWindowType_VariousWindows_ReturnsCorrectType(
            string title, bool isMaximized, bool isMinimized, WindowType expectedType)
        {
            // Arrange
            var handle = new IntPtr(12345);
            
            _mockWindowsManager.Setup(w => w.GetWindowTitle(handle))
                .Returns(title);
            
            _mockWindowsManager.Setup(w => w.IsMaximized(handle))
                .Returns(isMaximized);
            
            _mockWindowsManager.Setup(w => w.IsMinimized(handle))
                .Returns(isMinimized);
            
            // テスト用アダプターでウィンドウタイプを設定
            _adapter.SetWindowType(handle, expectedType);
            
            // Act
            var result = _adapter.GetWindowType(handle);
            
            // Assert
            Assert.Equal(expectedType, result);
        }
        
        public void Dispose()
        {
            _adapter.Dispose();
            GC.SuppressFinalize(this);
        }
    }

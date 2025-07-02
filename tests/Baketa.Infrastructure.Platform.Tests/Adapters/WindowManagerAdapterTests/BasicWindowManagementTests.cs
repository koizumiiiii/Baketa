using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Platform.Tests.Adapters.WindowManagerAdapterTests;

    /// <summary>
    /// WindowManagerAdapterの基本ウィンドウ管理機能テスト
    /// </summary>
    public class BasicWindowManagementTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<Baketa.Core.Abstractions.Platform.Windows.IWindowManager> _mockWindowsManager;
        private readonly WindowManagerAdapter _adapter;
        private bool _disposed;
        
        public BasicWindowManagementTests(ITestOutputHelper output)
        {
            _output = output;
            _mockWindowsManager = new Mock<Baketa.Core.Abstractions.Platform.Windows.IWindowManager>();
            
            _adapter = new WindowManagerAdapter(_mockWindowsManager.Object);
        }
        
        [Fact]
        public void Constructor_NullWindowsManager_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new WindowManagerAdapter(null!));
        }
        
        [Fact]
        public void GetActiveWindowHandle_CallsUnderlyingImplementation()
        {
            // Arrange
            var expectedHandle = new IntPtr(12345);
            _mockWindowsManager.Setup(w => w.GetActiveWindowHandle())
                .Returns(expectedHandle);
            
            // Act
            var result = _adapter.GetActiveWindowHandle();
            
            // Assert
            Assert.Equal(expectedHandle, result);
            _mockWindowsManager.Verify(w => w.GetActiveWindowHandle(), Times.Once);
        }
        
        [Fact]
        public void FindWindowByTitle_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _adapter.FindWindowByTitle(null!));
        }
        
        [Fact]
        public void FindWindowByTitle_CallsUnderlyingImplementation()
        {
            // Arrange
            string title = "Test Window";
            var expectedHandle = new IntPtr(12345);
            _mockWindowsManager.Setup(w => w.FindWindowByTitle(title))
                .Returns(expectedHandle);
            
            // Act
            var result = _adapter.FindWindowByTitle(title);
            
            // Assert
            Assert.Equal(expectedHandle, result);
            _mockWindowsManager.Verify(w => w.FindWindowByTitle(title), Times.Once);
        }
        
        [Fact]
        public void FindWindowByClass_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _adapter.FindWindowByClass(null!));
        }
        
        [Fact]
        public void FindWindowByClass_CallsUnderlyingImplementation()
        {
            // Arrange
            string className = "TestWindowClass";
            var expectedHandle = new IntPtr(12345);
            _mockWindowsManager.Setup(w => w.FindWindowByClass(className))
                .Returns(expectedHandle);
            
            // Act
            var result = _adapter.FindWindowByClass(className);
            
            // Assert
            Assert.Equal(expectedHandle, result);
            _mockWindowsManager.Verify(w => w.FindWindowByClass(className), Times.Once);
        }
        
        [Fact]
        public void GetWindowBounds_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var expectedBounds = new Rectangle(10, 20, 800, 600);
            _mockWindowsManager.Setup(w => w.GetWindowBounds(handle))
                .Returns(expectedBounds);
            
            // Act
            var result = _adapter.GetWindowBounds(handle);
            
            // Assert
            Assert.Equal(expectedBounds, result);
            _mockWindowsManager.Verify(w => w.GetWindowBounds(handle), Times.Once);
        }
        
        [Fact]
        public void GetClientBounds_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var expectedBounds = new Rectangle(0, 0, 780, 580);
            _mockWindowsManager.Setup(w => w.GetClientBounds(handle))
                .Returns(expectedBounds);
            
            // Act
            var result = _adapter.GetClientBounds(handle);
            
            // Assert
            Assert.Equal(expectedBounds, result);
            _mockWindowsManager.Verify(w => w.GetClientBounds(handle), Times.Once);
        }
        
        [Fact]
        public void GetWindowTitle_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var expectedTitle = "Test Window Title";
            _mockWindowsManager.Setup(w => w.GetWindowTitle(handle))
                .Returns(expectedTitle);
            
            // Act
            var result = _adapter.GetWindowTitle(handle);
            
            // Assert
            Assert.Equal(expectedTitle, result);
            _mockWindowsManager.Verify(w => w.GetWindowTitle(handle), Times.Once);
        }
        
        [Fact]
        public void IsMinimized_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var expected = true;
            _mockWindowsManager.Setup(w => w.IsMinimized(handle))
                .Returns(expected);
            
            // Act
            var result = _adapter.IsMinimized(handle);
            
            // Assert
            Assert.Equal(expected, result);
            _mockWindowsManager.Verify(w => w.IsMinimized(handle), Times.Once);
        }
        
        [Fact]
        public void IsMaximized_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var expected = true;
            _mockWindowsManager.Setup(w => w.IsMaximized(handle))
                .Returns(expected);
            
            // Act
            var result = _adapter.IsMaximized(handle);
            
            // Assert
            Assert.Equal(expected, result);
            _mockWindowsManager.Verify(w => w.IsMaximized(handle), Times.Once);
        }
        
        [Fact]
        public void SetWindowBounds_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var bounds = new Rectangle(10, 20, 800, 600);
            var expected = true;
            _mockWindowsManager.Setup(w => w.SetWindowBounds(handle, bounds))
                .Returns(expected);
            
            // Act
            var result = _adapter.SetWindowBounds(handle, bounds);
            
            // Assert
            Assert.Equal(expected, result);
            _mockWindowsManager.Verify(w => w.SetWindowBounds(handle, bounds), Times.Once);
        }
        
        [Fact]
        public void BringWindowToFront_CallsUnderlyingImplementation()
        {
            // Arrange
            var handle = new IntPtr(12345);
            var expected = true;
            _mockWindowsManager.Setup(w => w.BringWindowToFront(handle))
                .Returns(expected);
            
            // Act
            var result = _adapter.BringWindowToFront(handle);
            
            // Assert
            Assert.Equal(expected, result);
            _mockWindowsManager.Verify(w => w.BringWindowToFront(handle), Times.Once);
        }
        
        [Fact]
        public void GetRunningApplicationWindows_ReturnsWindowInfoCollection()
        {
            // Arrange
            var rawWindows = new Dictionary<IntPtr, string>
            {
                { new IntPtr(12345), "Window 1" },
                { new IntPtr(67890), "Window 2" }
            };
            
            _mockWindowsManager.Setup(w => w.GetRunningApplicationWindows())
                .Returns(rawWindows);
            
            // Act
            var result = _adapter.GetRunningApplicationWindows();
            
            // Assert
            Assert.NotNull(result);
            Assert.IsAssignableFrom<IReadOnlyCollection<WindowInfo>>(result);
            Assert.Equal(rawWindows.Count, result.Count);
            _mockWindowsManager.Verify(w => w.GetRunningApplicationWindows(), Times.Once);
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _adapter?.Dispose();
                }
                
                _disposed = true;
            }
        }
        
        ~BasicWindowManagementTests()
        {
            Dispose(false);
        }
    }

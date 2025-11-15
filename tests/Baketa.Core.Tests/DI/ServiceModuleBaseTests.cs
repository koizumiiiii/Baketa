using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Baketa.Core.Tests.DI;

/// <summary>
/// ServiceModuleBase クラスの単体テスト
/// </summary>
public class ServiceModuleBaseTests
{
    /// <summary>
    /// 依存関係のないモジュールが正常に登録できることをテスト
    /// </summary>
    [Fact]
    public void RegisterWithDependencies_WithoutDependencies_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var testModule = new TestModuleWithoutDependencies();
        var registeredModules = new HashSet<Type>();

        // Act
        testModule.RegisterWithDependencies(services, registeredModules, new Stack<Type>());

        // Assert
        Assert.Contains(typeof(TestModuleWithoutDependencies), registeredModules);
        Assert.True(testModule.RegisterServicesCalled);
    }

    /// <summary>
    /// 依存関係のあるモジュールが正常に登録でき、依存モジュールも登録されることをテスト
    /// </summary>
    [Fact]
    public void RegisterWithDependencies_WithDependencies_RegistersAllModules()
    {
        // Arrange
        var services = new ServiceCollection();
        var testModule = new TestModuleWithDependencies();
        var registeredModules = new HashSet<Type>();

        // Act
        testModule.RegisterWithDependencies(services, registeredModules, new Stack<Type>());

        // Assert
        Assert.Contains(typeof(TestModuleWithDependencies), registeredModules);
        Assert.Contains(typeof(TestModuleWithoutDependencies), registeredModules);
        Assert.True(testModule.RegisterServicesCalled);
    }

    /// <summary>
    /// 循環依存があるモジュールの場合、例外が発生することをテスト
    /// </summary>
    [Fact]
    public void RegisterWithDependencies_WithCircularDependencies_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        var testModule = new TestModuleWithCircularDependencyA();
        var registeredModules = new HashSet<Type>();

        // Act & Assert
        var exception = Assert.Throws<CircularDependencyException>(() =>
            testModule.RegisterWithDependencies(services, registeredModules, new Stack<Type>()));

        Assert.Contains("循環依存が検出", exception.Message);
        Assert.NotNull(exception.DependencyCycle);
        Assert.Contains(typeof(TestModuleWithCircularDependencyA), exception.DependencyCycle);
        Assert.Contains(typeof(TestModuleWithCircularDependencyB), exception.DependencyCycle);
    }

    #region テスト用モジュールクラス

    /// <summary>
    /// 依存関係のないテスト用モジュール
    /// </summary>
    private sealed class TestModuleWithoutDependencies : ServiceModuleBase
    {
        public bool RegisterServicesCalled { get; private set; }

        public override void RegisterServices(IServiceCollection services)
        {
            RegisterServicesCalled = true;
        }
    }

    /// <summary>
    /// 依存関係のあるテスト用モジュール
    /// </summary>
    private sealed class TestModuleWithDependencies : ServiceModuleBase
    {
        public bool RegisterServicesCalled { get; private set; }

        public override void RegisterServices(IServiceCollection services)
        {
            RegisterServicesCalled = true;
        }

        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(TestModuleWithoutDependencies);
        }
    }

    /// <summary>
    /// 循環依存関係のあるテスト用モジュールA
    /// </summary>
    private sealed class TestModuleWithCircularDependencyA : ServiceModuleBase
    {
        public override void RegisterServices(IServiceCollection services)
        {
        }

        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(TestModuleWithCircularDependencyB);
        }
    }

    /// <summary>
    /// 循環依存関係のあるテスト用モジュールB
    /// </summary>
    private sealed class TestModuleWithCircularDependencyB : ServiceModuleBase
    {
        public override void RegisterServices(IServiceCollection services)
        {
        }

        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(TestModuleWithCircularDependencyA);
        }
    }

    #endregion
}

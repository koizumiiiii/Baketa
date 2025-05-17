using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Baketa.Core.Tests.DI
{
    /// <summary>
    /// モジュール優先順位機能の単体テスト
    /// </summary>
    public class ModulePriorityTests
    {
        /// <summary>
        /// 優先順位に基づいてモジュールが適切にソートされることをテスト
        /// </summary>
        [Fact]
        public void SortModules_WithPriorities_SortsCorrectly()
        {
            // Arrange
            var modules = new List<IServiceModule>
            {
                new UiModuleWithPriority(),
                new CoreModuleWithPriority(),
                new InfrastructureModuleWithPriority(),
                new CustomModuleWithoutPriority()
            };

            // Act - 優先度でソート
            var sortedModules = modules
                .Select(m => new 
                {
                    Module = m,
                    Priority = m.GetType().GetCustomAttribute<ModulePriorityAttribute>()?.Priority 
                             ?? ModulePriority.Custom
                })
                .OrderByDescending(x => (int)x.Priority)
                .Select(x => x.Module)
                .ToArray();

            // Assert - 期待する順序: Core, Infrastructure, UI, Custom
            Assert.Equal(4, sortedModules.Length);
            Assert.IsType<CoreModuleWithPriority>(sortedModules[0]);
            Assert.IsType<InfrastructureModuleWithPriority>(sortedModules[1]);
            Assert.IsType<UiModuleWithPriority>(sortedModules[2]);
            Assert.IsType<CustomModuleWithoutPriority>(sortedModules[3]);
        }

        #region テスト用モジュールクラス

        /// <summary>
        /// Core優先度を持つテスト用モジュール
        /// </summary>
        [ModulePriority(ModulePriority.Core)]
        private sealed class CoreModuleWithPriority : ServiceModuleBase
        {
            public override void RegisterServices(IServiceCollection services)
            {
            }
        }

        /// <summary>
        /// Infrastructure優先度を持つテスト用モジュール
        /// </summary>
        [ModulePriority(ModulePriority.Infrastructure)]
        private sealed class InfrastructureModuleWithPriority : ServiceModuleBase
        {
            public override void RegisterServices(IServiceCollection services)
            {
            }
        }

        /// <summary>
        /// UI優先度を持つテスト用モジュール
        /// </summary>
        [ModulePriority(ModulePriority.UI)]
        private sealed class UiModuleWithPriority : ServiceModuleBase
        {
            public override void RegisterServices(IServiceCollection services)
            {
            }
        }

        /// <summary>
        /// 優先度を指定していないテスト用モジュール
        /// </summary>
        private sealed class CustomModuleWithoutPriority : ServiceModuleBase
        {
            public override void RegisterServices(IServiceCollection services)
            {
            }
        }

        #endregion
    }
}
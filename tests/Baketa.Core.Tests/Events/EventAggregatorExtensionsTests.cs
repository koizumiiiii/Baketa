using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Baketa.Core.Tests.Events;

    /// <summary>
    /// イベント集約機構の拡張メソッドのテスト
    /// </summary>
    public class EventAggregatorExtensionsTests
    {
        /// <summary>
        /// DIコンテナへの登録テスト
        /// </summary>
        [Fact]
        public void AddEventAggregator_RegistersAsSingleton()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Act
            services.AddEventAggregator();
            var serviceProvider = services.BuildServiceProvider();
            
            // Assert
            var eventAggregator1 = serviceProvider.GetRequiredService<IEventAggregator>();
            var eventAggregator2 = serviceProvider.GetRequiredService<IEventAggregator>();
            
            // シングルトンとして登録されているので、同じインスタンスであること
            Assert.NotNull(eventAggregator1);
            Assert.NotNull(eventAggregator2);
            Assert.Same(eventAggregator1, eventAggregator2);
            Assert.IsType<EventAggregator>(eventAggregator1);
        }
    }

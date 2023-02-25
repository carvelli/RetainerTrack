using System.IO;
using Dalamud.Data;
using Dalamud.Extensions.MicrosoftLogging;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Gui;
using Dalamud.Game.Network;
using Dalamud.Plugin;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetainerTrack.Database;
using RetainerTrack.Handlers;

namespace RetainerTrack
{
    // ReSharper disable once UnusedType.Global
    internal sealed class RetainerTrackPlugin : IDalamudPlugin
    {
        private readonly ServiceProvider? _serviceProvider;

        public string Name => "RetainerTrack";

        public RetainerTrackPlugin(
            DalamudPluginInterface pluginInterface,
            GameNetwork gameNetwork,
            DataManager dataManager,
            Framework framework,
            ClientState clientState,
            GameGui gameGui)
        {
            ServiceCollection serviceCollection = new();
            serviceCollection.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace)
                .ClearProviders()
                .AddDalamudLogger(this));
            serviceCollection.AddSingleton<IDalamudPlugin>(this);
            serviceCollection.AddSingleton(pluginInterface);
            serviceCollection.AddSingleton(gameNetwork);
            serviceCollection.AddSingleton(dataManager);
            serviceCollection.AddSingleton(framework);
            serviceCollection.AddSingleton(clientState);
            serviceCollection.AddSingleton(gameGui);

            serviceCollection.AddSingleton<LiteDatabase>(_ =>
                new LiteDatabase(new ConnectionString
                {
                    Filename = Path.Join(pluginInterface.GetPluginConfigDirectory(), "retainer-data.litedb"),
                    Connection = ConnectionType.Direct,
                    Upgrade = true,
                }));

            serviceCollection.AddSingleton<PersistenceContext>();
            serviceCollection.AddSingleton<NetworkHandler>();
            serviceCollection.AddSingleton<PartyHandler>();
            serviceCollection.AddSingleton<MarketBoardUiHandler>();

            _serviceProvider = serviceCollection.BuildServiceProvider();

            LiteDatabase liteDatabase = _serviceProvider.GetRequiredService<LiteDatabase>();
            liteDatabase.GetCollection<Retainer>()
                .EnsureIndex(x => x.Id);
            liteDatabase.GetCollection<Player>()
                .EnsureIndex(x => x.Id);

            _serviceProvider.GetRequiredService<PartyHandler>();
            _serviceProvider.GetRequiredService<NetworkHandler>();
            _serviceProvider.GetRequiredService<MarketBoardUiHandler>();
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }
    }
}

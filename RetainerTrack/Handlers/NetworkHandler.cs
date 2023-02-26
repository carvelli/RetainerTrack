using System;
using System.Threading.Tasks;
using Dalamud.Data;
using Dalamud.Game.ClientState;
using Dalamud.Game.Network;
using Dalamud.Game.Network.Structures;
using Microsoft.Extensions.Logging;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace RetainerTrack.Handlers
{
    internal sealed class NetworkHandler : IDisposable
    {
        private readonly ILogger<NetworkHandler> _logger;
        private readonly GameNetwork _gameNetwork;
        private readonly DataManager _dataManager;
        private readonly ClientState _clientState;
        private readonly PersistenceContext _persistenceContext;

        private readonly ushort? _contentIdMappingOpCode;

        public unsafe NetworkHandler(
            ILogger<NetworkHandler> logger,
            GameNetwork gameNetwork,
            DataManager dataManager,
            ClientState clientState,
            PersistenceContext persistenceContext)
        {
            _logger = logger;
            _gameNetwork = gameNetwork;
            _dataManager = dataManager;
            _clientState = clientState;
            _persistenceContext = persistenceContext;

            if (Framework.Instance()->GameVersion.Base == "2023.02.03.0000.0000")
                _contentIdMappingOpCode = 0x01C4;
            else
            {
                _logger.LogWarning("Not tracking content id mappings, unsupported game version {Version}",
                    Framework.Instance()->GameVersion.Base);
            }

            _gameNetwork.NetworkMessage += NetworkMessage;
        }

        public void Dispose()
        {
            _gameNetwork.NetworkMessage -= NetworkMessage;
        }

        private void NetworkMessage(nint dataPtr, ushort opcode, uint sourceActorId, uint targetActorId,
            NetworkMessageDirection direction)
        {
            if (direction != NetworkMessageDirection.ZoneDown || !_dataManager.IsDataReady)
                return;

            if (opcode == _dataManager.ServerOpCodes["MarketBoardOfferings"])
            {
                ushort worldId = (ushort?)_clientState.LocalPlayer?.CurrentWorld.Id ?? 0;
                if (worldId == 0)
                {
                    _logger.LogInformation("Skipping market board handler, current world unknown");
                    return;
                }

                var listings = MarketBoardCurrentOfferings.Read(dataPtr);
                Task.Run(() => _persistenceContext.HandleMarketBoardPage(listings, worldId));
            }
            else if (opcode == _contentIdMappingOpCode)
            {
                var mapping = ContentIdToName.ReadFromNetworkPacket(dataPtr);
                _logger.LogTrace("Content id {ContentId} belongs to player '{Name}'", mapping.ContentId,
                    !string.IsNullOrEmpty(mapping.PlayerName) ? mapping.PlayerName : "<unknown>");
                Task.Run(() => _persistenceContext.HandleContentIdMapping(mapping));
            }
        }
    }
}

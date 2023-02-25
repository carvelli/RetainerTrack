using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState;
using Dalamud.Game.Network.Structures;
using LiteDB;
using Microsoft.Extensions.Logging;
using RetainerTrack.Database;

namespace RetainerTrack.Handlers
{
    internal sealed class PersistenceContext
    {
        private readonly ILogger<PersistenceContext> _logger;
        private readonly ClientState _clientState;
        private readonly LiteDatabase _liteDatabase;
        private readonly ConcurrentDictionary<uint, ConcurrentDictionary<string, ulong>> _worldRetainerCache = new();
        private readonly ConcurrentDictionary<ulong, string> _playerNameCache = new();

        public PersistenceContext(ILogger<PersistenceContext> logger, ClientState clientState,
            LiteDatabase liteDatabase)
        {
            _logger = logger;
            _clientState = clientState;
            _liteDatabase = liteDatabase;

            var retainersByWorld = _liteDatabase.GetCollection<Retainer>().FindAll()
                .GroupBy(r => r.WorldId);
            foreach (var retainers in retainersByWorld)
            {
                var world = _worldRetainerCache.GetOrAdd(retainers.Key, _ => new());
                foreach (var retainer in retainers)
                {
                    if (retainer.Name != null)
                        world[retainer.Name] = retainer.OwnerContentId;
                }
            }

            foreach (var player in _liteDatabase.GetCollection<Player>().FindAll())
                _playerNameCache[player.Id] = player.Name ?? string.Empty;
        }

        public string GetCharacterNameOnCurrentWorld(string retainerName)
        {
            uint currentWorld = _clientState.LocalPlayer?.CurrentWorld.Id ?? 0;
            if (currentWorld == 0)
                return string.Empty;

            var currentWorldCache = _worldRetainerCache.GetOrAdd(currentWorld, _ => new());
            if (!currentWorldCache.TryGetValue(retainerName, out ulong playerContentId))
                return string.Empty;

            return _playerNameCache.TryGetValue(playerContentId, out string? playerName) ? playerName : string.Empty;
        }

        public void HandleMarketBoardPage(MarketBoardCurrentOfferings listings, ushort worldId)
        {
            try
            {
                var updates =
                    listings.ItemListings.DistinctBy(o => o.RetainerId)
                        .Where(l => l.RetainerId != 0)
                        .Where(l => l.RetainerOwnerId != 0)
                        .Select(l =>
                            new Retainer
                            {
                                Id = l.RetainerId,
                                Name = l.RetainerName,
                                WorldId = worldId,
                                OwnerContentId = l.RetainerOwnerId,
                            })
                        .ToList();
                _liteDatabase.GetCollection<Retainer>().Upsert(updates);
                foreach (var retainer in updates)
                {
                    if (!_playerNameCache.TryGetValue(retainer.OwnerContentId, out string? ownerName))
                        ownerName = retainer.OwnerContentId.ToString();
                    _logger.LogTrace("Retainer {RetainerName} belongs to {OwnerId}", retainer.Name,
                        ownerName);

                    if (retainer.Name != null)
                    {
                        var world = _worldRetainerCache.GetOrAdd(retainer.WorldId, _ => new());
                        world[retainer.Name] = retainer.OwnerContentId;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not persist retainer info from market board page");
            }
        }

        public void HandleContentIdMapping(ContentIdToName mapping)
            => HandleContentIdMapping(new List<ContentIdToName> { mapping });

        public void HandleContentIdMapping(IReadOnlyList<ContentIdToName> mappings)
        {
            try
            {
                var updates = mappings
                    .Where(mapping => mapping.ContentId != 0 && !string.IsNullOrEmpty(mapping.PlayerName))
                    .Select(mapping =>
                        new Player
                        {
                            Id = mapping.ContentId,
                            Name = mapping.PlayerName,
                        })
                    .ToList();
                _liteDatabase.GetCollection<Player>().Upsert(updates);
                foreach (var player in updates)
                    _playerNameCache[player.Id] = player.Name ?? string.Empty;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not persist multiple mappings");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game.Group;

namespace RetainerTrack.Handlers
{
    internal sealed class PartyHandler : IDisposable
    {
        private readonly Framework _framework;
        private readonly ClientState _clientState;
        private readonly PersistenceContext _persistenceContext;

        private long _lastUpdate = 0;

        public PartyHandler(Framework framework, ClientState clientState, PersistenceContext persistenceContext)
        {
            _framework = framework;
            _clientState = clientState;
            _persistenceContext = persistenceContext;

            _framework.Update += FrameworkUpdate;
        }

        private unsafe void FrameworkUpdate(Framework _)
        {
            long now = Environment.TickCount64;
            if (!_clientState.IsLoggedIn || _clientState.IsPvPExcludingDen || now - _lastUpdate < 180_000)
                return;

            _lastUpdate = now;

            // skip if we're not in an alliance, party members are handled via social list updates
            var groupManager = GroupManager.Instance();
            if (groupManager->AllianceFlags == 0x0)
                return;

            List<ContentIdToName> mappings = new();
            foreach (var allianceMember in groupManager->AllianceMembersSpan)
                HandlePartyMember(allianceMember, mappings);

            if (mappings.Count > 0)
                Task.Run(() => _persistenceContext.HandleContentIdMapping(mappings));
        }

        private unsafe void HandlePartyMember(PartyMember partyMember, List<ContentIdToName> contentIdToNames)
        {
            if (partyMember.ContentID == 0)
                return;

            string name = MemoryHelper.ReadStringNullTerminated((nint)partyMember.Name);
            if (string.IsNullOrEmpty(name))
                return;

            contentIdToNames.Add(new ContentIdToName
            {
                ContentId = (ulong)partyMember.ContentID,
                PlayerName = name,
            });
        }

        public void Dispose()
        {
            _framework.Update -= FrameworkUpdate;
        }
    }
}

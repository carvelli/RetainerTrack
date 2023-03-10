using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using Microsoft.Extensions.Logging;

namespace RetainerTrack.Handlers
{
    internal sealed unsafe class GameHooks : IDisposable
    {
        private readonly ILogger<GameHooks> _logger;
        private readonly PersistenceContext _persistenceContext;

        /// <summary>
        /// Processes the content id to character name packet, seen e.g. when you hover an item to retrieve the
        /// crafter's signature.
        /// </summary>
        private delegate int CharacterNameResultDelegate(nint a1, ulong contentId, char* playerName);

        private delegate nint SocialListResultDelegate(nint a1, nint dataPtr);

#pragma warning disable CS0649
        [Signature("40 53 48 83 EC 20 48 8B  D9 33 C9 45 33 C9", DetourName = nameof(ProcessCharacterNameResult))]
        private Hook<CharacterNameResultDelegate> CharacterNameResultHook { get; init; } = null!;

        // Signature adapted from https://github.com/LittleNightmare/UsedName
        [Signature("48 89 5C 24 10 56 48 83 EC 20 48 ?? ?? ?? ?? ?? ?? 48 8B F2 E8 ?? ?? ?? ?? 48 8B D8",
            DetourName = nameof(ProcessSocialListResult))]
        private Hook<SocialListResultDelegate> SocialListResultHook { get; init; } = null!;

#pragma warning restore CS0649

        public GameHooks(ILogger<GameHooks> logger, PersistenceContext persistenceContext)
        {
            _logger = logger;
            _persistenceContext = persistenceContext;

            _logger.LogDebug("Initializing game hooks");
            SignatureHelper.Initialise(this);
            CharacterNameResultHook.Enable();
            SocialListResultHook.Enable();

            _logger.LogDebug("Game hooks initialized");
        }

        private int ProcessCharacterNameResult(nint a1, ulong contentId, char* playerName)
        {
            try
            {
                var mapping = new ContentIdToName
                {
                    ContentId = contentId,
                    PlayerName = MemoryHelper.ReadString(new nint(playerName), Encoding.ASCII, 32),
                };

                if (!string.IsNullOrEmpty(mapping.PlayerName))
                {
                    _logger.LogTrace("Content id {ContentId} belongs to '{Name}'", mapping.ContentId,
                        mapping.PlayerName);
                    Task.Run(() => _persistenceContext.HandleContentIdMapping(mapping));
                }
                else
                {
                    _logger.LogDebug("Content id {ContentId} didn't resolve to a player name, ignoring",
                        mapping.ContentId);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not process character name result");
            }

            return CharacterNameResultHook.Original(a1, contentId, playerName);
        }

        private nint ProcessSocialListResult(nint a1, nint dataPtr)
        {
            try
            {
                var result = Marshal.PtrToStructure<SocialListResultPage>(dataPtr);
                List<ContentIdToName> mappings = new();
                foreach (SocialListPlayer player in result.PlayerSpan)
                {
                    var mapping = new ContentIdToName
                    {
                        ContentId = player.ContentId,
                        PlayerName = MemoryHelper.ReadString(new nint(player.CharacterName), Encoding.ASCII, 32),
                    };

                    if (!string.IsNullOrEmpty(mapping.PlayerName))
                    {
                        _logger.LogTrace("Content id {ContentId} belongs to '{Name}'", mapping.ContentId,
                            mapping.PlayerName);
                        mappings.Add(mapping);
                    }
                    else
                    {
                        _logger.LogDebug("Content id {ContentId} didn't resolve to a player name, ignoring",
                            mapping.ContentId);
                    }
                }

                if (mappings.Count > 0)
                    Task.Run(() => _persistenceContext.HandleContentIdMapping(mappings));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not process social list result");
            }

            return SocialListResultHook.Original(a1, dataPtr);
        }

        public void Dispose()
        {
            CharacterNameResultHook.Dispose();
            SocialListResultHook.Dispose();
        }

        /// <summary>
        /// There are some caveats here, the social list includes a LOT of things with different types
        /// (we don't care for the result type in this plugin), see sapphire for which field is the type.
        ///
        /// 1 = party
        /// 2 = friend list
        /// 3 = link shell
        /// 4 = player search
        /// 5 = fc short list (first tab, with company board + actions + online members)
        /// 6 = fc long list (members tab)
        ///
        /// Both 1 and 2 are sent to you on login, unprompted.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 0x380)]
        internal struct SocialListResultPage
        {
            [FieldOffset(0x10)] private fixed byte Players[10 * 0x58];

            public Span<SocialListPlayer> PlayerSpan => new(Unsafe.AsPointer(ref Players[0]), 10);
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x58)]
        internal struct SocialListPlayer
        {
            /// <summary>
            /// If this is set, it means there is a player present in this slot (even if no name can be retrieved),
            /// 0 if empty.
            /// </summary>
            [FieldOffset(0x00)] public readonly ulong ContentId;

            /// <summary>
            /// This *can* be empty, e.g. if you're querying your friend list, the names are ONLY set for characters on the same world.
            /// </summary>
            [FieldOffset(0x31)] public fixed byte CharacterName[32];
        }
    }
}

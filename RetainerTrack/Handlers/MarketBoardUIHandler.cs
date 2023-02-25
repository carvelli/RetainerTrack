using System;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;

namespace RetainerTrack.Handlers
{
    internal sealed unsafe class MarketBoardUiHandler : IDisposable
    {
        private readonly ILogger<MarketBoardUiHandler> _logger;
        private readonly Framework _framework;
        private readonly GameGui _gameGui;
        private readonly PersistenceContext _persistenceContext;

        private Hook<Draw>? _drawHook;

        private delegate void Draw(AtkUnitBase* addon);

        public MarketBoardUiHandler(
            ILogger<MarketBoardUiHandler> logger,
            Framework framework,
            GameGui gameGui,
            PersistenceContext persistenceContext)
        {
            _logger = logger;
            _framework = framework;
            _gameGui = gameGui;
            _persistenceContext = persistenceContext;

            _framework.Update += FrameworkUpdate;
        }

        private AddonItemSearchResult* ItemSearchResult => (AddonItemSearchResult*)_gameGui.GetAddonByName("ItemSearchResult");

        private void FrameworkUpdate(Framework framework)
        {
            var addon = ItemSearchResult;
            if (addon == null)
                return;

            _drawHook ??= Hook<Draw>.FromAddress(
                new nint(addon->AtkUnitBase.AtkEventListener.vfunc[42]),
                AddonDraw);
            _drawHook.Enable();
            _framework.Update -= FrameworkUpdate;
        }

        private void AddonDraw(AtkUnitBase* addon)
        {
            UpdateRetainerNames();
            _drawHook!.Original(addon);
        }

        private void UpdateRetainerNames()
        {
            try
            {
                var addon = ItemSearchResult;
                if (addon == null || !addon->AtkUnitBase.IsVisible)
                    return;

                var results = addon->Results;
                if (results == null)
                    return;

                int length = results->ListLength;
                if (length == 0)
                    return;

                for (int i = 0; i < length; ++i)
                {
                    var listItem = results->ItemRendererList[i].AtkComponentListItemRenderer;
                    var uldManager = listItem->AtkComponentButton.AtkComponentBase.UldManager;
                    if (uldManager.NodeListCount < 14)
                        continue;

                    var retainerNameNode = (AtkTextNode*)uldManager.NodeList[5];
                    string retainerName = retainerNameNode->NodeText.ToString();
                    if (!retainerName.Contains('('))
                    {
                        string playerName = _persistenceContext.GetCharacterNameOnCurrentWorld(retainerName);
                        if (!string.IsNullOrEmpty(playerName))
                            retainerNameNode->SetText($"{playerName} ({retainerName})");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, "Market board draw failed");
            }
        }

        public void Dispose()
        {
            _drawHook?.Dispose();
            _framework.Update -= FrameworkUpdate;
        }
    }
}

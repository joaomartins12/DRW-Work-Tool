using System;
using System.Collections.Generic;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    public static class BinCatalog
    {
        private static readonly string[] RawNames =
        {
            "Tactics",
            "Digimon_List",
            "Skill",
            "DigimonEvo",
            "ItemList",
            "Buff",
            "Monster",
            "Model",
            "InfiniteWar",
            "Quest",
            "Npc",
            "MapPortal",
            "MapNpc",
            "MapObject",
            "MapList",
            "MapMonsterList",
            "MasterCard",
            "TamerList",
            "DMBase",
            "CharCreateTable",
            "Digimon_Book",
            "CashShop",
            "UIText",
            "Achieve",
            "Event",
            "Talk",
            "WorldMap",
            "Portal",
            "AddExp",
            "BattleTable",
            "Cuid",
            "Data_Exchange",
            "DigimonParcel",
            "EffectList",
            "ExtraExchange",
            "Gotcha",
            "MapCharLight",
            "MapRegion",
            "Nature",
            "New_Element",
            "NewTutorial",
            "Passive_Ability",
            "Reward",
            "Ride",
            "Scene",
            "ServerTransfer",
            "Spirit_NPC",
            "SvLineUp",
            "TimeCharge",
            "Tutorial",
            "Weather"
        };

        public static readonly IReadOnlyList<string> Names =
            RawNames
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static string? ResolveExactName(string value)
        {
            return Names.FirstOrDefault(
                x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
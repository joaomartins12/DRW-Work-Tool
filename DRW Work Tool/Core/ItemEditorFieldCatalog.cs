using System;
using System.Collections.Generic;

namespace DRW_Work_Tool.Core
{
    public static class ItemEditorFieldCatalog
    {
        private static readonly HashSet<string> SelectionFields =
            new(StringComparer.Ordinal)
            {
                "s_nClass",
                "s_nUseMode",
                "s_nEquipSeries",
                "s_nUseCharacter",
                "s_bDummy",
                "s_nDrop",
                "s_bModel_Loop",
                "s_bModel_Shader",
                "s_nSkillCodeType",
                "s_nSocketCount",
                "s_nBelonging",
                "s_nDigiviceSkillSlot",
                "s_nDigiviceChipsetSlot",
                "s_btUseTimeType",
                "s_nUseBattle",
                "s_nDoNotUseType",
                "s_bUseTimeType"
            };

        public static bool ShouldUseSelection(
            string tag) =>
            SelectionFields.Contains(tag);

        public static string GetChoiceLabel(
            string tag,
            string value)
        {
            return tag switch
            {
                "s_nBelonging" => value switch
                {
                    "0" => "Tradeable",
                    "1" => "Bind after first use/equip",
                    "2" => "Bound / Non-tradeable",
                    _ => "Binding rule value"
                },

                "s_nUseBattle" => value switch
                {
                    "0" => "Cannot use during battle",
                    "1" => "Can use during battle",
                    _ => "Battle-use rule value"
                },

                "s_bModel_Loop" => value switch
                {
                    "0" => "Effect/model does not loop",
                    "1" => "Effect/model loops",
                    _ => "Model loop value"
                },

                "s_bModel_Shader" => value switch
                {
                    "0" => "Shader disabled",
                    "1" => "Shader enabled",
                    _ => "Shader value"
                },

                "s_nSocketCount" => value switch
                {
                    "0" => "No sockets",
                    "1" => "1 socket",
                    _ => $"{value} sockets"
                },

                "s_nDigiviceSkillSlot" => value switch
                {
                    "0" => "No Tamer Skill slots",
                    "1" => "1 Tamer Skill slot",
                    _ => $"{value} Tamer Skill slots"
                },

                "s_nDigiviceChipsetSlot" => value switch
                {
                    "0" => "No Chipset slots",
                    "1" => "1 Chipset slot",
                    _ => $"{value} Chipset slots"
                },

                "s_nDrop" => value switch
                {
                    "0" => "Drop disabled",
                    "1" => "Drop enabled",
                    "2" => "Special/alternate drop mode",
                    _ => "Drop mode value"
                },

                "s_nClass" =>
                    $"Item class / rarity {value}",

                "s_nUseMode" => value switch
                {
                    "0" => "Use mode 0",
                    "1" => "Use mode 1",
                    _ => $"Use mode {value}"
                },

                "s_nEquipSeries" => value switch
                {
                    "0" => "No equipment series",
                    _ => $"Equipment series {value}"
                },

                "s_nUseCharacter" => value switch
                {
                    "0" => "No character restriction",
                    "1" => "Character restriction group 1",
                    "2" => "Character restriction group 2",
                    "3" => "Character restriction group 3",
                    _ => $"Character restriction group {value}"
                },

                "s_bDummy" => value switch
                {
                    "0" => "Normal item",
                    _ => $"Dummy/internal mode {value}"
                },

                "s_nSkillCodeType" => value switch
                {
                    "0" => "No / default linked-skill mode",
                    "1" => "Linked-skill mode 1",
                    "2" => "Linked-skill mode 2",
                    _ => $"Linked-skill mode {value}"
                },

                "s_btUseTimeType" => value switch
                {
                    "0" => "Time rule 0",
                    "1" => "Time rule 1",
                    "2" => "Time rule 2",
                    "3" => "Time rule 3",
                    _ => $"Time rule {value}"
                },

                "s_nDoNotUseType" => value switch
                {
                    "0" => "No extra usage restriction",
                    "1" => "Usage restriction mode 1",
                    "2" => "Usage restriction mode 2",
                    _ => $"Usage restriction mode {value}"
                },

                "s_bUseTimeType" => value switch
                {
                    "0" => "Timed-rule flag disabled",
                    "1" => "Timed-rule flag enabled",
                    _ => $"Timed-rule flag {value}"
                },

                _ =>
                    "Observed value"
            };
        }

        public static string GetHelpText(
            string tag)
        {
            return tag switch
            {
                "s_dwItemID" =>
                    "Unique Item ID used throughout the client. New items must use an ID that does not already exist.",

                "s_szName" =>
                    "Display/name text stored for this item.",

                "s_nIcon" =>
                    "Icon atlas slot ID. The Work Tool resolves it through InterfaceIconMap and renders the actual DDS atlas.",

                "s_szComment" =>
                    "Full item description. Line breaks are preserved exactly when saving the XML.",

                "s_cNif" =>
                    "NIF resource reference used by item types that require a NIF asset.",

                "s_nClass" =>
                    "Item class/rarity numeric value. The current ItemList uses classes 1 through 13. The client source inspected does not expose trustworthy human rarity names for every value, so the editor does not invent them.",

                "s_szTypeComment" =>
                    "Human-readable item category/type text.",

                "s_btCodeTag" =>
                    "Internal item code/tag byte. The exact enum was not located in the exposed client files; changing it can alter type-specific handling, so preserve the original unless you know the target behavior.",

                "unkt" =>
                    "Unconfirmed internal ItemList field. No reliable client-side semantic mapping was found, so it remains raw and editable.",

                "s_nType_L" =>
                    "MAIN ITEM TYPE. The client directly compares this field with nItem categories such as Digivice and equipment part types. It is also used to determine which equipment slot an item belongs to. This is a high-impact field.",

                "s_nType_S" =>
                    "SECONDARY ITEM TYPE / SUBTYPE paired with the main type. The precise enum table was not exposed in the files inspected, so the numeric value remains editable.",

                "s_nTypeValue" =>
                    "Additional value associated with the item type/subtype. Exact interpretation depends on the item category.",

                "s_nSection" =>
                    "ITEM SECTION / DISPLAY MAPPING ID. When Sync ItemDisplay is enabled, this value is written to ItemDisplay.xml as <nItemS>.",

                "s_nSellType" =>
                    "Sell/price handling type. The ItemList contains many values, so it remains an unrestricted numeric reference.",

                "s_nUseMode" =>
                    "Item use-mode selector. The current XML uses 0 and 1. Exact per-mode semantics were not proven by the client files inspected.",

                "unkr" =>
                    "Unconfirmed internal ItemList field. Preserve it unless you have a known target value.",

                "s_nUseTimeGroup" =>
                    "COOLDOWN GROUP ID. The client obtains the item's CoolTimeSeq from this group and starts that cooldown when applicable. For Digivice equipment swaps the client explicitly requires this field to be non-zero.",

                "s_nOverlap" =>
                    "Maximum number of copies that can exist in one inventory stack.",

                "s_nTamerReqMinLevel" =>
                    "Minimum Tamer level requirement.",

                "s_nTamerReqMaxLevel" =>
                    "Maximum Tamer level requirement stored by the item.",

                "s_nDigimonReqMinLevel" =>
                    "Minimum Digimon level requirement.",

                "s_nDigimonReqMaxLevel" =>
                    "Maximum Digimon level requirement stored by the item.",

                "s_nPossess" =>
                    "Possession/use restriction value. The current XML uses many different IDs; no reliable complete enum was found in the inspected client files.",

                "s_nEquipSeries" =>
                    "Equipment series/group selector. Current XML values include 0 and series 11-14. Treat it as a compatibility/grouping ID rather than a cosmetic label.",

                "s_nUseCharacter" =>
                    "Character restriction group. 0 is used as the unrestricted/default value; groups 1-3 exist in the XML. The exact character enum names were not safely recovered from the inspected source.",

                "s_bDummy" =>
                    "Internal/dummy item flag. Every item in the supplied ItemList currently uses 0, so changing it is high-risk.",

                "ukteste1" =>
                    "Unconfirmed internal ItemList field. Its meaning was not found reliably in the inspected client source.",

                "s_nDrop" =>
                    "Drop permission/mode. Values 0, 1 and 2 occur in the XML. 1 is the overwhelmingly common value; 2 should be treated as a special/alternate mode rather than assuming it is simply true/false.",

                "uktest" =>
                    "Unconfirmed internal ItemList field. Preserve the original value unless you have a known use case.",

                "s_nEventItemType" =>
                    "Event-item classification/type ID. Many distinct values exist, therefore it remains a raw numeric selector.",

                "s_dwEventItemPrice" =>
                    "Event-item price/value field.",

                "s_dwDigiCorePrice" =>
                    "DigiCore-related price/cost field.",

                "s_dwScanPrice" =>
                    "Raw scan cost stored by the item.",

                "s_dwSale" =>
                    "Raw sale/vendor value stored by the item.",

                "s_cModel_Nif" =>
                    "NIF model resource used for the item's visible model when applicable.",

                "s_cModel_Effect" =>
                    "Visual effect resource associated with the item's model.",

                "s_bModel_Loop" =>
                    "Controls whether the model/effect is configured to loop. XML uses 0 and 1.",

                "s_bModel_Shader" =>
                    "Controls the model shader flag. XML uses 0 and 1.",

                "s_nSkillCodeType" =>
                    "REFERENCE SOURCE / MODE for s_dwSkill. Cross-checking the supplied ItemList, Skill.xml and ItemAcessorys.xml shows that type 2 is predominantly Accessory definitions, while types 0/1 are Skill-oriented. IDs can overlap between both tables, so the editor displays the resolved source explicitly.",

                "s_dwSkill" =>
                    "LINKED SKILL / ACCESSORY REFERENCE. This numeric field can point to Skill.xml OR ItemAcessorys.xml. The editor resolves both sources, shows Skill cards with ID, icon and name, and Accessory cards with the accessory definition ID and option metadata.",

                "s_btApplyRateMax" =>
                    "Maximum socket/attribute application rate. The runtime item structure contains per-socket apply-rate values (m_nSockAppRate), confirming that rates are functional item data.",

                "s_btApplyRateMin" =>
                    "Minimum socket/attribute application rate used as the lower configured bound.",

                "s_btApplyElement" =>
                    "ATTRIBUTE / ELEMENT RAW VALUE. This is intentionally a free textbox: the XML contains values not limited to a tiny enum and the client files inspected do not expose a complete safe enum. Enter the exact numeric value you need.",

                "s_nSocketCount" =>
                    "Number of available item sockets. The runtime item structure stores per-socket item type and apply-rate arrays, confirming sockets are functional equipment data.",

                "s_dwSoundID" =>
                    "Sound resource/ID associated with the item.",

                "s_nBelonging" =>
                    "Trade/binding rule from ItemList. Your project convention is 0=Tradeable, 1=bind after first use/equip, 2=Bound. The client also maintains runtime binding state (m_nLimited) and explicitly changes binding for time-limited equipment, so treat this field as high impact.",

                "unk2" or
                "unk3" or
                "unk4" or
                "unks" or
                "unkss" =>
                    "Binary-backed unknown field preserved by the converter. Do not change it casually; the exact client-side meaning has not been recovered safely.",

                "s_nQuest1" or
                "s_nQuest2" or
                "s_nQuest3" =>
                    "Quest reference associated with this item.",

                "s_nDigiviceSkillSlot" =>
                    "TAMER SKILL SLOT COUNT. When a Digivice is equipped, the client directly passes this value to SetTamerSkillCount().",

                "s_nDigiviceChipsetSlot" =>
                    "CHIPSET SLOT COUNT. When a Digivice is equipped, the client directly passes this value to SetChipsetCount().",

                "s_nQuestRequire" =>
                    "Quest requirement/reference used by the item.",

                "s_btUseTimeType" =>
                    "Timed-item rule selector. Values 0-3 exist in the XML; exact enum names were not proven by the inspected source.",

                "s_nUseTime_Min" =>
                    "TIME-LIMIT DURATION VALUE. Client equipment logic checks this field to determine whether an equipped item is time-limited and uses the runtime end-time when applying its linked skill/buff.",

                "s_nUseBattle" =>
                    "Battle-use permission. 0=not usable during battle; 1=usable during battle.",

                "s_nDoNotUseType" =>
                    "Additional usage-restriction mode. Values 0, 1 and 2 exist; exact mode names were not recovered safely.",

                "s_bUseTimeType" =>
                    "Additional on/off flag related to timed-use handling. XML currently uses 0 and 1.",

                _ =>
                    "Raw ItemList field. No sufficiently reliable semantic mapping was found in the client files inspected, so the editor preserves the original XML name/value rather than inventing behavior."
            };
        }

        public static string GetSection(
            string tag)
        {
            return tag switch
            {
                "s_dwItemID" or
                "s_szName" or
                "s_nIcon" or
                "s_szComment" or
                "s_cNif" or
                "s_nClass" or
                "s_szTypeComment"
                    => "BASIC",

                "s_btCodeTag" or
                "unkt" or
                "s_nType_L" or
                "s_nType_S" or
                "s_nTypeValue" or
                "s_nSection"
                    => "CLASSIFICATION",

                "s_nSellType" or
                "s_nUseMode" or
                "unkr" or
                "s_nUseTimeGroup" or
                "s_nOverlap"
                    => "USAGE",

                "s_nTamerReqMinLevel" or
                "s_nTamerReqMaxLevel" or
                "s_nDigimonReqMinLevel" or
                "s_nDigimonReqMaxLevel" or
                "s_nPossess" or
                "s_nEquipSeries" or
                "s_nUseCharacter"
                    => "REQUIREMENTS",

                "s_bDummy" or
                "ukteste1" or
                "s_nDrop" or
                "uktest" or
                "s_nEventItemType" or
                "s_dwEventItemPrice" or
                "s_dwDigiCorePrice" or
                "s_dwScanPrice" or
                "s_dwSale"
                    => "ECONOMY",

                "s_cModel_Nif" or
                "s_cModel_Effect" or
                "s_bModel_Loop" or
                "s_bModel_Shader" or
                "s_nSkillCodeType" or
                "s_dwSkill"
                    => "MODEL_SKILL",

                "s_btApplyRateMax" or
                "s_btApplyRateMin" or
                "s_btApplyElement" or
                "s_nSocketCount" or
                "s_dwSoundID" or
                "s_nBelonging"
                    => "SOCKET_TRADE",

                "s_nQuest1" or
                "s_nQuest2" or
                "s_nQuest3" or
                "s_nDigiviceSkillSlot" or
                "s_nDigiviceChipsetSlot" or
                "s_nQuestRequire"
                    => "QUEST_DIGIVICE",

                "s_btUseTimeType" or
                "s_nUseTime_Min" or
                "s_nUseBattle" or
                "s_nDoNotUseType" or
                "s_bUseTimeType"
                    => "TIME_BATTLE",

                _ =>
                    "ADVANCED"
            };
        }

        public static string GetSectionTitle(
            string section) =>
            section switch
            {
                "BASIC" => "BASIC INFORMATION",
                "CLASSIFICATION" => "CLASSIFICATION / REFERENCES",
                "USAGE" => "USAGE / STACK",
                "REQUIREMENTS" => "REQUIREMENTS / CHARACTER",
                "ECONOMY" => "DROP / EVENT / ECONOMY",
                "MODEL_SKILL" => "MODEL / SKILL",
                "SOCKET_TRADE" => "SOCKETS / ATTRIBUTES / TRADE",
                "QUEST_DIGIVICE" => "QUEST / DIGIVICE",
                "TIME_BATTLE" => "TIME / BATTLE",
                _ => "ADVANCED / UNKNOWN FIELDS"
            };
    }
}

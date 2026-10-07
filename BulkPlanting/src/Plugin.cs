using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BulkPlanting
{
    public enum GridPattern
    {
        Hex,
        Square,
    }

    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.x86_64")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "igorr.BulkPlanting";
        public const string Name = "BulkPlanting";
        public const string Version = "1.0.0";

        public const int MaxGridSize = 12;

        internal static ManualLogSource Log;

        // General
        public static ConfigEntry<bool> BulkModeEnabled;
        public static ConfigEntry<int> Rows;
        public static ConfigEntry<int> Columns;
        public static ConfigEntry<GridPattern> Pattern;

        // Spacing / layout
        public static ConfigEntry<float> SpacingMargin;
        public static ConfigEntry<bool> SnapToExistingPlants;

        // Validation
        public static ConfigEntry<bool> RequireGrowableSpot;

        // Costs
        public static ConfigEntry<bool> StaminaPerPlant;
        public static ConfigEntry<bool> DurabilityPerPlant;
        public static ConfigEntry<bool> SkillPerPlant;

        // Keys
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<KeyboardShortcut> PatternKey;
        public static ConfigEntry<KeyboardShortcut> RowsUpKey;
        public static ConfigEntry<KeyboardShortcut> RowsDownKey;
        public static ConfigEntry<KeyboardShortcut> ColumnsUpKey;
        public static ConfigEntry<KeyboardShortcut> ColumnsDownKey;
        public static ConfigEntry<KeyCode> ResizeModifier;

        // UI
        public static ConfigEntry<bool> ShowHud;
        public static ConfigEntry<float> HudVerticalPosition;

        private Harmony _harmony;
        private float _scrollAccumulator;

        private void Awake()
        {
            Log = Logger;

            BulkModeEnabled = Config.Bind("1 - General", "BulkModeEnabled", true,
                "Whether bulk planting is active when holding the Cultivator with a seed selected. Toggled in-game with ToggleKey.");
            Rows = Config.Bind("1 - General", "Rows", 3,
                new ConfigDescription("Rows in the planting grid (extending away from you).", new AcceptableValueRange<int>(1, MaxGridSize)));
            Columns = Config.Bind("1 - General", "Columns", 3,
                new ConfigDescription("Columns in the planting grid (across your view).", new AcceptableValueRange<int>(1, MaxGridSize)));
            Pattern = Config.Bind("1 - General", "Pattern", GridPattern.Hex,
                "Hex packs ~15% more plants into the same area; Square gives classic straight rows.");

            SpacingMargin = Config.Bind("2 - Layout", "SpacingMargin", 0.05f,
                new ConfigDescription(
                    "Extra distance (meters) added on top of the tightest spacing at which every plant still grows. " +
                    "Raise it if you prefer roomier fields.",
                    new AcceptableValueRange<float>(0f, 2f)));
            SnapToExistingPlants = Config.Bind("2 - Layout", "SnapToExistingPlants", true,
                "When aiming next to existing crops, align the grid with them so fields extend seamlessly.");

            RequireGrowableSpot = Config.Bind("3 - Validation", "RequireGrowableSpot", true,
                "Only plant where the crop will actually grow (open sky, right biome, not too hot/cold, no crowding of neighbours).");

            StaminaPerPlant = Config.Bind("4 - Costs", "StaminaPerPlant", true,
                "Charge stamina for every plant, as if planted one by one. Planting stops when you run out.");
            DurabilityPerPlant = Config.Bind("4 - Costs", "DurabilityPerPlant", true,
                "Wear the Cultivator for every plant, as if planted one by one.");
            SkillPerPlant = Config.Bind("4 - Costs", "SkillPerPlant", true,
                "Gain Farming skill for every plant, as if planted one by one.");

            ToggleKey = Config.Bind("5 - Keys", "ToggleBulkMode", new KeyboardShortcut(KeyCode.N), "Turn bulk planting on/off.");
            PatternKey = Config.Bind("5 - Keys", "TogglePattern", new KeyboardShortcut(KeyCode.H), "Switch between Hex and Square patterns.");
            RowsUpKey = Config.Bind("5 - Keys", "RowsUp", new KeyboardShortcut(KeyCode.UpArrow), "Add a row.");
            RowsDownKey = Config.Bind("5 - Keys", "RowsDown", new KeyboardShortcut(KeyCode.DownArrow), "Remove a row.");
            ColumnsUpKey = Config.Bind("5 - Keys", "ColumnsUp", new KeyboardShortcut(KeyCode.RightArrow), "Add a column.");
            ColumnsDownKey = Config.Bind("5 - Keys", "ColumnsDown", new KeyboardShortcut(KeyCode.LeftArrow), "Remove a column.");
            ResizeModifier = Config.Bind("5 - Keys", "ResizeModifier", KeyCode.LeftAlt,
                "Hold this and use the mouse wheel to grow/shrink the whole grid (instead of rotating).");

            ShowHud = Config.Bind("6 - UI", "ShowHud", true, "Show the bulk planting status panel while planting.");
            // Defaults to the top: the bottom centre is taken by the game's selected-piece box.
            HudVerticalPosition = Config.Bind("6 - UI", "PanelVerticalPosition", 0.06f,
                new ConfigDescription("Vertical position of the status panel (0 = top, 1 = bottom).", new AcceptableValueRange<float>(0f, 1f)));

            Pattern.SettingChanged += (_, _) => BulkPlanter.InvalidateLayout();
            SpacingMargin.SettingChanged += (_, _) => BulkPlanter.ClearFootprintCache();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            BulkPlanter.DestroyPreviews();
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZInput.instance == null)
            {
                BulkPlanter.DestroyPreviews();
                return;
            }

            if (!BulkPlanter.IsHoldingPlantable(player, out _, out _) || !player.TakeInput() || Hud.IsPieceSelectionVisible())
            {
                return;
            }

            if (IsDown(ToggleKey.Value))
            {
                BulkModeEnabled.Value = !BulkModeEnabled.Value;
                player.Message(MessageHud.MessageType.TopLeft, BulkModeEnabled.Value ? "Bulk planting: on" : "Bulk planting: off");
            }

            if (!BulkModeEnabled.Value)
            {
                return;
            }

            if (IsDown(PatternKey.Value))
            {
                Pattern.Value = Pattern.Value == GridPattern.Hex ? GridPattern.Square : GridPattern.Hex;
            }

            if (IsDown(RowsUpKey.Value)) Rows.Value = Mathf.Clamp(Rows.Value + 1, 1, MaxGridSize);
            if (IsDown(RowsDownKey.Value)) Rows.Value = Mathf.Clamp(Rows.Value - 1, 1, MaxGridSize);
            if (IsDown(ColumnsUpKey.Value)) Columns.Value = Mathf.Clamp(Columns.Value + 1, 1, MaxGridSize);
            if (IsDown(ColumnsDownKey.Value)) Columns.Value = Mathf.Clamp(Columns.Value - 1, 1, MaxGridSize);

            if (IsResizeModifierHeld())
            {
                _scrollAccumulator += ZInput.GetMouseScrollWheel();
                float threshold = player.m_scrollAmountThreshold;
                if (Mathf.Abs(_scrollAccumulator) > threshold)
                {
                    int delta = _scrollAccumulator > 0f ? 1 : -1;
                    _scrollAccumulator = 0f;
                    Rows.Value = Mathf.Clamp(Rows.Value + delta, 1, MaxGridSize);
                    Columns.Value = Mathf.Clamp(Columns.Value + delta, 1, MaxGridSize);
                }
            }
            else
            {
                _scrollAccumulator = 0f;
            }
        }

        private void OnGUI()
        {
            if (ShowHud.Value)
            {
                StatusPanel.Draw();
            }
        }

        internal static bool IsResizeModifierHeld()
        {
            return ResizeModifier.Value != KeyCode.None && ZInput.instance != null && ZInput.GetKey(ResizeModifier.Value, logWarning: false);
        }

        // Valheim uses Unity's new Input System, so read keys through ZInput instead of
        // KeyboardShortcut.IsDown() (which relies on the legacy input manager).
        private static bool IsDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey, logWarning: false))
            {
                return false;
            }
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!ZInput.GetKey(modifier, logWarning: false))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

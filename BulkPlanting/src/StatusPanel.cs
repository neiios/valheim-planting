using UnityEngine;

namespace BulkPlanting
{
    /// <summary>A small translucent panel describing the current grid, drawn with IMGUI.</summary>
    internal static class StatusPanel
    {
        private const string Accent = "#f2c879";
        private const string Good = "#9be08a";
        private const string Bad = "#ff7b6b";
        private const string Dim = "#c9c2b3";

        private static GUIStyle s_box;
        private static Texture2D s_background;
        private static int s_fontSize;

        public static void Draw()
        {
            LayoutSummary s = BulkPlanter.Summary;
            Player player = Player.m_localPlayer;
            if (!s.Holding || player == null || Hud.IsPieceSelectionVisible() || Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen())
            {
                return;
            }

            EnsureStyles();

            string text;
            if (!Plugin.BulkModeEnabled.Value)
            {
                text = $"<color={Dim}>Bulk planting off  ·  </color><color={Accent}>[{Plugin.ToggleKey.Value}]</color><color={Dim}> to enable</color>";
            }
            else if (!s.Active)
            {
                return;
            }
            else
            {
                string pattern = s.Pattern == GridPattern.Hex ? "hex" : "square";
                string snapped = s.Snapped ? $"  ·  <color={Good}>aligned to field</color>" : "";
                string seeds = s.FreeBuild
                    ? $"<color={Good}>free</color>"
                    : $"{s.SeedName}: <color={(s.SeedCount >= s.Plantable ? Good : Bad)}>{s.SeedCount}</color>";
                string count = $"<color={Good}><b>{s.Plantable}</b></color> to plant";
                if (s.Blocked > 0)
                {
                    count += $"  <color={Bad}>{s.Blocked} blocked ({Localization.instance.Localize(BulkPlanter.DescribeProblem(s.FirstProblem))})</color>";
                }

                text =
                    $"<color={Accent}><b>{s.PlantName}</b></color>  <b>{s.Columns} × {s.Rows}</b> {pattern}  ·  {s.Spacing:0.00} m{snapped}\n" +
                    $"{count}  ·  {seeds}\n" +
                    $"<size={s_fontSize * 3 / 4}><color={Dim}>" +
                    $"[{Plugin.ColumnsDownKey.Value}/{Plugin.ColumnsUpKey.Value}] columns   " +
                    $"[{Plugin.RowsDownKey.Value}/{Plugin.RowsUpKey.Value}] rows   " +
                    $"[{Plugin.ResizeModifier.Value}+Wheel] size   " +
                    $"[{Plugin.PatternKey.Value}] pattern   " +
                    $"[{Plugin.ToggleKey.Value}] off</color></size>";
            }

            var content = new GUIContent(text);
            float maxWidth = Screen.width * 0.8f;
            Vector2 size = s_box.CalcSize(content);
            size.x = Mathf.Min(size.x, maxWidth);
            size.y = s_box.CalcHeight(content, size.x);
            float y = Mathf.Clamp(Screen.height * Plugin.HudVerticalPosition.Value - size.y / 2f, 0f, Screen.height - size.y);
            var rect = new Rect((Screen.width - size.x) / 2f, y, size.x, size.y);
            GUI.Box(rect, content, s_box);
        }

        private static void EnsureStyles()
        {
            int fontSize = Mathf.Clamp(Screen.height / 50, 12, 40);
            if (s_box != null && s_background != null && fontSize == s_fontSize)
            {
                return;
            }
            s_fontSize = fontSize;

            if (s_background == null)
            {
                s_background = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                s_background.SetPixel(0, 0, new Color(0.08f, 0.06f, 0.04f, 0.72f));
                s_background.Apply();
                s_background.hideFlags = HideFlags.HideAndDontSave;
            }

            int pad = fontSize / 2;
            s_box = new GUIStyle(GUI.skin.box)
            {
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                padding = new RectOffset(pad * 2, pad * 2, pad, pad),
            };
            s_box.normal.background = s_background;
            s_box.normal.textColor = new Color(0.95f, 0.92f, 0.86f);
        }
    }
}

using System;

namespace Game.Presentation
{
    /// <summary>Screen facts in pixels. Origin is bottom-left (Unity screen space).</summary>
    public readonly struct ScreenMetrics
    {
        public readonly float Width;
        public readonly float Height;
        /// <summary>Screen DPI; 0 or less when the platform does not report it.</summary>
        public readonly float Dpi;
        public readonly float SafeX;
        public readonly float SafeY;
        public readonly float SafeWidth;
        public readonly float SafeHeight;

        public ScreenMetrics(float width, float height, float dpi, float safeX, float safeY, float safeWidth, float safeHeight)
        {
            Width = width;
            Height = height;
            Dpi = dpi;
            SafeX = safeX;
            SafeY = safeY;
            SafeWidth = safeWidth;
            SafeHeight = safeHeight;
        }

        public static ScreenMetrics FullScreen(float width, float height, float dpi) =>
            new ScreenMetrics(width, height, dpi, 0, 0, width, height);
    }

    /// <summary>
    /// Layout tuning in density-independent pixels (dp, 1 dp = 1 px at 160 dpi). Defaults reserve the
    /// space the later HUD needs (top bar, order panel, bottom bar), so cell sizes measured in M2 match
    /// the final layout. See Docs/VisionReference.md.
    /// </summary>
    [Serializable]
    public sealed class LayoutSpec
    {
        public float topBarDp = 64f;
        public float orderPanelDp = 112f;
        public float bottomBarDp = 80f;
        public float paddingDp = 16f;
        public float sideMarginDp = 12f;
        public float maxSlotDp = 60f;
        public float slotGapDp = 6f;
        public float dragThresholdDp = 10f;
        /// <summary>Used when the platform reports no DPI (density = fallbackDpi / 160).</summary>
        public float fallbackDpi = 160f;
        /// <summary>Comfort minimum for a cell; layouts below it are flagged in the cell-size report.</summary>
        public float minComfortCellDp = 48f;
    }

    /// <summary>
    /// Pure portrait layout of the merge board, the inventory row and the sell zone. All values are in
    /// screen pixels, origin bottom-left. Board cells are row-major from the top-left, matching the engine.
    /// </summary>
    public sealed class BoardLayout
    {
        public BoardLayout(ScreenMetrics screen, LayoutSpec spec, int columns, int rows, int inventorySlots)
        {
            if (columns < 1 || rows < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Board needs at least one column and row.");
            }

            Columns = columns;
            Rows = rows;
            InventorySlots = Math.Max(0, inventorySlots);
            UsedFallbackDpi = screen.Dpi <= 0f;
            Density = (UsedFallbackDpi ? spec.fallbackDpi : screen.Dpi) / 160f;

            float top = (spec.topBarDp + spec.orderPanelDp) * Density;
            float bottom = spec.bottomBarDp * Density;
            float pad = spec.paddingDp * Density;
            float side = spec.sideMarginDp * Density;

            float availWidth = Math.Max(1f, screen.SafeWidth - 2f * side);
            float availHeight = Math.Max(1f, screen.SafeHeight - top - bottom - pad);
            float byWidth = availWidth / columns;
            float byHeight = availHeight / rows;
            HeightLimited = byHeight < byWidth;
            CellPx = (float)Math.Floor(Math.Min(byWidth, byHeight));
            CellDp = CellPx / Density;
            BelowComfort = CellDp < spec.minComfortCellDp;

            float boardWidth = CellPx * columns;
            float boardHeight = CellPx * rows;
            BoardLeft = screen.SafeX + (screen.SafeWidth - boardWidth) * 0.5f;
            float areaTop = screen.SafeY + screen.SafeHeight - top;
            float areaBottom = screen.SafeY + bottom + pad;
            BoardTop = areaTop - (areaTop - areaBottom - boardHeight) * 0.5f;
            BoardWidth = boardWidth;
            BoardHeight = boardHeight;

            // Bottom bar: inventory slots from the left, sell zone on the right, all the same square size.
            float barCentreY = screen.SafeY + bottom * 0.5f;
            float gap = spec.slotGapDp * Density;
            int squares = InventorySlots + 1;
            float bySpace = (screen.SafeWidth - 2f * side - gap * squares) / Math.Max(1, squares);
            SlotPx = (float)Math.Floor(Math.Min(spec.maxSlotDp * Density, Math.Min(bySpace, bottom - gap)));
            SlotY = barCentreY;
            SlotLeft = screen.SafeX + side;
            SlotStep = SlotPx + gap;
            SellX = screen.SafeX + screen.SafeWidth - side - SlotPx * 0.5f;

            HeaderX = screen.SafeX + screen.SafeWidth * 0.5f;
            HeaderY = areaTop + top * 0.5f;
            DragThresholdPx = spec.dragThresholdDp * Density;
        }

        public int Columns { get; }
        public int Rows { get; }
        public int InventorySlots { get; }
        /// <summary>Pixels per dp.</summary>
        public float Density { get; }
        public bool UsedFallbackDpi { get; }
        public float CellPx { get; }
        public float CellDp { get; }
        public bool HeightLimited { get; }
        public bool BelowComfort { get; }
        public float BoardLeft { get; }
        public float BoardTop { get; }
        public float BoardWidth { get; }
        public float BoardHeight { get; }
        public float SlotPx { get; }
        public float SlotY { get; }
        public float SlotLeft { get; }
        public float SlotStep { get; }
        public float SellX { get; }
        /// <summary>Centre of the area above the board (feedback text in M2, top bar and orders later).</summary>
        public float HeaderX { get; }
        public float HeaderY { get; }
        public float DragThresholdPx { get; }

        public float CellCentreX(int cell) => BoardLeft + (cell % Columns + 0.5f) * CellPx;
        public float CellCentreY(int cell) => BoardTop - (cell / Columns + 0.5f) * CellPx;
        public float SlotCentreX(int slot) => SlotLeft + SlotPx * 0.5f + slot * SlotStep;

        /// <summary>Board cell under a screen point, or -1.</summary>
        public int CellAt(float x, float y)
        {
            float fx = (x - BoardLeft) / CellPx;
            float fy = (BoardTop - y) / CellPx;
            if (fx < 0f || fy < 0f || fx >= Columns || fy >= Rows)
            {
                return -1;
            }

            return (int)fy * Columns + (int)fx;
        }

        /// <summary>Inventory slot under a screen point, or -1.</summary>
        public int SlotAt(float x, float y)
        {
            for (int s = 0; s < InventorySlots; s++)
            {
                if (Math.Abs(x - SlotCentreX(s)) <= SlotPx * 0.5f && Math.Abs(y - SlotY) <= SlotPx * 0.5f)
                {
                    return s;
                }
            }

            return -1;
        }

        /// <summary>True anywhere over the inventory row (any slot or the gaps between them).</summary>
        public bool IsOverInventory(float x, float y)
        {
            if (InventorySlots == 0)
            {
                return false;
            }

            float left = SlotLeft;
            float right = SlotCentreX(InventorySlots - 1) + SlotPx * 0.5f;
            return x >= left && x <= right && Math.Abs(y - SlotY) <= SlotPx * 0.5f;
        }

        public bool IsOverSell(float x, float y) =>
            Math.Abs(x - SellX) <= SlotPx * 0.5f && Math.Abs(y - SlotY) <= SlotPx * 0.5f;
    }
}

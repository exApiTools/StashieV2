using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Enums;
using SharpDX;
using System;
using System.Collections.Generic;
using static Stashie.StashieCore;
using Vector2N = System.Numerics.Vector2;

namespace Stashie.Compartments;

/// <summary>
/// Indicate the ignored intenvory slots (<see cref="StashieSettings.IgnoredCells"/> and
/// <see cref="StashieSettings.IgnoredExpandedCells"/>) by drawing a small lock icon on 
/// the inventory itself and allow toggling the state on-the-fly with a hotkey.
/// The hotkey toggles all slots occupied by the hovered item assuming original state
/// from the top left one or, in case of no item, toggles only the hovered slot.
/// </summary>
internal static class IgnoredCellsOverlay
{
    private const int LockArcSegments = 8;
    private static readonly Color LockColor = Color.Gold;
    private static readonly Color KeyholeColor = new(30, 30, 30, 255);

    private sealed record Grid(RectangleF Rect, int[,] Cells, int Width, int Height, InventorySlotE Slot)
    {
        public float CellWidth => Rect.Width / Width;
        public float CellHeight => Rect.Height / Height;
    }

    private static IEnumerable<Grid> VisibleGrids()
    {
        var grids = new List<Grid>(2);
        try
        {
            var panel = Main.GameController.IngameState.IngameUi.InventoryPanel;
            if (panel == null || !panel.IsVisible)
                return grids;

            AddGrid(grids, panel[InventoryIndex.PlayerInventory], Main.Settings.IgnoredCells, 12, 5, InventorySlotE.MainInventory1);
            AddGrid(grids, panel[InventoryIndex.PlayerExpandedInventory], Main.Settings.IgnoredExpandedCells, 4, 5, InventorySlotE.ExpandedMainInventory1);
        }
        catch
        {
            // UI can be transiently unreadable during zone/panel transitions.
        }

        return grids;
    }

    private static void AddGrid(List<Grid> grids, Element element, int[,] cells, int width, int height, InventorySlotE slot)
    {
        if (element == null || !element.IsVisible || cells == null)
            return;
        if (cells.GetLength(0) < height || cells.GetLength(1) < width)
            return;

        var rect = element.GetClientRect();
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        grids.Add(new Grid(rect, cells, width, height, slot));
    }

    /// <summary>Called every Render. Draws a small lock in the bottom-right corner of every ticked inventory slot.</summary>
    public static void Draw()
    {
        if (!Main.Settings.ShowIgnoredCells)
            return;

        foreach (var grid in VisibleGrids())
        {
            var cellW = grid.CellWidth;
            var cellH = grid.CellHeight;
            var cellMin = Math.Min(cellW, cellH);
            var height = cellMin * 0.19f;
            var width = height * 0.8f;
            var padding = Math.Max(3f, cellMin * 0.06f);

            for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                if (grid.Cells[y, x] == 0)
                    continue;

                var cellRight = grid.Rect.X + (x + 1) * cellW;
                var cellBottom = grid.Rect.Y + (y + 1) * cellH;
                DrawLock(new Vector2N(cellRight - width - padding, cellBottom - height - padding), width, height);
            }
        }
    }

    /// <summary>A padlock in a <paramref name="width"/> x <paramref name="height"/> box at <paramref name="topLeft"/>: filled body, arched shackle, keyhole.</summary>
    private static void DrawLock(Vector2N topLeft, float width, float height)
    {
        var size = height;
        var thickness = Math.Max(1.2f, size * 0.12f);
        var bodyTop = topLeft.Y + size * 0.45f;

        // Shackle: two legs rising from the body, joined by a half circle.
        var radius = width * 0.3f;
        var centerX = topLeft.X + width / 2f;
        var arcCenterY = topLeft.Y + radius + thickness / 2f;
        var points = new Vector2N[LockArcSegments + 3];
        points[0] = new Vector2N(centerX - radius, bodyTop);
        for (var i = 0; i <= LockArcSegments; i++)
        {
            var angle = MathF.PI + MathF.PI * i / LockArcSegments;
            points[i + 1] = new Vector2N(centerX + radius * MathF.Cos(angle), arcCenterY + radius * MathF.Sin(angle));
        }
        points[^1] = new Vector2N(centerX + radius, bodyTop);
        Main.Graphics.DrawPolyLine(points, LockColor, thickness);

        // Body and keyhole.
        Main.Graphics.DrawBox(new Vector2N(topLeft.X, bodyTop), new Vector2N(topLeft.X + width, topLeft.Y + size), LockColor, size * 0.12f);
        Main.Graphics.DrawCircleFilled(new Vector2N(centerX, bodyTop + (topLeft.Y + size - bodyTop) / 2f), Math.Max(1f, size * 0.09f), KeyholeColor, 8);
    }

    /// <summary>Called every Tick. Toggles the ignore state of the cell(s) under the mouse.</summary>
    public static void HandleHotkey()
    {
        if (!Main.Settings.ToggleIgnoredCellHotkey.PressedOnce())
            return;

        try
        {
            // Client rects are window-relative; the mouse position is screen-space.
            var windowTopLeft = Main.GameController.Window.GetWindowRectangle().TopLeft;
            var mouse = Input.MousePositionNum - new Vector2N(windowTopLeft.X, windowTopLeft.Y);

            foreach (var grid in VisibleGrids())
            {
                if (!grid.Rect.Contains(mouse.X, mouse.Y))
                    continue;

                var cx = Math.Clamp((int)((mouse.X - grid.Rect.X) / grid.CellWidth), 0, grid.Width - 1);
                var cy = Math.Clamp((int)((mouse.Y - grid.Rect.Y) / grid.CellHeight), 0, grid.Height - 1);
                ToggleAt(grid, cx, cy);
                return;
            }
        }
        catch (Exception e)
        {
            Main.LogError($"Stashie: toggling ignored cell failed: {e.Message}");
        }
    }

    private static void ToggleAt(Grid grid, int cx, int cy)
    {
        var items = Main.GameController.IngameState.Data.ServerData.PlayerInventories[(int)grid.Slot]?.Inventory?.InventorySlotItems;
        if (items != null)
        {
            foreach (var invItem in items)
            {
                if (invItem?.Item == null || invItem.Address == 0)
                    continue;

                var baseC = invItem.Item.GetComponent<Base>();
                var sizeX = Math.Max(1, (int)(baseC?.ItemCellsSizeX ?? 1));
                var sizeY = Math.Max(1, (int)(baseC?.ItemCellsSizeY ?? 1));
                var posX = invItem.PosX;
                var posY = invItem.PosY;

                if (cx < posX || cx >= posX + sizeX || cy < posY || cy >= posY + sizeY)
                    continue;

                // The top-left cell decides, matching Utility.CheckIgnoreCells.
                var inBounds = posX >= 0 && posX < grid.Width && posY >= 0 && posY < grid.Height;
                var newValue = inBounds && grid.Cells[posY, posX] != 0 ? 0 : 1;

                for (var y = Math.Max(0, posY); y < Math.Min(grid.Height, posY + sizeY); y++)
                for (var x = Math.Max(0, posX); x < Math.Min(grid.Width, posX + sizeX); x++)
                    grid.Cells[y, x] = newValue;

                return;
            }
        }

        // Empty slot: toggle just this cell.
        grid.Cells[cy, cx] ^= 1;
    }
}

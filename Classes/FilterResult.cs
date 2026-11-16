using ItemFilterLibrary;
using Stashie.Filter;
using System.Drawing;

namespace Stashie.Classes;

public class FilterResult(CustomFilter.Filter filter, ItemData itemData, RectangleF clientRect, int invPosX, int invPosY)
{
    public CustomFilter.Filter Filter { get; } = filter;
    public ItemData ItemData { get; } = itemData;
    public int StashIndex { get; } = filter.StashIndexNode.Index;
    public RectangleF ClientRect { get; } = clientRect;
    public (int X, int Y) inventoryPos { get; } = (invPosX, invPosY);
    public bool SkipSwitchTab { get; } = filter.Affinity ?? false;
    public bool ShiftForStashing { get; } = filter.Shifting ?? false;
}
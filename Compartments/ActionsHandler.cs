using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using Stashie.Classes;
using System;
using System.Collections;
using System.Linq;
using System.Windows.Forms;
using static Stashie.StashieCore;
using ExileCore.Shared.Helpers;

namespace Stashie.Compartments;

internal class ActionsHandler
{
    public static int GetIndexOfCurrentVisibleTab() => Main.GameController.Game.IngameState.IngameUi.StashElement.IndexVisibleStash;

    public static void CleanUp()
    {
        DebugWindow.LogMsg("Stashie: Done. Releasing modifier keys..");
        Input.KeyUp(Keys.LControlKey);
        Input.KeyUp(Keys.LShiftKey);
    }

    public static void HandleSwitchToTabEvent(object tab)
    {
        switch (tab)
        {
            case int index:
                Main.CoroutineWorker = new Coroutine(ActionCoRoutine.ProcessSwitchToTab(index), Main, CoroutineName);
                break;

            case string name:
                if (!RenamedAllStashNames.Contains(name))
                {
                    DebugWindow.LogMsg($"{Main.Name}: can't find tab with name '{name}'.");
                    break;
                }

                var tempIndex = RenamedAllStashNames.IndexOf(name);
                Main.CoroutineWorker = new Coroutine(ActionCoRoutine.ProcessSwitchToTab(tempIndex), Main, CoroutineName);
                DebugWindow.LogMsg($"{Main.Name}: Switching to tab with index: {tempIndex} ('{name}').");
                break;

            default:
                DebugWindow.LogMsg("The received argument is not a string or an integer.");
                break;
        }

        Core.ParallelRunner.Run(Main.CoroutineWorker);
    }

    public static IEnumerator SwitchToTab(int tabIndex)
    {
        Main.VisibleStashIndex = GetIndexOfCurrentVisibleTab();
        var travelDistance = Math.Abs(tabIndex - Main.VisibleStashIndex);
        if (travelDistance == 0)
            yield break;
        
        yield return SwitchToTabViaArrowKeys(tabIndex);
    }

    public static IEnumerator SwitchToTabViaArrowKeys(int tabIndex, int numberOfTries = 1)
    {
        if (numberOfTries >= 3)
        {
            yield break;
        }

        var indexOfCurrentVisibleTab = GetIndexOfCurrentVisibleTab();
        var travelDistance = tabIndex - indexOfCurrentVisibleTab;
        var tabIsToTheLeft = travelDistance < 0;
        travelDistance = Math.Abs(travelDistance);

        if (tabIsToTheLeft)
        {
            yield return PressKey(Keys.Left, travelDistance);
        }
        else
        {
            yield return PressKey(Keys.Right, travelDistance);
        }

        yield return Delay();

        if (GetIndexOfCurrentVisibleTab() != tabIndex)
        {
            yield return SwitchToTabViaArrowKeys(tabIndex, numberOfTries + 1);
        }
    }

    public static IEnumerator PressKey(Keys key, int repetitions = 1)
    {
        for (var i = 0; i < repetitions; i++)
        {
            yield return Input.KeyPress(key);
            yield return new WaitTime(Main.Settings.StashItemDelay);
        }
    }

    public static IEnumerator Delay(int ms = 0)
    {
        yield return new WaitTime(Main.Settings.ExtraDelay.Value + ms);
    }

    public static InventoryType GetTypeOfCurrentVisibleStash()
    {
        var stashPanelVisibleStash = Main.GameController.Game.IngameState.IngameUi?.StashElement?.VisibleStash;
        return stashPanelVisibleStash?.InvType ?? InventoryType.InvalidInventory;
    }

    public static IEnumerator StashItemsIncrementer()
    {
        Main.CoroutineIteration++;

        yield return StashItems();
    }

    public static IEnumerator StashItems()
    {
        Main.PublishEvent("stashie_start_drop_items", null);

        Main.VisibleStashIndex = GetIndexOfCurrentVisibleTab();
        if (Main.VisibleStashIndex < 0)
        {
            Main.LogMessage($"Stashie: VisibleStashIndex was invalid: {Main.VisibleStashIndex}, stopping.");
            yield break;
        }

        var items = Main.DropItems;

        // If bulk stash is enabled, filter out duplicates of stackable currency and fragments so we can right-click stash them in bulk
        if (Main.Settings.EnableBulkStash) {
            items = items
                .GroupBy(x => x.ItemData.ClassName == "StackableCurrency" || x.ItemData.ClassName == "Fragment" ? x.ItemData.Name : null)               
                .SelectMany(g => string.IsNullOrEmpty(g.Key) ? g.Select(x => x) : new[] { g.First() })
                .ToList();
        }

        var itemsSortedByStash = items
            .OrderBy(x => (
                x.SkipSwitchTab || x.StashIndex == Main.VisibleStashIndex ? 0 : 1,
                x.SkipSwitchTab || x.StashIndex == Main.VisibleStashIndex ? int.MaxValue : x.StashIndex
            ))
            .ThenBy(x => x.ShiftForStashing)
            .ThenBy(x => x.inventoryPos.X)
            .ThenBy(x => x.inventoryPos.Y)
            .ToList();

        Input.KeyDown(Keys.LControlKey);
        yield return Delay();

        bool shiftHeld = false;
        foreach (var stashResult in itemsSortedByStash) {            
            if (stashResult.ShiftForStashing && !shiftHeld) {
                Input.KeyDown(Keys.LShiftKey);
                shiftHeld = true;
                yield return Delay();
            } else if (!stashResult.ShiftForStashing && shiftHeld) {
                Input.KeyUp(Keys.LShiftKey);
                shiftHeld = false;
                yield return Delay();
            }

            Main.CoroutineIteration++;
            Main.CoroutineWorker?.UpdateTicks(Main.CoroutineIteration);
            var maxTryTime = Main.DebugTimer.ElapsedMilliseconds + 2000;

            //move to correct tab
            if (!stashResult.SkipSwitchTab)
                yield return SwitchToTab(stashResult.StashIndex);

            yield return new WaitFunctionTimed(() => Main.GameController.IngameState.IngameUi.StashElement.VisibleStash != null, true, 2000,
                $"Error while loading tab, Index: {Main.VisibleStashIndex}"); //maybe replace waittime with Setting option

            yield return new WaitFunctionTimed(() => GetTypeOfCurrentVisibleStash() != InventoryType.InvalidInventory, true, 2000,
                $"Error with inventory type, Index: {Main.VisibleStashIndex}"); //maybe replace waittime with Setting option

            yield return StashItem(stashResult);

            Main.DebugTimer.Restart();
        }
    }

    public static IEnumerator StashItem(FilterResult stashResult)
    {
        var position = stashResult.ClientRect.ClickRandom() + Main.ClickWindowOffset;
        Input.SetCursorPos(position);
        yield return new WaitTime(Main.Settings.HoverItemDelay);

        // If bulk stash is enabled, we ctrl-right click stackable currency and fragments to stash them in bulk
        if (Main.Settings.EnableBulkStash && (stashResult.ItemData.ClassName == "StackableCurrency" || stashResult.ItemData.ClassName == "Fragment")) 
            Input.Click(MouseButtons.Right);
        else 
            Input.Click(MouseButtons.Left);
        
        yield return new WaitTime(Main.Settings.StashItemDelay);
    }
}
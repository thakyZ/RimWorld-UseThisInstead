using System;
using System.Diagnostics;

using HarmonyLib;
using UnityEngine;
using Verse;

namespace UseThisInstead;

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), typeof(Rect), typeof(string), typeof(bool), typeof(bool),
    typeof(bool), typeof(TextAnchor))]
[HarmonyBefore("Mlie.ShowModUpdates")]
public static class Widgets_ButtonText_Postfix
{
    public static void Postfix(ref Rect rect, string? label)
    {
        if (UseThisInstead.AnythingChanged && !Find.WindowStack.AnyWindowAbsorbingAllInput)
        {
            ModsConfig.Save();
            ModsConfig.RestartFromChangedMods();
        }

        // Null check here because for some reason mods like "Mod List Diff" use a null value for the parameter "label".
        if (label?.Equals(LanguageDatabase.activeLanguage.FriendlyNameNative, StringComparison.Ordinal) == false ||
            (!UseThisInsteadMod.instance.Settings.AlwaysShow && !UseThisInstead.FoundModReplacementsFiltered.Any()))
        {
            return;
        }

        if (Find.WindowStack.AnyWindowAbsorbingAllInput)
        {
            return;
        }

        var newRect = rect;
        newRect.y += rect.height + 5f;
        rect.y += rect.height + 5f;

        if (Widgets.ButtonText(newRect, "UTI.replacements".Translate(UseThisInstead.FoundModReplacementsFiltered.Count)))
        {
            Find.WindowStack.Add(new Dialog_ModReplacements());
        }
    }
}
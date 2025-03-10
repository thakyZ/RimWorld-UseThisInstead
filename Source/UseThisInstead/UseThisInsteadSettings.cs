using System.Collections.Generic;

using Verse;

namespace UseThisInstead;

/// <summary>
///     Definition of the settings for the mod
/// </summary>
internal class UseThisInsteadSettings : ModSettings
{
    public bool AllMods;
    public bool AlwaysShow;
    public bool OnlyRelevant;
    public bool PreferOverlay;
    public bool VerboseLogging;
    public bool ShowIgnoredMods;
    private List<string>? ignoredMods;
    public List<string> IgnoredMods
    {
        get => ignoredMods ??= [];
        set => ignoredMods = value ?? [];
    }

    /// <summary>
    ///     Saving and loading the values
    /// </summary>
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref AllMods, "AllMods");
        Scribe_Values.Look(ref PreferOverlay, "PreferOverlay");
        Scribe_Values.Look(ref AlwaysShow, "AlwaysShow");
        Scribe_Values.Look(ref OnlyRelevant, "OnlyRelevant");
        Scribe_Values.Look(ref VerboseLogging, "VerboseLogging");
        Scribe_Values.Look(ref ShowIgnoredMods, "ShowIgnoredMods");
        Scribe_Collections.Look(ref ignoredMods, "IgnoredMods");
    }
}
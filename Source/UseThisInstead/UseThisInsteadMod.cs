using System.IO;
using Mlie;
using UnityEngine;
using Verse;

namespace UseThisInstead;

[StaticConstructorOnStartup]
internal class UseThisInsteadMod : Mod
{
    /// <summary>
    ///     The instance of the settings to be read by the mod
    /// </summary>
    public static UseThisInsteadMod instance;

    public static string ReplacementsFolderPath;

    private static string currentVersion;


    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="content"></param>
    public UseThisInsteadMod(ModContentPack content) : base(content)
    {
        instance = this;
        Settings = GetSettings<UseThisInsteadSettings>();
        currentVersion = VersionFromManifest.GetVersionFromModMetaData(content.ModMetaData);
        ReplacementsFolderPath = Path.Combine(Content.RootDir, "Replacements");
        UseThisInstead.CheckForReplacements();
    }

    /// <summary>
    ///     The instance-settings for the mod
    /// </summary>
    internal UseThisInsteadSettings Settings { get; }

    /// <summary>
    ///     The title for the mod-settings
    /// </summary>
    /// <returns></returns>
    public override string SettingsCategory()
    {
        return "Use This Instead";
    }

    /// <summary>
    ///     The settings-window
    ///     For more info: https://rimworldwiki.com/wiki/Modding_Tutorials/ModSettings
    /// </summary>
    /// <param name="rect"></param>
    public override void DoSettingsWindowContents(Rect rect)
    {
        var listing_Standard = new Listing_Standard();
        listing_Standard.Begin(rect);
        listing_Standard.Gap();
        listing_Standard.CheckboxLabeled("UTI.allMods".Translate(), ref Settings.AllMods, "UTI.allModstt".Translate());
        listing_Standard.CheckboxLabeled("UTI.veboseLogging".Translate(), ref Settings.VeboseLogging,
            "UTI.veboseLoggingtt".Translate());
        if (currentVersion != null)
        {
            listing_Standard.Gap();
            GUI.contentColor = Color.gray;
            listing_Standard.Label("UTI.modVersion".Translate(currentVersion));
            GUI.contentColor = Color.white;
        }

        listing_Standard.End();
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        UseThisInstead.CheckForReplacements();
    }

    public void WriteSettingsOnly()
    {
        base.WriteSettings();
    }
}
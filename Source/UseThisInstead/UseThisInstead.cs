using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Serialization;
using HarmonyLib;
using RimWorld;
using Steamworks;
using Verse;

namespace UseThisInstead;

[StaticConstructorOnStartup]
public static class UseThisInstead
{
    public static Dictionary<ulong, ModReplacement> ModReplacements = [];
    public static List<ModReplacement> FoundModReplacements;
    public static bool Scanning;
    public static bool Replacing;

    static UseThisInstead()
    {
        new Harmony("Mlie.UseThisInstead").PatchAll(Assembly.GetExecutingAssembly());
    }

    public static void CheckForReplacements(bool noWait = false)
    {
        if (Scanning)
        {
            return;
        }

        if (noWait)
        {
            checkForReplacements();
            return;
        }

        Scanning = true;
        new Thread(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            checkForReplacements();
        }).Start();
    }

    private static void checkForReplacements()
    {
        if (!ModReplacements.Any())
        {
            LoadAllReplacementFiles();
        }

        FoundModReplacements = [];
        var modsToCheck = ModLister.AllInstalledMods;
        if (!UseThisInsteadMod.instance.Settings.AllMods)
        {
            modsToCheck = ModsConfig.ActiveModsInLoadOrder;
        }

        foreach (var mod in modsToCheck)
        {
            if (mod.Official)
            {
                continue;
            }

            var publishedFileId = mod.GetPublishedFileId();
            if (publishedFileId == PublishedFileId_t.Invalid)
            {
                LogMessage($"Ignoring {mod.Name} since its a local mod and does not have a steam PublishedFileId");
                continue;
            }


            if (!ModReplacements.TryGetValue(publishedFileId.m_PublishedFileId, out var replacement))
            {
                continue;
            }

            var replacementPublishedFileId = replacement.GetReplacementPublishedFileId();
            if (!mod.Active &&
                ModLister.AllInstalledMods.Any(data => data.GetPublishedFileId() == replacementPublishedFileId))
            {
                LogMessage($"Ignoring {mod.Name} since its not active and its replacement is also downloaded");
                continue;
            }

            replacement.ModMetaData = mod;
            FoundModReplacements.Add(replacement);
        }

        FoundModReplacements = FoundModReplacements.OrderBy(replacement => replacement.ModName).ToList();

        LogMessage($"[UseThisInstead]: Found {FoundModReplacements.Count} replacements", true);

        Scanning = false;
    }

    public static void LoadAllReplacementFiles()
    {
        ModReplacements = [];

        var replacementFiles = Directory.GetFiles(UseThisInsteadMod.ReplacementsFolderPath, "*.xml");
        foreach (var replacementFile in replacementFiles)
        {
            using var streamReader = new StreamReader(replacementFile);
            var xml = streamReader.ReadToEnd();
            try
            {
                var serializer = new XmlSerializer(typeof(ModReplacement));
                var replacement = (ModReplacement)serializer.Deserialize(new StringReader(xml));
                ModReplacements[replacement.SteamId] = replacement;
            }
            catch (Exception exception)
            {
                LogMessage($"Failed to parse xml for {replacementFile}: {exception}", warning: true);
            }
        }

        LogMessage($"Loaded {ModReplacements.Count} possible replacements");
    }

    public static void ReplaceMod(ModReplacement modReplacement)
    {
        if (Replacing)
        {
            return;
        }

        Replacing = true;
        var currentMod = modReplacement.ModMetaData;
        var currentModPublishedId = currentMod.GetPublishedFileId();
        var replacementPublishedId = modReplacement.GetReplacementPublishedFileId();
        var installedMods = ModLister.AllInstalledMods.ToList();
        var justReplace = !currentMod.Active || modReplacement.ReplacementModId == modReplacement.ModId;
        var activeMods = ModsConfig.ActiveModsInLoadOrder.Select(data => data.PackageIdPlayerFacing).ToList();

        Messages.Message("UTI.unsubscribing".Translate(modReplacement.ModName, modReplacement.SteamId),
            MessageTypeDefOf.NeutralEvent);
        SteamUGC.UnsubscribeItem(currentModPublishedId);
        var unsubscribedMod =
            installedMods.FirstOrDefault(data => data.GetPublishedFileId() == currentModPublishedId);
        var counter = 0;
        while (unsubscribedMod != null)
        {
            counter++;
            if (counter % 120 == 0)
            {
                break;
            }

            Thread.Sleep(500);
            installedMods = ModLister.AllInstalledMods.ToList();
            unsubscribedMod = installedMods.FirstOrDefault(data =>
                data.GetPublishedFileId() == replacementPublishedId);
        }

        if (unsubscribedMod != null)
        {
            Messages.Message(
                "UTI.failedToUnsubscribe".Translate(modReplacement.ModName,
                    modReplacement.SteamId), MessageTypeDefOf.NegativeEvent);
            Replacing = false;
            return;
        }

        Messages.Message("UTI.unsubscribed".Translate(modReplacement.ModName), MessageTypeDefOf.PositiveEvent);

        var subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == replacementPublishedId);
        if (subscribedMod == null)
        {
            Messages.Message(
                "UTI.subscribing".Translate(modReplacement.ReplacementName,
                    modReplacement.ReplacementSteamId), MessageTypeDefOf.NeutralEvent);
            SteamUGC.SubscribeItem(replacementPublishedId);

            counter = 0;
            while (subscribedMod == null)
            {
                counter++;
                if (counter % 120 == 0)
                {
                    break;
                }

                Thread.Sleep(500);
                installedMods = ModLister.AllInstalledMods.ToList();
                subscribedMod = installedMods.FirstOrDefault(data =>
                    data.GetPublishedFileId() == replacementPublishedId);
            }

            if (subscribedMod == null)
            {
                Messages.Message(
                    "UTI.failedToSubscribe".Translate(modReplacement.ReplacementName,
                        modReplacement.ReplacementSteamId), MessageTypeDefOf.NegativeEvent);
                Replacing = false;
                return;
            }

            Messages.Message("UTI.subscribed".Translate(modReplacement.ReplacementName),
                MessageTypeDefOf.PositiveEvent);
        }

        if (justReplace)
        {
            Replacing = false;
        }

        activeMods.Replace(modReplacement.ModId, modReplacement.ReplacementModId);
        ModsConfig.SetActiveToList(activeMods);
        Messages.Message("UTI.modlistChanged".Translate(), MessageTypeDefOf.NeutralEvent);
        Replacing = false;
    }

    public static void LogMessage(string message, bool force = false, bool warning = false)
    {
        if (warning)
        {
            Log.Warning($"[UseThisInstead]: {message}");
            return;
        }

        if (!force && !UseThisInsteadMod.instance.Settings.VeboseLogging)
        {
            return;
        }

        Log.Message($"[UseThisInstead]: {message}");
    }
}
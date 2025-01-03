using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;
using HarmonyLib;
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
    public static bool ActivityMonitor;
    public static bool AnythingChanged;
    public static List<string> StatusMessages;

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

        LogMessage($"Found {FoundModReplacements.Count} replacements", true);

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
        StatusMessages = [];
        var justReplace = !modReplacement.ModMetaData.Active || modReplacement.ReplacementModId == modReplacement.ModId;
        if (!justReplace)
        {
            ModsConfig.SetActive(modReplacement.ModId, false);
        }

        if (!UnSubscribeToMod(modReplacement.ModMetaData.GetPublishedFileId(), modReplacement.ModName))
        {
            return;
        }

        Thread.Sleep(10);

        if (!SubscribeToMod(modReplacement.GetReplacementPublishedFileId(), modReplacement.ReplacementName))
        {
            return;
        }

        Thread.Sleep(10);

        var installedMods = ModLister.AllInstalledMods.ToList();
        var subscribedMod = installedMods.FirstOrDefault(data =>
            data.GetPublishedFileId() == modReplacement.GetReplacementPublishedFileId());

        if (subscribedMod == null)
        {
            Replacing = false;
            return;
        }

        var requirements = subscribedMod.GetRequirements();
        List<string> requirementIds = [];
        if (requirements.Any() && requirements.Any(requirement => !requirement.IsSatisfied))
        {
            StatusMessages.Add("UTI.checkingRequirements".Translate());

            foreach (var modRequirement in requirements.Where(requirement => !requirement.IsSatisfied))
            {
                if (modRequirement is ModDependency dependency)
                {
                    var match = Regex.Match(dependency.steamWorkshopUrl, @"\d+$");
                    if (!match.Success)
                    {
                        StatusMessages.Add("UTI.failedToSubscribe".Translate(dependency.displayName,
                            dependency.steamWorkshopUrl));
                        Replacing = false;
                        return;
                    }

                    var modId = ulong.Parse(match.Value);
                    if (SubscribeToMod(new PublishedFileId_t(modId), dependency.displayName))
                    {
                        requirementIds.Add(dependency.packageId);
                        continue;
                    }

                    Replacing = false;
                    return;
                }

                if (!justReplace && modRequirement is ModIncompatibility incompatibility)
                {
                    StatusMessages.Add("UTI.incompatibility".Translate(subscribedMod.Name,
                        incompatibility.displayName));
                }
            }
        }

        if (!justReplace)
        {
            StatusMessages.Add("UTI.activatingMods".Translate());
            foreach (var requirementId in requirementIds)
            {
                if (ModLister.GetActiveModWithIdentifier(requirementId, true) == null)
                {
                    ModsConfig.SetActive(requirementId, true);
                }
            }

            ModsConfig.SetActive(modReplacement.ReplacementModId, true);
        }

        Replacing = false;
    }

    private static bool UnSubscribeToMod(PublishedFileId_t modId, string modName)
    {
        var installedMods = ModLister.AllInstalledMods.ToList();
        var unsubscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);

        if (unsubscribedMod == null)
        {
            return true;
        }

        StatusMessages.Add("UTI.unsubscribing".Translate(modName, modId.m_PublishedFileId));
        SteamUGC.UnsubscribeItem(modId);
        unsubscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        var counter = 0;
        StatusMessages.Add("UTI.waitingUnsub".Translate());
        while (unsubscribedMod != null)
        {
            counter++;
            if (counter > 120)
            {
                break;
            }

            Thread.Sleep(500);
            ActivityMonitor = !ActivityMonitor;
            installedMods = ModLister.AllInstalledMods.ToList();
            unsubscribedMod = installedMods.FirstOrDefault(data =>
                data.GetPublishedFileId() == modId);
        }

        if (unsubscribedMod != null)
        {
            StatusMessages.Add("UTI.failedToUnsubscribe".Translate(modName, modId.m_PublishedFileId));
            Replacing = false;
            return false;
        }

        StatusMessages.Add("UTI.unsubscribed".Translate(modName));
        return true;
    }

    private static bool SubscribeToMod(PublishedFileId_t modId, string modName)
    {
        var installedMods = ModLister.AllInstalledMods.ToList();
        var subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        if (subscribedMod != null)
        {
            return true;
        }

        StatusMessages.Add("UTI.subscribing".Translate(modName, modId.m_PublishedFileId));
        SteamUGC.SubscribeItem(modId);

        var counter = 0;
        subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        StatusMessages.Add("UTI.waitingSub".Translate());
        while (subscribedMod == null)
        {
            counter++;
            if (counter > 120)
            {
                break;
            }

            Thread.Sleep(500);
            ActivityMonitor = !ActivityMonitor;
            installedMods = ModLister.AllInstalledMods.ToList();
            subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        }

        if (subscribedMod == null)
        {
            StatusMessages.Add("UTI.failedToSubscribe".Translate(modName, modId.m_PublishedFileId));
            Replacing = false;
            return false;
        }

        StatusMessages.Add("UTI.subscribed".Translate(modName));
        return true;
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Verse;

namespace UseThisInstead;

[StaticConstructorOnStartup]
public static class UseThisInstead
{
    public static Vector2 ScrollPosition;
    public static Dictionary<ulong, ModReplacement> ModReplacements { get; } = [];
    public static List<ModReplacement> FoundModReplacements { get; } = [];
    public static List<ModReplacement> FoundModReplacementsFiltered => [..FoundModReplacements.Where((ModReplacement foundMod) => !UseThisInsteadMod.instance.Settings.IgnoredMods.Any((string ignoredMod) => foundMod.ModId?.Equals(ignoredMod, StringComparison.Ordinal) == true))];
    public static bool Scanning { get; set; }
    public static bool Replacing { get; set; }
    public static bool ActivityMonitor { get; set; }
    public static bool AnythingChanged { get; set; }
    public static List<string> StatusMessages { get; } = [];
    public static float Progress => ItemsProcessed / TotalItemsToProcess;
    public static int ItemsProcessed { get; set; }
    public static int TotalItemsToProcess { get; set; }

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
            CheckForReplacements();
            return;
        }

        Scanning = true;
        Task.Run(async () =>
        {
            await Task.Yield();
            CheckForReplacements();
        });
    }

    private static void CheckForReplacements()
    {
        if (!ModReplacements.Any())
        {
            LoadAllReplacementFiles();
        }

        FoundModReplacements.Clear();
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

            if (UseThisInsteadMod.instance.Settings.OnlyRelevant && !replacement.ReplacementSupportsVersion())
            {
                LogMessage($"Ignoring {mod.Name} since it does not support this version of RimWorld");
                continue;
            }

            replacement.ModMetaData = mod;
            FoundModReplacements.Add(replacement);
        }

        FoundModReplacements.ReplaceAll(FoundModReplacements.OrderBy(replacement => replacement.ModName));

        LogMessage($"Found {FoundModReplacementsFiltered.Count} replacements", true);

        Scanning = false;
    }

    public static void LoadAllReplacementFiles()
    {
        ModReplacements.Clear();

        foreach (var replacementFile in Directory.GetFiles(UseThisInsteadMod.ReplacementsFolderPath, "*.xml"))
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

        LogMessage($"Loaded {FoundModReplacementsFiltered.Count} possible replacements");
    }

    public static async Task ReplaceModsAsync(List<ModReplacement> modReplacements, CancellationToken token)
    {
        if (Replacing)
        {
            return;
        }

        Replacing = true;
        StatusMessages.Clear();
        for (var counter = 0; counter < modReplacements.Count; counter++)
        {
            if (token.IsCancellationRequested)
            {
                UseThisInstead.ItemsProcessed = counter;
                return;
            }

            var modReplacement = modReplacements[counter];
            StatusMessages.Add("UTI.replacing".Translate(modReplacement.ModName, counter, modReplacements.Count));
            var justReplace = modReplacement.ModMetaData?.Active != true || (
                              modReplacement.ReplacementModId is not null && modReplacement.ModId is not null &&
                              modReplacement.ReplacementModId == modReplacement.ModId);
            if (!justReplace)
            {
                ModsConfig.SetActive(modReplacement.ModId, false);
            }

            if (modReplacement.ModMetaData is { } modMetaData && modReplacement.ModName is string modName && !await UnSubscribeToModAsync(modMetaData, modName, token))
            {
                UseThisInstead.ItemsProcessed = counter;
                continue;
            }

            await Task.Delay(10);

            if (modReplacement.ReplacementName is string replacementName && !await SubscribeToModAsync(modReplacement.GetReplacementPublishedFileId(), replacementName, token))
            {
                UseThisInstead.ItemsProcessed = counter;
                continue;
            }

            await Task.Delay(10);

            var installedMods = ModLister.AllInstalledMods.ToList();
            var subscribedMod = installedMods.FirstOrDefault(data =>
                data.GetPublishedFileId() == modReplacement.GetReplacementPublishedFileId());

            if (subscribedMod is null)
            {
                UseThisInstead.ItemsProcessed = counter;
                Replacing = false;
                continue;
            }

            var requirements = subscribedMod.GetRequirements();
            List<string> requirementIds = [];
            if (requirements.Any() && requirements.Any(requirement => !requirement.IsSatisfied))
            {
                StatusMessages.Add("UTI.checkingRequirements".Translate());

                foreach (var modRequirement in requirements.Where(requirement => !requirement.IsSatisfied))
                {
                    if (token.IsCancellationRequested)
                    {
                        UseThisInstead.ItemsProcessed = counter;
                        return;
                    }

                    if (modRequirement is ModDependency dependency)
                    {
                        var match = Regex.Match(dependency.steamWorkshopUrl, @"\d+$");
                        if (!match.Success)
                        {
                            StatusMessages.Add("UTI.failedToSubscribe".Translate(dependency.displayName,
                                dependency.steamWorkshopUrl));
                            Replacing = false;
                            continue;
                        }

                        if (ulong.TryParse(match.Value, out var modId) && await SubscribeToModAsync(new PublishedFileId_t(modId), dependency.displayName, token))
                        {
                            requirementIds.Add(dependency.packageId);
                            continue;
                        }

                        Replacing = false;
                        continue;
                    }

                    if (!justReplace && modRequirement is ModIncompatibility incompatibility)
                    {
                        StatusMessages.Add("UTI.incompatibility".Translate(subscribedMod.Name,
                            incompatibility.displayName));
                    }
                }
            }

            if (justReplace)
            {
                UseThisInstead.ItemsProcessed = counter;
                continue;
            }

            StatusMessages.Add("UTI.activatingMods".Translate());
            foreach (var requirementId in requirementIds)
            {
                if (token.IsCancellationRequested)
                {
                    UseThisInstead.ItemsProcessed = counter;
                    return;
                }

                if (ModLister.GetActiveModWithIdentifier(requirementId, true) is null)
                {
                    ModsConfig.SetActive(requirementId, true);
                }
            }

            ModsConfig.SetActive(modReplacement.ReplacementModId, true);
            UseThisInstead.ItemsProcessed = counter;
        }

        Replacing = false;
    }

    private static async Task<bool> UnSubscribeToModAsync(ModMetaData modMetaData, string modName, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!modMetaData.OnSteamWorkshop)
        {
            StatusMessages.Add("UTI.cantUnsubscribe".Translate(modName));
            return true;
        }

        var installedMods = ModLister.AllInstalledMods.ToList();
        var modId = modMetaData.GetPublishedFileId();
        var unsubscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);

        if (unsubscribedMod is null)
        {
            return true;
        }

        StatusMessages.Add("UTI.unsubscribing".Translate(modName, modId.m_PublishedFileId));
        SteamUGC.UnsubscribeItem(modId);
        unsubscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        var counter = 0;
        StatusMessages.Add("UTI.waitingUnsub".Translate());
        while (unsubscribedMod is not null)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            counter++;
            if (counter > 120)
            {
                break;
            }

            await Task.Delay(500);
            ActivityMonitor = !ActivityMonitor;
            installedMods = [..ModLister.AllInstalledMods];
            unsubscribedMod = installedMods.FirstOrDefault(data =>
                data.GetPublishedFileId() == modId);
        }

        if (unsubscribedMod is not null)
        {
            StatusMessages.Add("UTI.failedToUnsubscribe".Translate(modName, modId.m_PublishedFileId));
            Replacing = false;
            return false;
        }

        StatusMessages.Add("UTI.unsubscribed".Translate(modName));
        return true;
    }

    private static async Task<bool> SubscribeToModAsync(PublishedFileId_t modId, string modName, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        var installedMods = ModLister.AllInstalledMods.ToList();
        var subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        if (subscribedMod is not null)
        {
            return true;
        }

        StatusMessages.Add("UTI.subscribing".Translate(modName, modId.m_PublishedFileId));
        SteamUGC.SubscribeItem(modId);

        var counter = 0;
        subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        StatusMessages.Add("UTI.waitingSub".Translate());
        while (subscribedMod is null)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            counter++;
            if (counter > 120)
            {
                break;
            }

            await Task.Delay(500);
            ActivityMonitor = !ActivityMonitor;
            installedMods = [..ModLister.AllInstalledMods];
            subscribedMod = installedMods.FirstOrDefault(data => data.GetPublishedFileId() == modId);
        }

        if (subscribedMod is null)
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

        if (!force && !UseThisInsteadMod.instance.Settings.VerboseLogging)
        {
            return;
        }

        Log.Message($"[UseThisInstead]: {message}");
    }
}
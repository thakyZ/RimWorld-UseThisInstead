using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

using static UnityEngine.UIElements.UxmlAttributeDescription;

namespace UseThisInstead;

[StaticConstructorOnStartup]
public class Dialog_ModReplacements : Window
{
    private const int headerHeight = 50;
    private const int rowHeight = 60;
    private static Vector2 scrollPosition;
    private static readonly Color alternateBackground = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    private static readonly Vector2 previewImage = new Vector2(120f, 100f);
    private static readonly Vector2 buttonSize = new Vector2(140f, 25f);
    private static readonly Texture2D ArrowTex = ContentFinder<Texture2D>.Get("UI/Overlays/TutorArrowRight");
    private static readonly Texture2D steamIcon = ContentFinder<Texture2D>.Get("UI/Steam");
    private static readonly Texture2D folderIcon = ContentFinder<Texture2D>.Get("UI/Folder");
    private static readonly Texture2D ignoredIcon = ContentFinder<Texture2D>.Get("UI/Ignored");
    private static readonly Texture2D notIgnoredIcon = ContentFinder<Texture2D>.Get("UI/NotIgnored");
    private readonly List<ModReplacement> selectedReplacements = [];
    private readonly ReplacementStatus_Window replcementStatus;
    private readonly CancellationTokenSource cancellationTokenSource;

    public Dialog_ModReplacements()
    {
        doCloseX = true;
        forcePause = true;
        absorbInputAroundWindow = true;
        cancellationTokenSource = new CancellationTokenSource();
        replcementStatus = new ReplacementStatus_Window(cancellationTokenSource);
    }

    public override Vector2 InitialSize => new Vector2(700f, 700f);

    public override void Close(bool doCloseSound = true)
    {
        if (UseThisInstead.Replacing)
        {
            return;
        }

        base.Close(doCloseSound);
    }

    public override void PostClose()
    {
        base.PostClose();
        if (!UseThisInstead.AnythingChanged)
        {
            return;
        }

        Messages.Message("UTI.activeModsChanged".Translate(), MessageTypeDefOf.NeutralEvent);
        Find.WindowStack.Add(new Page_ModsConfig());
    }

    public override void DoWindowContents(Rect inRect)
    {
        var listingStandard = new Listing_Standard();
        listingStandard.Begin(inRect);
        Text.Font = GameFont.Medium;

        listingStandard.Label("UTI.foundReplacements".Translate(UseThisInstead.FoundModReplacementsFiltered.Count));
        if (UseThisInsteadMod.CurrentVersion is not null)
        {
            Text.Font = GameFont.Tiny;
            GUI.contentColor = Color.gray;
            listingStandard.Label("UTI.modVersion".Translate(UseThisInsteadMod.CurrentVersion));
            GUI.contentColor = Color.white;
        }

        Text.Font = GameFont.Small;
        if (UseThisInstead.AnythingChanged)
        {
            listingStandard.Label("UTI.restartNeeded".Translate());
        }
        else
        {
            listingStandard.Gap();
        }

        Rect subtitleRect;
        var list = UseThisInsteadMod.instance.Settings.ShowIgnoredMods ? UseThisInstead.FoundModReplacements : UseThisInstead.FoundModReplacementsFiltered;
        if (!UseThisInstead.Replacing)
        {
            if (SteamManager.Initialized)
            {
                listingStandard.CheckboxLabeled("UTI.preferOverlay".Translate(),
                    ref UseThisInsteadMod.instance.Settings.PreferOverlay,
                    "UTI.preferOverlaytt".Translate());
            }

            var settingChanged = false;
            var originalSetting = UseThisInsteadMod.instance.Settings.OnlyRelevant;
            listingStandard.CheckboxLabeled("UTI.onlyRelevant".Translate(),
                ref UseThisInsteadMod.instance.Settings.OnlyRelevant,
                "UTI.onlyRelevanttt".Translate());

            if (originalSetting != UseThisInsteadMod.instance.Settings.OnlyRelevant)
            {
                settingChanged = true;
            }

            originalSetting = UseThisInsteadMod.instance.Settings.AllMods;
            listingStandard.CheckboxLabeled("UTI.allMods".Translate(), ref UseThisInsteadMod.instance.Settings.AllMods,
                "UTI.allModstt".Translate());
            subtitleRect = listingStandard.GetRect(0);
            listingStandard.CheckboxLabeled("UTI.showIgnored".Translate(), ref UseThisInsteadMod.instance.Settings.ShowIgnoredMods,
                "UTI.showIgnoredtt".Translate());
            if (originalSetting != UseThisInsteadMod.instance.Settings.AllMods)
            {
                settingChanged = true;
            }

            if (settingChanged)
            {
                UseThisInsteadMod.instance.WriteSettingsOnly();
                UseThisInstead.CheckForReplacements(true);
            }
        }
        else
        {
            listingStandard.Label(UseThisInsteadMod.instance.Settings.OnlyRelevant
                ? "UTI.showingRelevant".Translate()
                : "UTI.showingAll".Translate());
            subtitleRect = listingStandard.Label(UseThisInsteadMod.instance.Settings.AllMods
                ? "UTI.checkingAll".Translate()
                : "UTI.checkingEnabled".Translate());
        }

        var buttonRect = listingStandard.GetRect(30f);

        for (var i = 0; i < selectedReplacements.Count; i++)
        {
            var replacement = selectedReplacements[i];
            if (UseThisInstead.FoundModReplacements.Contains(replacement))
            {
                continue;
            }

            selectedReplacements.Remove(replacement);
        }

        if (Widgets.ButtonText(buttonRect.LeftHalf().ContractedBy(5, 0),
                selectedReplacements.Any() ? "UTI.selectNone".Translate() : "UTI.selectAll".Translate(),
                active: UseThisInstead.FoundModReplacementsFiltered.Any()))
        {
            if (selectedReplacements.Any())
            {
                selectedReplacements.Clear();
            }
            else
            {
                selectedReplacements.AddRange(
                    UseThisInstead.FoundModReplacementsFiltered.Where(replacement => replacement.ReplacementSupportsVersion()));
            }
        }

        if (Widgets.ButtonText(buttonRect.RightHalf().ContractedBy(5, 0),
                "UTI.updateSelected".Translate(selectedReplacements.Count),
                active: selectedReplacements.Any()))
        {
            var replaceModString = "UTI.replaceMultipleMods";
            if (selectedReplacements.Any(replacement => replacement.ModMetaData?.Active == true))
            {
                replaceModString += "Active";
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                replaceModString.Translate(selectedReplacements.Count), OpenReplacementStatusWindow));
        }

        listingStandard.Gap();
        listingStandard.End();

        var borderRect = inRect;
        borderRect.y += subtitleRect.y + headerHeight;
        borderRect.height -= subtitleRect.y + headerHeight;
        var scrollContentRect = inRect;
        scrollContentRect.height = list.Count * (rowHeight + 1);

        scrollContentRect.width -= 20;
        scrollContentRect.x = 0;
        scrollContentRect.y = 0;

        var scrollListing = new Listing_Standard();
        Widgets.BeginScrollView(borderRect, ref scrollPosition, scrollContentRect);
        scrollListing.Begin(scrollContentRect);

        var alternate = false;
        foreach (ModReplacement modInfo in list)
        {
            var rowRectFull = scrollListing.GetRect(rowHeight);
            alternate = !alternate;
            if (alternate)
            {
                Widgets.DrawBoxSolid(rowRectFull, alternateBackground);
            }

            var rowRectLeft = rowRectFull.ContractedBy(5f).LeftPart(0.9f);
            var rowRectRight = rowRectFull.ContractedBy(5f).RightPart(0.1f);

            var modInfoRect = rowRectLeft.RightPartPixels(rowRectLeft.width - previewImage.x - 5f);

            var leftModRect = modInfoRect.LeftHalf().LeftPartPixels(modInfoRect.LeftHalf().width - (buttonSize.y / 2));
            var rightModRect = modInfoRect.RightHalf()
                .RightPartPixels(modInfoRect.LeftHalf().width - (buttonSize.y / 2));
            var spacerRect = modInfoRect.LeftHalf();
            spacerRect.width = buttonSize.y;
            spacerRect.x = leftModRect.xMax;
            spacerRect = spacerRect.ContractedBy(5);
            var previewRect = rowRectLeft.LeftPartPixels(previewImage.x);

            Widgets.DrawBoxSolid(previewRect.ContractedBy(1f), Color.black);
            if (modInfo.ModMetaData?.PreviewImage is not null)
            {
                Widgets.DrawTextureFitted(previewRect.ContractedBy(1f), modInfo.ModMetaData.PreviewImage, 1f);
            }

            Widgets.Label(leftModRect.TopHalf(), modInfo.ModName ?? "null");
            Widgets.Label(leftModRect.BottomHalf(), $"{"UTI.author".Translate()}{modInfo.Author ?? "null"}");
            TooltipHandler.TipRegion(leftModRect, tip: modInfo.SteamUri(true)?.AbsoluteUri ?? "null");
            Widgets.DrawHighlightIfMouseover(leftModRect);
            if (Widgets.ButtonInvisible(leftModRect) && modInfo.SteamUri(true) is { } steamUri1)
            {
                if (UseThisInsteadMod.instance.Settings.PreferOverlay)
                {
                    SteamUtility.OpenUrl(steamUri1.AbsoluteUri);
                }
                else
                {
                    Application.OpenURL(steamUri1.AbsoluteUri);
                }
            }

            Widgets.DrawTextureFitted(spacerRect, ArrowTex, 1f);

            if (UseThisInstead.Replacing)
            {
                Widgets.DrawTextureFitted(rowRectRight.LeftHalf(), TexButton.SpeedButtonTextures[0], 1f);
                TooltipHandler.TipRegion(rowRectRight.LeftHalf(), "UTI.alreadyReplacing".Translate());
            }

            var originalColor = GUI.color;
            if (!modInfo.ReplacementSupportsVersion())
            {
                GUI.color = Color.red;
            }
            else if (!UseThisInstead.Replacing)
            {
                TooltipHandler.TipRegion(rowRectRight.LeftHalf(), "UTI.replace".Translate());
                var selected = selectedReplacements.Contains(modInfo);
                var selectedOriginally = selected;
                Widgets.Checkbox(rowRectRight.LeftHalf().position, ref selected);
                if (selectedOriginally != selected)
                {
                    if (selected)
                    {
                        selectedReplacements.Add(modInfo);
                    }
                    else
                    {
                        selectedReplacements.Remove(modInfo);
                    }
                }
            }

            if (!UseThisInstead.Replacing)
            {
                Rect rightHalf = rowRectRight.RightHalf();
                Rect rect = new Rect(0, 0, notIgnoredIcon.width, notIgnoredIcon.height)
                  .ScaledBy(rightHalf.width / notIgnoredIcon.width)
                  .CenteredOnYIn(rightHalf)
                  .CenteredOnXIn(rightHalf);
                if (CheckModNotIgnored(modInfo))
                {
                   if (Widgets.ButtonImage(rect, notIgnoredIcon, false, "UTI.ignore".Translate()) && modInfo.ModId is not null)
                   {
                        UseThisInsteadMod.instance.Settings.IgnoredMods.Add(modInfo.ModId);
                        UseThisInsteadMod.instance.WriteSettingsOnly();
                   }
                }
                else
                {
                    if (Widgets.ButtonImage(rect, ignoredIcon, false, "UTI.unignore".Translate()) && modInfo.ModId is not null)
                    {
                        UseThisInsteadMod.instance.Settings.IgnoredMods.Remove(modInfo.ModId);
                        UseThisInsteadMod.instance.WriteSettingsOnly();
                    }
                }
            }

            Widgets.Label(rightModRect.TopHalf(), modInfo.ReplacementName);
            Widgets.Label(rightModRect.BottomHalf(), $"{"UTI.author".Translate()}{modInfo.ReplacementAuthor}");
            TooltipHandler.TipRegion(rightModRect, modInfo.SteamUri()?.AbsoluteUri ?? "null");
            if (!modInfo.ReplacementSupportsVersion())
            {
                GUI.color = originalColor;
                TooltipHandler.TipRegion(rightModRect,
                    "UTI.notUpdated".Translate(VersionControl.CurrentVersionStringWithoutBuild));
            }

            Widgets.DrawHighlightIfMouseover(rightModRect);
            if (Widgets.ButtonInvisible(rightModRect) && modInfo.SteamUri() is { } steamUri2)
            {
                if (UseThisInsteadMod.instance.Settings.PreferOverlay)
                {
                    SteamUtility.OpenUrl(steamUri2.AbsoluteUri);
                }
                else
                {
                    Application.OpenURL(steamUri2.AbsoluteUri);
                }
            }

            if (modInfo.ModMetaData?.OnSteamWorkshop == true)
            {
                Widgets.DrawTextureFitted(previewRect.BottomHalf().LeftHalf().LeftHalf(), steamIcon, 1f);
                TooltipHandler.TipRegion(previewRect.BottomHalf().LeftHalf().LeftHalf(), "SMU.SteamMod".Translate());
                continue;
            }

            Widgets.DrawTextureFitted(previewRect.BottomHalf().LeftHalf().LeftHalf(), folderIcon, 1f);
            TooltipHandler.TipRegion(previewRect.BottomHalf().LeftHalf().LeftHalf(), "SMU.LocalMod".Translate());
        }

        scrollListing.End();
        Widgets.EndScrollView();
    }

    public static bool CheckModNotIgnored(ModReplacement modInfo)
    {
        return !UseThisInsteadMod.instance.Settings.IgnoredMods.Any(x => x.Equals(modInfo.ModId, StringComparison.Ordinal));
    }

    public void OpenReplacementStatusWindow() {
        UseThisInstead.TotalItemsToProcess = selectedReplacements.Count;
        if (selectedReplacements.Any(replacement => replacement.ModMetaData?.Active == true))
        {
            UseThisInstead.AnythingChanged = true;
        }

        var task = Task.Run(async () => {
            await Task.Yield();
            await UseThisInstead.ReplaceModsAsync(selectedReplacements, cancellationTokenSource.Token);
        }, cancellationTokenSource.Token);
        Find.WindowStack.Add(replcementStatus);
    }
}
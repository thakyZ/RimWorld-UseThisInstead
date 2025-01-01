using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

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
    private static bool lastReplacingStatus;
    private static bool anythingChanged;

    public Dialog_ModReplacements()
    {
        doCloseX = true;
        forcePause = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new Vector2(700f, 700f);

    public override void Close(bool doCloseSound = true)
    {
        if (UseThisInstead.Replacing)
        {
            Messages.Message("UTI.alreadyReplacing".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        base.Close(doCloseSound);
    }

    public override void PostClose()
    {
        base.PostClose();
        if (anythingChanged)
        {
            ModsConfig.RestartFromChangedMods();
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (lastReplacingStatus != UseThisInstead.Replacing && !UseThisInstead.Replacing)
        {
            UseThisInstead.CheckForReplacements(true);
        }

        lastReplacingStatus = UseThisInstead.Replacing;
        var listingStandard = new Listing_Standard();
        listingStandard.Begin(inRect);
        Text.Font = GameFont.Medium;

        listingStandard.Label("UTI.foundReplacements".Translate(UseThisInstead.FoundModReplacements.Count));

        Text.Font = GameFont.Small;
        if (anythingChanged)
        {
            listingStandard.Label("UTI.restartNeeded".Translate());
        }
        else
        {
            listingStandard.Gap();
        }

        Rect subtitleRect;
        if (!UseThisInstead.Replacing)
        {
            var originalSetting = UseThisInsteadMod.instance.Settings.AllMods;
            listingStandard.CheckboxLabeled("UTI.allMods".Translate(), ref UseThisInsteadMod.instance.Settings.AllMods,
                "UTI.allModstt".Translate());
            subtitleRect = listingStandard.GetRect(0);

            if (originalSetting != UseThisInsteadMod.instance.Settings.AllMods)
            {
                UseThisInsteadMod.instance.WriteSettingsOnly();
                UseThisInstead.CheckForReplacements(true);
            }
        }
        else
        {
            subtitleRect = listingStandard.Label(UseThisInsteadMod.instance.Settings.AllMods
                ? "UTI.checkingAll".Translate()
                : "UTI.checkingEnabled".Translate());
        }

        listingStandard.End();

        var borderRect = inRect;
        borderRect.y += subtitleRect.y + headerHeight;
        borderRect.height -= subtitleRect.y + headerHeight;
        var scrollContentRect = inRect;
        scrollContentRect.height = UseThisInstead.FoundModReplacements.Count * (rowHeight + 1);

        scrollContentRect.width -= 20;
        scrollContentRect.x = 0;
        scrollContentRect.y = 0;

        var scrollListing = new Listing_Standard();
        Widgets.BeginScrollView(borderRect, ref scrollPosition, scrollContentRect);
        scrollListing.Begin(scrollContentRect);

        var alternate = false;
        foreach (var modInfo in UseThisInstead.FoundModReplacements)
        {
            var rowRectFull = scrollListing.GetRect(rowHeight);
            alternate = !alternate;
            if (alternate)
            {
                Widgets.DrawBoxSolid(rowRectFull, alternateBackground);
            }

            var rowRect = rowRectFull.ContractedBy(5f);

            var modInfoRect = rowRect.RightPartPixels(rowRect.width - previewImage.x - 5f);

            var leftModRect = modInfoRect.LeftHalf().LeftPartPixels(modInfoRect.LeftHalf().width - (buttonSize.y / 2));
            var rightModRect = modInfoRect.RightHalf()
                .RightPartPixels(modInfoRect.LeftHalf().width - (buttonSize.y / 2));
            var actionRect = modInfoRect.LeftHalf();
            actionRect.width = buttonSize.y;
            actionRect.x = leftModRect.xMax;
            actionRect = actionRect.ContractedBy(5);
            var previewRect = rowRect.LeftPartPixels(previewImage.x);

            if (modInfo.ModMetaData.PreviewImage != null)
            {
                Widgets.DrawBoxSolid(previewRect.ContractedBy(1f), Color.black);
                Widgets.DrawTextureFitted(previewRect.ContractedBy(1f), modInfo.ModMetaData.PreviewImage, 1f);
            }

            Widgets.Label(leftModRect.TopHalf(), modInfo.ModName);
            Widgets.Label(leftModRect.BottomHalf(), $"{"UTI.author".Translate()}{modInfo.Author}");
            TooltipHandler.TipRegion(leftModRect, modInfo.SteamUri(true).AbsoluteUri);
            Widgets.DrawHighlightIfMouseover(leftModRect);
            if (Widgets.ButtonInvisible(leftModRect))
            {
                Application.OpenURL(modInfo.SteamUri(true).AbsoluteUri);
            }

            if (UseThisInstead.Replacing)
            {
                Widgets.DrawTextureFitted(actionRect.CenteredOnYIn(modInfoRect), TexButton.SpeedButtonTextures[0], 1f);
                TooltipHandler.TipRegion(actionRect, "UTI.alreadyReplacing".Translate());
            }
            else
            {
                TooltipHandler.TipRegion(actionRect, "UTI.replace".Translate());
                if (Widgets.ButtonImageFitted(actionRect, ArrowTex))
                {
                    var replaceModString = "UTI.replaceMod";
                    if (modInfo.ModMetaData.Active)
                    {
                        replaceModString += "Active";
                    }

                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        replaceModString.Translate(modInfo.ModName, modInfo.ReplacementName, modInfo.Author,
                            modInfo.ReplacementAuthor, modInfo.Versions, modInfo.ReplacementVersions),
                        delegate
                        {
                            if (modInfo.ModMetaData.Active)
                            {
                                anythingChanged = true;
                            }

                            new Thread(() =>
                            {
                                Thread.CurrentThread.IsBackground = true;
                                UseThisInstead.ReplaceMod(modInfo);
                            }).Start();
                        }));
                }
            }

            var originalColor = GUI.color;
            if (!modInfo.ReplacementSupportsVersion())
            {
                GUI.color = Color.red;
            }

            Widgets.Label(rightModRect.TopHalf(), modInfo.ReplacementName);
            Widgets.Label(rightModRect.BottomHalf(), $"{"UTI.author".Translate()}{modInfo.ReplacementAuthor}");
            TooltipHandler.TipRegion(rightModRect, modInfo.SteamUri().AbsoluteUri);
            if (!modInfo.ReplacementSupportsVersion())
            {
                GUI.color = originalColor;
                TooltipHandler.TipRegion(rightModRect,
                    "UTI.notUpdated".Translate(VersionControl.CurrentVersionStringWithoutBuild));
            }

            Widgets.DrawHighlightIfMouseover(rightModRect);
            if (Widgets.ButtonInvisible(rightModRect))
            {
                Application.OpenURL(modInfo.SteamUri().AbsoluteUri);
            }
        }

        scrollListing.End();
        Widgets.EndScrollView();
    }
}
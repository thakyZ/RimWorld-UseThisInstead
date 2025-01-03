using System.Linq;
using UnityEngine;
using Verse;

namespace UseThisInstead;

[StaticConstructorOnStartup]
public class ReplacementStatus_Window : Window
{
    public ReplacementStatus_Window()
    {
        forcePause = true;
        absorbInputAroundWindow = true;
    }

    public override void Close(bool doCloseSound = true)
    {
        if (UseThisInstead.Replacing)
        {
            return;
        }

        UseThisInstead.CheckForReplacements(true);
        base.Close(doCloseSound);
    }

    public override void DoWindowContents(Rect inRect)
    {
        var statusMessages = UseThisInstead.StatusMessages.ToList();
        if (!statusMessages.Any())
        {
            return;
        }

        var listingStandard = new Listing_Standard();
        listingStandard.Begin(inRect);
        Text.Font = GameFont.Medium;
        listingStandard.Label("UTI.replacementStatus".Translate());
        Text.Font = GameFont.Small;
        listingStandard.Gap();
        foreach (var statusMessage in statusMessages)
        {
            listingStandard.Label(statusMessage);
        }

        listingStandard.Gap();
        if (UseThisInstead.Replacing)
        {
            listingStandard.Label(UseThisInstead.ActivityMonitor ? ". . ." : " . . ");
        }
        else
        {
            var imageRect = listingStandard.GetRect(50f);
            var lastStatus = statusMessages.Last();
            if (lastStatus == "UTI.failedToSubscribe".Translate() ||
                lastStatus == "UTI.failedToUnsubscribe".Translate())
            {
                Widgets.DrawTextureFitted(imageRect, Widgets.CheckboxOffTex, 1f);
            }
            else
            {
                Widgets.DrawTextureFitted(imageRect, Widgets.CheckboxOnTex, 1f);
            }

            listingStandard.GapLine();
            if (listingStandard.ButtonText("Close".Translate(), widthPct: 0.5f))
            {
                Close();
            }
        }

        listingStandard.End();
    }
}
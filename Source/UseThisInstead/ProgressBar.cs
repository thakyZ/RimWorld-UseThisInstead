using UnityEngine;
using Verse;

namespace UseThisInstead;

public static class ProgressBar {
  public static void Draw(Rect rect, float progress, Vector2 progressBarSize, int total, int step, string? label) {
    GameFont savedFont = Text.Font;
    Color savedColor = GUI.color;
    try {
      var progressBarRect = new Rect(rect.LeftHalf().width - (progressBarSize.x * 0.5f), rect.TopHalf().width - (progressBarSize.y * 0.5f), progressBarSize.x, progressBarSize.y);
      GUI.color = Color.gray;
      Widgets.DrawBox(progressBarRect, 1);
      var barWidth = progressBarRect.width * progress;
      Widgets.DrawRectFast(new Rect(progressBarRect.x, progressBarRect.y, barWidth, progressBarRect.height), Color.green);
      GUI.color = Color.white;
      Text.Font = GameFont.Tiny;
      Widgets.Label(new Rect(progressBarRect.x, progressBarRect.yMax + 2, progressBarRect.width, 20), $"({step}/{total})");
      if (label is not null) {
        Widgets.Label(new Rect(progressBarRect.x, progressBarRect.yMax + 2, progressBarRect.width, 20), label);
      }
    } finally {
      Text.Font = savedFont;
      GUI.color = savedColor;
    }
  }
}

using System.Collections.Generic;

namespace UseThisInstead;

public static class Extensions
{
    public static void ReplaceAll<T>(this List<T> list, params IEnumerable<T> replacements)
    {
        List<T> backup = [..replacements];
        list.Clear();
        list.AddRange(backup);
    }
}

using System.Text;
using Godot;

/// <summary>
/// Debug instrumentation: dump a Control subtree's resolved layout —
/// per node: name(type), global rect, visibility, combined minimum
/// size — one indented line each, under Render:Trace (quiet in ordinary
/// Render:Debug sessions; enable FOUREXHEX_LOG="Render:Trace" to read
/// it). Deferred-call it after a layout pass to compare "before vs
/// after" element geometry when a structural change munges a screen.
/// </summary>
public static class LayoutDump
{
    public static void Dump(Control? root, string tag, int maxDepth = 6)
    {
        if (root == null)
        {
            Log.Trace(Log.LogCategory.Render, $"[layout {tag}] root=null");
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"[layout {tag}]");
        Append(sb, root, 0, maxDepth);
        Log.Trace(Log.LogCategory.Render, sb.ToString());
    }

    private static void Append(StringBuilder sb, Control c, int depth, int maxDepth)
    {
        Rect2 r = c.GetGlobalRect();
        Vector2 min = c.GetCombinedMinimumSize();
        sb.AppendLine(
            $"{new string(' ', depth * 2)}{c.Name}({c.GetType().Name}) " +
            $"rect=({r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}) " +
            $"vis={(c.Visible ? "T" : "f")} min=({min.X:0}x{min.Y:0})");
        if (depth >= maxDepth) return;
        foreach (Node child in c.GetChildren())
        {
            if (child is Control cc) Append(sb, cc, depth + 1, maxDepth);
        }
    }
}

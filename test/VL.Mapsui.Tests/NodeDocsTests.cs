using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace VL.Mapsui.Tests;

/// <summary>
/// The XML documentation of every public member is what vvvv shows when a node is hovered and in
/// the NodeBrowser, so it is held to what a tooltip can carry.
/// </summary>
/// <remarks>
/// <para>The Gray Book (reference/extending/writing-nodes.md): summary "a one-liner", remarks "usage
/// instructions, warnings", param and returns "short info". Measured over VL.CoreLib's own XML
/// (2026-10-18): summary median 68 characters, remarks median 50. This package's remarks had a
/// median of 494 — design rationale that belongs in <c>//</c> comments and docs/ARCHITECTURE.md,
/// not in front of the user.</para>
/// <para>What the tooltip does with markup, seen on a hover: a <c>see cref</c> renders as nothing
/// ("Use  for all the attributes"), paragraphs run together, and a line break in the source is a
/// line break on screen. So none of those markups, and no hard wraps inside a sentence.</para>
/// <para>Copied verbatim between the family's repositories; only the anchor type differs.</para>
/// </remarks>
public class NodeDocsTests
{
    static readonly Assembly Package = typeof(MapNode).Assembly;

    const int SummaryLimit = 200;
    const int RemarksLimit = 300;
    const int PinLimit = 200;

    static readonly string[] ForbiddenMarkup = ["<see ", "<seealso", "<para", "<list", "<code"];

    [Fact]
    public void Every_tooltip_is_short_plain_text()
    {
        var problems = Check(Package).ToList();
        Assert.True(problems.Count == 0,
            $"{problems.Count} tooltip problem(s):\n  " + string.Join("\n  ", problems));
    }

    internal static IEnumerable<string> Check(Assembly assembly)
    {
        var xml = Path.ChangeExtension(assembly.Location, ".xml");
        Assert.True(File.Exists(xml), $"no XML documentation next to {assembly.Location}");

        var exported = assembly.GetExportedTypes().ToDictionary(t => t.FullName!.Replace('+', '.'));

        foreach (var member in XDocument.Load(xml).Descendants("member"))
        {
            var id = (string)member.Attribute("name")!;
            if (!IsPublic(id, exported))
                continue;

            foreach (var (tag, limit) in new[] { ("summary", SummaryLimit), ("remarks", RemarksLimit), ("param", PinLimit), ("returns", PinLimit) })
            {
                foreach (var element in member.Elements(tag))
                {
                    var where = tag == "param" ? $"{id} param {(string?)element.Attribute("name")}" : $"{id} {tag}";
                    var inner = string.Concat(element.Nodes().Select(n => n.ToString()));

                    foreach (var markup in ForbiddenMarkup.Where(inner.Contains))
                        yield return $"{where}: {markup.Trim()}...> - the tooltip cannot render it";

                    var text = Regex.Replace(Regex.Replace(element.Value, @"[ \t]+", " "), @"\s*\n\s*", "\n").Trim();
                    var length = text.Replace("\n", " ").Length;
                    if (length > limit)
                        yield return $"{where}: {length} characters, limit {limit}";

                    // A line break in the source is a line break in the tooltip.
                    var lines = text.Split('\n');
                    for (var i = 0; i < lines.Length - 1; i++)
                        if (!Regex.IsMatch(lines[i], @"[.:!?)]$"))
                        {
                            yield return $"{where}: a line break inside a sentence, after \"{Tail(lines[i])}\"";
                            break;
                        }
                }
            }
        }
    }

    static string Tail(string line) => line.Length <= 30 ? line : "..." + line[^30..];

    /// <summary>Is this documentation ID a public member of an exported type?</summary>
    static bool IsPublic(string id, Dictionary<string, Type> exported)
    {
        var kind = id[0];
        var full = id[2..];
        var paren = full.IndexOf('(');
        if (paren >= 0)
            full = full[..paren];

        if (kind == 'T')
            return exported.ContainsKey(full);

        var dot = full.LastIndexOf('.');
        if (dot < 0 || !exported.TryGetValue(full[..dot], out var type))
            return false;
        var name = full[(dot + 1)..];
        var tick = name.IndexOf("``", StringComparison.Ordinal);
        if (tick >= 0)
            name = name[..tick];

        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return kind switch
        {
            'M' when name == "#ctor" => type.GetConstructors(Public).Length > 0,
            'M' => type.GetMethods(Public).Any(m => m.Name == name),
            'P' => type.GetProperties(Public).Any(p => p.Name == name),
            'F' => type.GetFields(Public).Any(f => f.Name == name),
            'E' => type.GetEvents(Public).Any(e => e.Name == name),
            _ => false,
        };
    }
}

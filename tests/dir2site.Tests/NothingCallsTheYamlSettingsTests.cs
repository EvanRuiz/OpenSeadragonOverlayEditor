// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// The file beside an artifact is its <em>yaml</em>. The settings are what it holds.
/// </summary>
/// <remarks>
/// <c>docs/artifact-settings.md</c> has drawn this line correctly from the beginning — "every
/// artifact has a YAML file beside it holding its settings", "the yaml is where per-artifact
/// settings live" — and the docs being right did not stop the app from telling the user "1 settings
/// file is left over from something no longer in your folder". Prose in a docs page defines a word;
/// it does not defend one.
///
/// That is the same lesson the repository's other terminology guard was written for, about the same
/// file and one word over: there too the docs were corrected, a test was put over the docs, and the
/// word grew back in code written in parallel. So this guard watches the source and the copy rather
/// than the docs. (Naming that test here would trip it, which is its own small proof of the point.)
///
/// The rule is narrower than that one, because "settings" is a perfectly good word here for
/// everything except the file. Site Settings is a panel, SFTP settings are settings, and
/// <c>dir2site.yaml</c> genuinely is the site's settings file. What is banned is the phrase that
/// makes the <em>artifact's</em> file into "settings" — and the one that lists it beside the
/// previews, which is where it did the real damage: a dialog headed "settings or preview file"
/// while the list underneath held a folder of the user's photographs.
/// </remarks>
public class NothingCallsTheYamlSettingsTests
{
    /// <summary>
    /// "settings file(s)", except where something says whose — "site settings file" is honest, and
    /// <c>dir2site.yaml</c> is exactly that.
    /// </summary>
    private static readonly Regex TheFile =
        new(@"(?<!\bsite[\s-])(?<!\bproject[\s-])\bsettings\s+files?\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// The artifact's two derived things listed together, which only ever means the yaml and the
    /// previews — and named the yaml wrongly every time it was written.
    /// </summary>
    private static readonly Regex ListedWithPreviews =
        new(@"\bsettings\s+(and|or)\s+preview", RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("*.cs")]
    [InlineData("*.axaml")]
    [InlineData("*.html")]
    public void NoSourceFileCallsItThat(string pattern)
    {
        var root = RepoFiles.Root();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(root, pattern))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            offenders.AddRange(Offences(relative, File.ReadAllLines(file)));
        }

        Assert.True(offenders.Count == 0, Complaint(offenders));
    }

    /// <summary>
    /// Everything <see cref="RepoFiles"/> hands out, minus this file — which has to write the
    /// phrases down in order to search for them.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        RepoFiles.Sources(root, pattern)
            .Where(f => !Path.GetFileName(f).Equals(
                nameof(NothingCallsTheYamlSettingsTests) + ".cs", StringComparison.Ordinal));

    private static IEnumerable<string> Offences(string relative, string[] lines) =>
        lines
            .Select((text, index) => (Line: index + 1, Text: text))
            .Where(l => TheFile.IsMatch(l.Text) || ListedWithPreviews.IsMatch(l.Text))
            .Select(l => $"  {relative}:{l.Line}  {l.Text.Trim()}");

    private static string Complaint(IReadOnlyCollection<string> offenders) =>
        "the file beside an artifact is its yaml; the settings are what it holds — see\n"
        + "docs/artifact-settings.md, which has always said so. Name the file a yaml:\n"
        + string.Join("\n", offenders);

    /// <summary>
    /// The words that must stay legal, so the rule above cannot quietly grow into a ban on a
    /// perfectly good word.
    /// </summary>
    [Theory]
    [InlineData("Site Settings")]
    [InlineData("SFTP settings saved")]
    [InlineData("Could not save site settings: {ex.Message}")]
    [InlineData("the site settings file")]
    [InlineData("the settings every artifact has")]
    [InlineData("Added the settings that were missing to 1 yaml file")]
    [InlineData("the yaml is where per-artifact settings live")]
    public void TheseAreStillFine(string line)
    {
        Assert.Empty(Offences("x.cs", [line]));
    }

    [Theory]
    [InlineData("1 settings file is left over from something no longer in your folder.")]
    [InlineData("2 settings files are left over from things no longer in your folder.")]
    [InlineData("Removed the settings and previews for Portrait.jpg")]
    [InlineData("1 settings or preview file is left over")]
    public void TheseAreNot(string line)
    {
        Assert.NotEmpty(Offences("x.cs", [line]));
    }
}

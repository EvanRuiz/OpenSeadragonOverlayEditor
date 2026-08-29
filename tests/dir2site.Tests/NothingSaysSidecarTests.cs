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
/// The file of metadata beside an artifact is called its <em>yaml</em>. "Sidecar" is the word for
/// the arrangement, not for the file, and it means nothing to someone who has only ever seen
/// <c>Portrait.jpg</c> and <c>Portrait.jpg.yaml</c> sitting next to each other.
///
/// Keeping it out by hand did not work, twice. First it lived in the summaries on
/// <c>MarkdownRenderer</c> and <c>SiteGenerator</c> — what a person reads when they sit down to
/// document a feature — so it kept being copied into the README, corrected there, and copied in
/// again from the same comments. Those were reworded and a test was added over the docs. Then a
/// branch that had been open across the whole rename merged, and the word came back in code written
/// in parallel: 101 occurrences across twenty files, seven times what had been removed.
///
/// So the guard is the whole repository, not the docs. A rule enforced only where the last leak
/// happened is a rule that catches the last leak.
///
/// This file is the one place the word may appear, because searching for a word means naming it.
/// </summary>
public class NothingSaysSidecarTests
{
    private const string Forbidden = "sidecar";

    /// <summary>
    /// Every Markdown file a reader is handed: the README, and the pages under <c>docs/</c> it
    /// links to. Named individually rather than swept up by a glob, so a new docs page is a
    /// deliberate addition to this list and generated or vendored Markdown never wanders in.
    /// </summary>
    private static readonly string[] Docs =
    {
        "README.md",
        "docs/artifact-settings.md",
        "docs/folder-markers.md",
        "docs/the-footer.md",
        "docs/writing-articles.md",
        "docs/adding-videos.md",
    };

    public static TheoryData<string> ReaderFacingDocs()
    {
        var data = new TheoryData<string>();
        foreach (var doc in Docs) data.Add(doc);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReaderFacingDocs))]
    public void NoDocSaysIt(string relativePath)
    {
        var path = Path.Combine(RepoFiles.Root(), relativePath);
        Assert.True(File.Exists(path), $"{relativePath} is listed here but is not in the repository");

        AssertClean(relativePath, File.ReadAllLines(path));
    }

    /// <summary>
    /// And the source, which is where it grew back. Comments are what the docs get written from, and
    /// an identifier is read far more often than any comment — a test method called
    /// <c>ALegacySidecarJoinsTheCurrentConvention</c> teaches the word to everyone who reads the
    /// suite.
    ///
    /// <c>.axaml</c> and the site templates are here for a different reason: they are not a source
    /// the word could spread *from*, they are where it would be read by someone who never opens the
    /// repository. A <c>&lt;Run&gt;</c> in a view or a line in <c>collection.html</c> is copy, and a
    /// guard that watched only the code would pass while the welcome screen said it out loud.
    /// </summary>
    [Theory]
    [InlineData("*.cs")]
    [InlineData("*.axaml")]
    [InlineData("*.html")]
    public void NoSourceFileSaysIt(string pattern)
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
    /// Everything <see cref="RepoFiles"/> hands out, minus this file — which has to name the word
    /// in order to search for it.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        RepoFiles.Sources(root, pattern)
            .Where(f => !Path.GetFileName(f).Equals(
                nameof(NothingSaysSidecarTests) + ".cs", StringComparison.Ordinal));

    private static void AssertClean(string relative, string[] lines)
    {
        var offenders = Offences(relative, lines).ToList();
        Assert.True(offenders.Count == 0, Complaint(offenders));
    }

    private static IEnumerable<string> Offences(string relative, string[] lines) =>
        lines
            .Select((text, index) => (Line: index + 1, Text: text))
            .Where(l => l.Text.Contains(Forbidden, StringComparison.OrdinalIgnoreCase))
            .Select(l => $"  {relative}:{l.Line}  {l.Text.Trim()}");

    private static string Complaint(IReadOnlyCollection<string> offenders) =>
        $"\"{Forbidden}\" is an internal word for how the file sits, not a name a reader knows.\n"
        + "Call it the artifact's yaml, or its yaml metadata — in prose and in identifiers alike:\n"
        + string.Join("\n", offenders);

    /// <summary>
    /// The docs list has to keep covering <c>docs/</c>, or the rule quietly stops applying to the
    /// page someone adds next.
    /// </summary>
    [Fact]
    public void EveryDocsPageIsOnTheList()
    {
        var listed = Docs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var docsDir = Path.Combine(RepoFiles.Root(), "docs");

        var missing = Directory
            .GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            .Select(p => "docs/" + Path.GetRelativePath(docsDir, p)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .Where(p => !listed.Contains(p))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "these pages are in docs/ but not checked by this test — add them to "
            + $"{nameof(Docs)}:\n  " + string.Join("\n  ", missing));
    }
}

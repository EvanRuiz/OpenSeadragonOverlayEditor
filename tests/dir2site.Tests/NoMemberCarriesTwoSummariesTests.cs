// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// A member may not carry two <c>&lt;summary&gt;</c> blocks.
///
/// It always means the same accident: a new member was written directly above an existing docstring,
/// so it adopted a description of something else and left the member below undocumented. It is never
/// deliberate — which is what makes it worth a test rather than a review comment.
///
/// Nothing else catches it. <c>GenerateDocumentationFile</c> is off, so the compiler says nothing,
/// and the result reads plausibly: two well-written summaries, one attached to the wrong thing. In a
/// repo whose comments are the source its documentation gets written from, that is the defect with
/// no backstop.
///
/// It has happened three times here. <c>QuickSync</c> lost its docstring to <c>Preview</c> and went
/// undocumented on main; then twice in two commits on this branch, the second inside the commit
/// fixing the first.
/// </summary>
public class NoMemberCarriesTwoSummariesTests
{
    [Fact]
    public void NoTwoSummariesRunTogether()
    {
        var root = RepoFiles.Root();
        var offenders = new List<string>();

        foreach (var file in RepoFiles.Sources(root, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

            for (var i = 1; i < lines.Length; i++)
            {
                if (!lines[i].Trim().StartsWith("/// <summary>", StringComparison.Ordinal)) continue;

                // What comes before, once blank lines and bare `///` are stepped over. Taking
                // lines[i - 1] would only see the two blocks written flush against each other,
                // which is how the three known instances happened to be spaced — a guard shaped
                // around its own samples, which is the thing this file exists to argue against.
                // Pasting a member with a leading newline gives the blank-line variant, and that
                // is no less likely than pasting without one.
                var previous = PrecedingCode(lines, i);

                // One summary ends and the next begins with no member between them. Both the
                // multi-line and one-line forms end with the closing tag, so this catches either.
                if (previous is not null
                    && previous.EndsWith("</summary>", StringComparison.Ordinal))
                {
                    offenders.Add($"  {relative}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "these members carry two <summary> blocks, so one describes something else and a "
            + "member below is undocumented — move the stranded one down onto what it describes:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The nearest line above <paramref name="index"/> that is neither blank nor a bare
    /// <c>///</c>, or null if there is none. Blank lines and empty comment lines separate two
    /// doc blocks visually without putting anything between them.
    /// </summary>
    private static string? PrecedingCode(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var text = lines[i].Trim();
            if (text.Length == 0 || text == "///") continue;

            return text;
        }

        return null;
    }

}

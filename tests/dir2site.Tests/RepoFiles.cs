// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace dir2site.Tests;

/// <summary>
/// Finding the repository, and reading the files in it that a rule should apply to.
///
/// Three tests here walk the tree and assert something about everything they find. What they must
/// not read is the same for all of them and is not obvious: build output, generated sites, and —
/// the one that cost a merge — anything hidden, because <c>.claude/worktrees/</c> sits under the
/// main checkout and holds every other branch in flight. A guard that walked in there failed in the
/// main checkout over 381 lines of other people's code, and no change to this branch could fix it.
///
/// That policy lived in three copies, which is two more than can be kept in step by hand: the next
/// exclusion would have gone into whichever one its author was looking at.
/// </summary>
internal static class RepoFiles
{
    /// <summary>The checkout this test assembly was built in.</summary>
    internal static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "dir2site.sln")))
                return dir.FullName;

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>
    /// Every file matching <paramref name="pattern"/> that a rule about this repository's own
    /// source should be asked of. Walked rather than listed, so a file added later is covered
    /// without anyone remembering to add it.
    /// </summary>
    internal static IEnumerable<string> Sources(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !IsExcluded(Segments(root, f)));

    /// <summary>The path relative to the repository, split into its parts.</summary>
    internal static string[] Segments(string root, string file) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsExcluded(string[] parts) =>
        parts.Contains("obj")
        || parts.Contains("bin")
        // Generated output, and the demo sites written into a project folder — copies of the
        // templates, so a hit there is the template's, reported twice.
        || parts.Contains("_site")
        || parts.Contains("node_modules")
        // Nothing hidden, which is what keeps this out of the other worktrees under .claude/.
        || parts.Any(p => p.StartsWith('.'));
}

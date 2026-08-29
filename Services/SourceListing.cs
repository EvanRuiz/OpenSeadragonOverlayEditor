// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace dir2site.Services;

/// <summary>
/// Reads the source project folder, with a seam that lets a test make one listing fail the way an
/// unreadable folder does.
/// </summary>
/// <remarks>
/// The generator treats a folder it couldn't read as a gap rather than as content that has gone,
/// which is what stops it offering a live site for deletion. Testing that needs a listing to fail,
/// and permissions are the one thing Unix and Windows genuinely model differently — mode bits
/// against an access-control list — so reproducing it at the OS level means two fixtures and two
/// behaviours to keep true. Reproducing it here instead is the same code on every platform.
///
/// What that gives up is evidence that the OS really throws rather than quietly returning an empty
/// listing. That turns out to be a framework guarantee rather than a per-platform one: the
/// <see cref="SearchOption"/> overloads use <c>EnumerationOptions.Compatible</c>, which sets
/// <c>IgnoreInaccessible = false</c>, so an entry that can't be read raises
/// <see cref="UnauthorizedAccessException"/> instead of being skipped.
/// </remarks>
internal static class SourceListing
{
    // AsyncLocal rather than a plain static: xunit runs test classes in parallel and several of
    // them generate sites, so a plain static would leak a simulated failure into an unrelated run.
    private static readonly AsyncLocal<string?> _unreadable = new();

    /// <summary>Makes listings of <paramref name="path"/> fail until the returned scope is disposed.</summary>
    internal static IDisposable PretendUnreadable(string path) => new Scope(Path.GetFullPath(path));

    /// <summary>
    /// The immediate subdirectories of <paramref name="path"/>, read eagerly so a failure surfaces
    /// here rather than part-way through the caller's loop, where it would escape the caller's
    /// guard entirely.
    /// </summary>
    internal static List<string> Directories(string path)
    {
        Refuse(path);
        return [.. Directory.EnumerateDirectories(path)];
    }

    /// <summary>Every file at or below <paramref name="path"/>, read eagerly for the same reason.</summary>
    internal static List<string> FilesRecursive(string path)
    {
        Refuse(path);
        return [.. Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)];
    }

    /// <summary>Whether this directory is a symlink or a junction, rather than a directory.</summary>
    /// <remarks>
    /// Asked on the way to a delete, in both trees this app sweeps: <c>SourceLeftovers</c> before it
    /// names anything in the project, and <c>SiteGenerator</c> before it names anything in
    /// <c>_site</c>. Stepping through a link puts everything under its target in range, and the paths
    /// come back written as though they were local — so a dialog and a CI log both name
    /// <c>media/film.mp4</c> for a file that is nowhere near either folder.
    ///
    /// A lexical containment check does not stand in for this: <see cref="Path.GetFullPath"/>
    /// normalises <c>..</c> and leaves links alone, so a path through one passes a StartsWith test
    /// while pointing outside. Not stepping through the link is the check.
    ///
    /// Answers true when it cannot tell, which is the safe direction where the next step is a
    /// delete. <c>LinkTarget</c> covers Windows junctions as well as symlinks.
    /// </remarks>
    internal static bool IsLinkedDirectory(string directory)
    {
        try { return new DirectoryInfo(directory).LinkTarget != null; }
        catch { return true; }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is inside <paramref name="root"/> and stays there.
    /// </summary>
    /// <remarks>
    /// The boundary this app deletes against, asked at the delete rather than in the walk that found
    /// the path. Every walk has had to learn the same lesson separately — the project walk, the
    /// <c>_site</c> walk, the join to <c>.dir2site</c> that neither walk enters, the empty-directory
    /// tidy — and each was fixed where someone happened to look. This is the invariant those four
    /// were each approximating: no delete resolves outside the tree it was pointed at, whatever the
    /// walk that produced it does next.
    ///
    /// Lexical containment is the first half and not the whole. <see cref="Path.GetFullPath"/>
    /// normalises <c>..</c> and leaves links alone, so a path written inside the tree can still lead
    /// out of it — which is why the second half walks the chain of directories between the two and
    /// refuses if any of them is a link.
    ///
    /// The leaf is not asked about: deleting a symlink removes the link and leaves its target, so a
    /// linked leaf inside the tree is ours to take.
    /// </remarks>
    internal static bool ResolvesInside(string root, string path)
    {
        string fullRoot, full;
        try
        {
            fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            full = Path.GetFullPath(path);
        }
        catch { return false; }

        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        for (var at = Path.GetDirectoryName(full);
             at != null && at.Length > fullRoot.Length;
             at = Path.GetDirectoryName(at))
        {
            if (IsLinkedDirectory(at)) return false;
        }

        return true;
    }

    private static void Refuse(string path)
    {
        if (_unreadable.Value is not { } denied) return;
        if (!string.Equals(Path.GetFullPath(path), denied, StringComparison.OrdinalIgnoreCase)) return;

        throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;

        public Scope(string path)
        {
            _previous = _unreadable.Value;
            _unreadable.Value = path;
        }

        public void Dispose() => _unreadable.Value = _previous;
    }
}

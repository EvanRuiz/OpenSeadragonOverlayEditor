// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using dir2site.Models;
using dir2site.Services;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// Telling the generator's own leftovers from somebody else's files, and acting on the difference.
/// </summary>
/// <remarks>
/// <c>_site</c> is output. A page in it that this run does not want, and that a previous run wrote,
/// is this app's own stale work — taking it away needs no more permission than overwriting it did,
/// and asking made the app look unsure of something it knew for certain. The sweep used to ask about
/// all of it, on a rule that could only guess from the shape of a name: anything without a dot
/// segment was assumed to be ours. That guess was wrong in the one direction that matters — a
/// hand-placed <c>CNAME</c> or <c>robots.txt</c> was offered for deletion on every single run.
///
/// So the run records what it wrote, and the next one reads it. What it recognises it removes; what
/// it does not it leaves alone and offers, which is what the dialog is now for.
/// </remarks>
public class GeneratedFileSweepTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "d2s-sweep-" + Guid.NewGuid().ToString("N"));

    public GeneratedFileSweepTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string At(params string[] parts) => Path.Combine([_root, .. parts]);
    private string SitePath(params string[] parts) => Path.Combine([_root, "_site", .. parts]);

    private string MakeFolder(params string[] parts)
    {
        var path = At(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void MakePhoto(string folder, string fileName, string caption)
    {
        File.WriteAllText(Path.Combine(folder, fileName), "not really a jpeg");
        File.WriteAllText(Path.Combine(folder, fileName + ".yaml"),
            $"type: photo\ncaption: {caption}\n");
    }

    private static Dir2SiteModel Config() => new()
    {
        Title = "My Site",
        Footer = "© 2026",
        SiteUrl = "https://example.test",
    };

    private (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<string> Orphans) Generate()
    {
        var tree = DirectoryTraverser.BuildTree(_root, new List<string>(), new List<string>());
        var result = SiteGenerator.Generate(_root, tree, Config());
        return (result.Errors, result.Warnings, result.Orphans);
    }

    /// <summary>Two folders, so removing one leaves a site to be right about.</summary>
    private void MakeProject()
    {
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");
        MakePhoto(photos, "Landscape.jpg", "A Landscape");

        var documents = MakeFolder("Documents");
        MakePhoto(documents, "Letter.jpg", "A Letter");
        MakePhoto(documents, "Memo.jpg", "A Memo");
    }

    // ---- ours ---------------------------------------------------------------

    [AvaloniaFact]
    public void APageThisRunWroteAndTheNextDoesNot_GoesWithoutAsking()
    {
        MakeProject();
        Generate();
        Assert.True(File.Exists(SitePath("Photographs", "Portrait", "index.html")));

        File.Delete(At("Photographs", "Portrait.jpg"));
        File.Delete(At("Photographs", "Portrait.jpg.yaml"));

        var result = Generate();

        Assert.False(File.Exists(SitePath("Photographs", "Portrait", "index.html")));
        Assert.Empty(result.Orphans);
        Assert.Empty(result.Errors);

        // Only that page. A sweep that took the site with it would pass the check above.
        Assert.True(File.Exists(SitePath("Documents", "Letter", "index.html")));
        Assert.True(File.Exists(SitePath("index.html")));
    }

    // ---- somebody else's ----------------------------------------------------

    [AvaloniaFact]
    public void AFileNoRunEverWrote_IsOfferedRatherThanTaken()
    {
        // The case the old rule got wrong, and the reason the record exists. A CNAME is how a site
        // is pointed at a domain on some hosts: the user puts it in _site by hand, it has no dot
        // segment to hide behind, and no run ever claims it. It was offered for deletion on every
        // single generate, and the answer was always no.
        MakeProject();
        Generate();

        var cname = SitePath("CNAME");
        File.WriteAllText(cname, "example.test\n");

        var result = Generate();

        Assert.True(File.Exists(cname), "a file the generator never wrote was deleted");
        Assert.Contains("CNAME", result.Orphans);
    }

    [AvaloniaFact]
    public void ASiteGeneratedBeforeTheRecordExisted_IsOfferedRatherThanTaken()
    {
        // Upgrading into this. The first run on an existing project has no record to read, so every
        // unclaimed file is one it cannot vouch for — and not being able to vouch for something means
        // asking about it, never taking it. The run writes the record, and the run after it can act.
        // Four photographs, so that removing two in turn never leaves the folder holding one — a
        // folder down to a single item republishes as that item, which moves pages for reasons that
        // have nothing to do with this.
        var photos = MakeFolder("Photographs");
        foreach (var name in new[] { "Portrait", "Landscape", "Cover", "Street" })
            MakePhoto(photos, $"{name}.jpg", $"A {name}");
        MakePhoto(MakeFolder("Documents"), "Letter.jpg", "A Letter");

        Generate();

        File.Delete(At("Photographs", "Portrait.jpg"));
        File.Delete(At("Photographs", "Portrait.jpg.yaml"));
        File.Delete(SiteGenerator.GeneratedManifestPath(_root));

        var result = Generate();

        Assert.True(File.Exists(SitePath("Photographs", "Portrait", "index.html")));
        Assert.Contains("Photographs/Portrait/index.html", result.Orphans);

        // It stays offered, too, rather than being adopted later: the record names what a run wrote,
        // and no run ever wrote this one as far as any record goes. Only the user can settle it, and
        // once they do it is settled. Adopting whatever happened to be in _site on the first run
        // would have made migration seamless at the price of quietly taking ownership of a
        // hand-placed CNAME, and then deleting it.
        Assert.Contains("Photographs/Portrait/index.html", Generate().Orphans);

        // What the record does buy is everything from here on. Landscape is stranded after it
        // exists, so it goes without anyone being asked.
        File.Delete(At("Photographs", "Landscape.jpg"));
        File.Delete(At("Photographs", "Landscape.jpg.yaml"));

        var third = Generate();

        Assert.False(File.Exists(SitePath("Photographs", "Landscape", "index.html")));
        Assert.Equal(["Photographs/Portrait/index.html"], third.Orphans);
    }

    // ---- when it cannot be sure ---------------------------------------------

    [AvaloniaFact]
    public void ARunThatCouldNotReadTheProject_TakesNothingAndOffersNothing()
    {
        // The guard that makes silent removal safe at all. A folder that could not be listed
        // contributes nothing to the ledger, which is indistinguishable from a folder the user
        // emptied — so a run that saw less than the whole project may not act on what it thinks is
        // missing. It could not before either; the difference is that acting now means deleting.
        MakeProject();
        Generate();

        IReadOnlyList<string> orphans;
        using (SourceListing.PretendUnreadable(At("Photographs")))
        {
            var result = Generate();
            orphans = result.Orphans;
            Assert.Contains(result.Warnings, w => w.Contains("could not be read", StringComparison.Ordinal));
        }

        Assert.Empty(orphans);
        Assert.True(File.Exists(SitePath("Photographs", "Portrait", "index.html")));
        Assert.True(File.Exists(SitePath("Documents", "Letter", "index.html")));
    }

    // ---- the record itself --------------------------------------------------

    [AvaloniaFact]
    public void TheRecord_IsKeptOutOfTheSite()
    {
        // It is bookkeeping, not content. Everything in _site is published and uploaded, so a record
        // kept there would be served at the site's own address listing every file in it.
        MakeProject();
        Generate();

        Assert.True(File.Exists(SiteGenerator.GeneratedManifestPath(_root)));
        Assert.Empty(Directory.GetFiles(SitePath(), "site-manifest", SearchOption.AllDirectories));
    }

    [AvaloniaFact]
    public void TheRecord_IsNotRewrittenWhenNothingChanged()
    {
        // The deploy compares timestamps, and this one is read on every run. Rewriting it each time
        // would be a file whose mtime moves for no reason.
        MakeProject();
        Generate();

        var record = SiteGenerator.GeneratedManifestPath(_root);
        var stamp = File.GetLastWriteTimeUtc(record);

        Generate();

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(record));
    }
}

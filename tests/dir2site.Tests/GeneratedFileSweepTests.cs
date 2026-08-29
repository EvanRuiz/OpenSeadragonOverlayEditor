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

    private static void MakeVideo(string folder, string fileName, string caption)
    {
        File.WriteAllText(Path.Combine(folder, fileName),
            "[InternetShortcut]\nURL=https://www.youtube.com/watch?v=dQw4w9WgXcQ\n");
        File.WriteAllText(Path.Combine(folder, fileName + ".yaml"),
            $"type: video\ncaption: {caption}\nprovider: youtube\nvideoId: dQw4w9WgXcQ\n");
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

    // ---- when it cannot say ------------------------------------------------

    [AvaloniaFact]
    public void ATypoInOneFolder_DoesNotKeepADeletionInAnotherFromTakingEffect()
    {
        // The first fix for the test below switched the whole sweep off whenever any yaml anywhere
        // failed to parse, on the reasoning that not deleting is the safe direction. It is not — not
        // for _site, which is output. The two directions are not symmetrical: deleting too much here
        // is rewritten by the next run, while leaving too much means a page the user took down is
        // still in _site, so the next deploy keeps it at its public address. Over a typo. In a
        // different folder. With nothing to connect the two.
        MakeProject();
        Generate();

        // A take-down, deliberately made.
        File.Delete(At("Photographs", "Portrait.jpg"));
        File.Delete(At("Photographs", "Portrait.jpg.yaml"));

        // And, unrelatedly, a typo somewhere else entirely.
        File.WriteAllText(At("Documents", "Memo.jpg.yaml"), "caption: [unclosed\n");

        var result = Generate();

        Assert.False(File.Exists(SitePath("Photographs", "Portrait", "index.html")),
            "a page the user deleted stayed published because of a typo in another folder");
        Assert.Empty(result.Orphans);

        // The typo's own artifact keeps what it had, which is the other half of the rule.
        Assert.True(File.Exists(SitePath("Documents", "Memo", "index.html")));
        Assert.NotEmpty(result.Errors);
    }

    [AvaloniaFact]
    public void AFolderWhoseYamlStoppedParsing_KeepsItsPages()
    {
        // A file whose yaml does not parse is dropped from the tree, so a folder holding only that
        // file has no children and reads as one the user emptied — no page generated, nothing kept,
        // and the pages a previous run wrote are in the record, so they went without a word. The
        // artifact is sitting right there and its folder disappeared off the site.
        //
        // "We could not understand this" is the same kind of answer as "we could not read this", and
        // it belongs to the same guard: a run that did not see the whole project may not act on what
        // looks missing.
        MakeProject();
        Generate();

        File.WriteAllText(At("Photographs", "Portrait.jpg.yaml"), "caption: [unclosed\n");

        var result = Generate();

        Assert.True(File.Exists(SitePath("Photographs", "Portrait", "index.html")),
            "a page was deleted because a yaml stopped parsing");
        Assert.Empty(result.Orphans);
        Assert.NotEmpty(result.Errors);

        // The folder keeps its own page too: one file we could not read is not an empty folder.
        Assert.True(File.Exists(SitePath("Photographs", "index.html")));

        // And it comes back to itself once the yaml does parse again.
        File.WriteAllText(At("Photographs", "Portrait.jpg.yaml"), "type: photo\ncaption: A Portrait\n");
        Assert.Empty(Generate().Errors);
    }

    // A superseded file whose delete fails — a file held open by an indexer or the preview server,
    // which is ordinary on Windows — stays recorded as ours, so the next run tries again instead of
    // asking the user about a page it wrote itself. Verified once by taking write off the directory
    // and watching it: the delete is reported as an error, nothing is offered, and the path is still
    // in the record on the run after. Not kept as a test, because the only portable way to make a
    // delete fail is File.SetUnixFileMode, which throws on the Windows half of the CI matrix — and
    // this suite does not branch on which platform it is running on. See SourceWatcherTests for the
    // same call made about a race that cannot be caused without a seam.

    /// <summary>
    /// Where an artifact's pages end up, which is not one shape. A folder holding one artifact
    /// publishes as that artifact at the folder's own address, with no directory of its own — the
    /// rule <c>SoleArtifact</c> states, and the one a reconstruction from a source path will get
    /// wrong if it does not ask.
    /// </summary>
    public static TheoryData<string> Layouts() => new()
    {
        "the folder's only artifact",
        "one of several in a folder",
        "an artifact at the project root",
        "the only artifact in a marker folder",
        "an artifact beside a subfolder",
        "a sole artifact in a folder with an introduction",
        "a folder whose only artifact is a video",
    };

    [AvaloniaTheory]
    [MemberData(nameof(Layouts))]
    public void AYamlThatStoppedParsing_NeverBlanksWhatWasPublishedForIt(string layout)
    {
        // The first attempt at scoping this rebuilt the artifact's address as {folder}/{stem}/, which
        // is right for a collection and wrong for a folder holding one — there is no {stem}/ there at
        // all. Nothing was found, the miss read as "never published", nothing was claimed — and the
        // folder, now counted as non-empty because of the unreadable file, rendered an empty
        // collection page straight over the photograph's own page. A typo turned a published
        // photograph into a blank page.
        //
        // Which layout the artifact was in should not come into it, so this asks all of them.
        var (yaml, page) = MakeLayout(layout);

        Generate();
        Assert.Contains("The Subject", File.ReadAllText(page));

        File.WriteAllText(yaml, "type: photo\ncaption: [unclosed\n");
        var result = Generate();

        Assert.True(File.Exists(page), $"the published page went, for {layout}");
        Assert.Contains("The Subject", File.ReadAllText(page));
        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Orphans);
    }

    /// <summary>Builds one of the layouts, and says which yaml to break and which page must survive.</summary>
    private (string Yaml, string Page) MakeLayout(string layout)
    {
        // A second folder throughout, so the project is never reduced to the folder under test.
        MakePhoto(MakeFolder("Elsewhere"), "Letter.jpg", "A Letter");
        MakePhoto(At("Elsewhere"), "Memo.jpg", "A Memo");

        switch (layout)
        {
            case "one of several in a folder":
                MakePhoto(MakeFolder("Photographs"), "Portrait.jpg", "The Subject");
                MakePhoto(At("Photographs"), "Landscape.jpg", "A Landscape");
                return (At("Photographs", "Portrait.jpg.yaml"), SitePath("Photographs", "Portrait", "index.html"));

            case "an artifact at the project root":
                MakePhoto(_root, "Cover.jpg", "The Subject");
                return (At("Cover.jpg.yaml"), SitePath("Cover", "index.html"));

            case "the only artifact in a marker folder":
                MakePhoto(MakeFolder("-About"), "Team.jpg", "The Subject");
                return (At("-About", "Team.jpg.yaml"), SitePath("About", "index.html"));

            case "an artifact beside a subfolder":
                MakePhoto(MakeFolder("Photographs"), "Portrait.jpg", "The Subject");
                MakePhoto(MakeFolder("Photographs", "1890s"), "Old.jpg", "An Old One");
                MakePhoto(At("Photographs", "1890s"), "Older.jpg", "An Older One");
                return (At("Photographs", "Portrait.jpg.yaml"), SitePath("Photographs", "Portrait", "index.html"));

            case "a sole artifact in a folder with an introduction":
                // An introduction pulls the two rules apart: it stops SoleArtifact collapsing the
                // folder, so the artifact keeps a page of its own, while also making the folder
                // renderable — so it is drawn, and the {stem} address is the one to protect.
                MakePhoto(MakeFolder("Photographs"), "Portrait.jpg", "The Subject");
                File.WriteAllText(At("Photographs", "index.md"), "# What this collection is\n");
                return (At("Photographs", "Portrait.jpg.yaml"), SitePath("Photographs", "Portrait", "index.html"));

            case "a folder whose only artifact is a video":
                // A video plays on its folder's page and never gets one of its own, so SoleArtifact
                // declines to collapse and the folder publishes as a collection carrying the card.
                MakeVideo(MakeFolder("Clips"), "Talk.url", "The Subject");
                return (At("Clips", "Talk.url.yaml"), SitePath("Clips", "index.html"));

            default:
                // A folder holding one artifact publishes as that artifact, at the folder's address.
                MakePhoto(MakeFolder("Photographs"), "Portrait.jpg", "The Subject");
                return (At("Photographs", "Portrait.jpg.yaml"), SitePath("Photographs", "index.html"));
        }
    }

    [AvaloniaFact]
    public void AFolderWhoseOnlyArtifactStoppedParsing_IsNotRenderedOverEmpty()
    {
        // The other half of the case above, stated on its own because it is the mechanism rather
        // than the symptom: a folder with nothing left to draw is not a folder to draw empty.
        MakePhoto(MakeFolder("Photographs"), "Portrait.jpg", "The Subject");
        MakePhoto(MakeFolder("Elsewhere"), "Letter.jpg", "A Letter");
        MakePhoto(At("Elsewhere"), "Memo.jpg", "A Memo");
        Generate();

        var page = SitePath("Photographs", "index.html");
        var before = File.ReadAllBytes(page);

        File.WriteAllText(At("Photographs", "Portrait.jpg.yaml"), "type: photo\ncaption: [unclosed\n");
        Generate();

        Assert.Equal(before, File.ReadAllBytes(page));
    }

    [SkippableFact]
    public void TheSiteListingDoesNotStepThroughASymlink()
    {
        // Linking a media folder into the published site rather than copying gigabytes into it is an
        // ordinary thing to do, and everything behind that link is the user's. The recursive
        // enumeration followed it, so their masters came back as files with no source and were listed
        // as exactly that — the sentence a dialog uses to get a yes, and --force-clean is a yes
        // already given. RemoveOrphans' containment check does not catch it either: a path through a
        // link resolves to something inside the site while pointing elsewhere.
        //
        // Asked of the listing rather than through a generate, because the assertion is about the
        // walk and a test that needs a symlink cannot also be an AvaloniaFact — the two attributes
        // do not combine, and a test that quietly passes where links cannot be made is worse than
        // one that says so.
        var site = Directory.CreateDirectory(SitePath()).FullName;
        File.WriteAllText(Path.Combine(site, "index.html"), "ours");

        var theirs = Directory.CreateDirectory(Path.Combine(_root, "..",
            "masters-" + Path.GetFileName(_root))).FullName;

        try
        {
            File.WriteAllText(Path.Combine(theirs, "film.mp4"), "the user's master");

            try { Directory.CreateSymbolicLink(Path.Combine(site, "media"), theirs); }
            catch (Exception ex)
            {
                // Windows needs a privilege for this that a build agent may not have. Where links
                // cannot be made the case cannot arise either, so saying why beats a false green.
                Skip.If(true, $"this system would not make a symlink: {ex.Message}");
            }

            var listed = SiteGenerator.FilesInTheSite(site);

            Assert.DoesNotContain(listed, f => f.Contains("film.mp4", StringComparison.Ordinal));
            Assert.Contains(listed, f => f.EndsWith("index.html", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(Path.Combine(site, "media")); } catch { }
            try { Directory.Delete(theirs, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public void TheEmptyDirectoryTidyDoesNotStepThroughASymlink()
    {
        // The naming walk got this rule; the tidy walk that runs at the end of every RemoveOrphans
        // did not. It recurses through a linked directory and rmdirs empty folders at the target —
        // only empty ones, so nothing of theirs is lost, but it is still a write outside the site in
        // exactly the linked-media case the naming fix was about.
        var site = Directory.CreateDirectory(SitePath()).FullName;
        var theirs = Directory.CreateDirectory(Path.Combine(_root, "..",
            "media-" + Path.GetFileName(_root))).FullName;

        try
        {
            var theirEmpty = Directory.CreateDirectory(Path.Combine(theirs, "empty-sub")).FullName;

            try { Directory.CreateSymbolicLink(Path.Combine(site, "linked"), theirs); }
            catch (Exception ex)
            {
                Skip.If(true, $"this system would not make a symlink: {ex.Message}");
            }

            // And an empty directory of ours, to show the tidy still does its job.
            var ours = Directory.CreateDirectory(Path.Combine(site, "ours-empty")).FullName;

            SiteGenerator.RemoveEmptyDirectories(site, site);

            Assert.True(Directory.Exists(theirEmpty), "an empty directory outside the site was removed");
            Assert.False(Directory.Exists(ours), "the tidy stopped removing our own empty directories");
        }
        finally
        {
            try { Directory.Delete(Path.Combine(site, "linked")); } catch { }
            try { Directory.Delete(theirs, recursive: true); } catch { }
        }
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

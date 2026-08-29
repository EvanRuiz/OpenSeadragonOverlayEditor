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
/// A folder with nothing to publish, and everywhere it used to turn up anyway.
/// </summary>
/// <remarks>
/// Emptying a folder in Finder does not remove the folder: the hidden <c>.dir2site</c> beside its
/// contents stays behind, so the directory is still there with nothing in it the site can show. The
/// walk adds every directory it finds whether or not anything is left inside, and nothing further
/// down asked — so the folder kept a card reading "0 items", a place in the menu, a promotion to the
/// home page if its name ended in '+', and a page of its own that the ledger claimed, which put it
/// beyond the reach of the sweep that would otherwise have offered it up.
///
/// All of which is a question about what gets <em>published</em>, and is answered entirely on the
/// site side. That the folder still sits in the project afterwards is the user's business and not
/// this app's — see <c>NothingButOurOwnIsOfferedTests</c> for the boundary, and for what happened
/// when this fix went looking for the folder as well as the pages.
///
/// The awkward case is the folder that holds only an <c>index.md</c>. It reports zero children by
/// design — an introduction is prose about the folder rather than one of its contents — and it is
/// emphatically not empty. So the question is never "how many children" but "is there anything to
/// publish", and an introduction counts.
/// </remarks>
public class EmptyFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "d2s-empty-" + Guid.NewGuid().ToString("N"));

    public EmptyFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string At(params string[] parts) => Path.Combine([_root, .. parts]);
    private string SitePath(params string[] parts) => Path.Combine([_root, "_site", .. parts]);
    private string ReadPage(params string[] parts) =>
        File.ReadAllText(Path.Combine([_root, "_site", .. parts, "index.html"]));

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

    private static void MakeIntro(string folder, string text) =>
        File.WriteAllText(Path.Combine(folder, "index.md"), text);

    /// <summary>Empties a folder the way Finder does: the contents go, the hidden folder stays.</summary>
    private static void EmptyInFinder(string folder)
    {
        foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
        Assert.True(Directory.Exists(Path.Combine(folder, ".dir2site")),
            "this folder never had a previews folder, so it is not the case being tested");
    }

    private IReadOnlyList<string> Generate()
    {
        var tree = DirectoryTraverser.BuildTree(_root, new List<string>(), new List<string>());
        DirectoryTraverser.GeneratePreviews(tree, Config(), null);
        var result = SiteGenerator.Generate(_root, tree, Config());
        Assert.Empty(result.Errors);
        return result.Orphans;
    }

    private static Dir2SiteModel Config() => new()
    {
        Title = "My Site",
        Footer = "© 2026",
        SiteUrl = "https://example.test",
    };

    // ---- the ghost ----------------------------------------------------------

    [AvaloniaFact]
    public void AnEmptiedFolder_IsNotACardOnThePageAbove()
    {
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");
        var archive = MakeFolder("Archive");
        MakePhoto(archive, "Letter.jpg", "A Letter");

        Generate();
        Assert.Contains("Archive/", ReadPage());

        EmptyInFinder(archive);
        Generate();

        Assert.DoesNotContain("0 items", ReadPage());
        Assert.DoesNotContain("Archive/", ReadPage());
    }

    [AvaloniaFact]
    public void AnEmptiedFolder_IsNotInTheMenuEither()
    {
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");
        var archive = MakeFolder("Archive");
        MakePhoto(archive, "Letter.jpg", "A Letter");

        Generate();
        EmptyInFinder(archive);
        Generate();

        // The menu is on every page, so a ghost there follows the reader around the whole site.
        Assert.DoesNotContain("Archive/", ReadPage("Photographs"));
    }

    [AvaloniaFact]
    public void AnEmptiedFolder_GetsNoPageOfItsOwn()
    {
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");
        var archive = MakeFolder("Archive");
        MakePhoto(archive, "Letter.jpg", "A Letter");

        Generate();
        Assert.True(File.Exists(SitePath("Archive", "index.html")));

        EmptyInFinder(archive);
        var orphans = Generate();

        // Gone, not offered. While the ledger claimed this page it could not even reach the sweep,
        // so an empty collection page sat in the site for good — reachable by anyone holding the
        // address long after the card had gone. It is the generator's own output and the generator
        // no longer wants it, so nobody is asked about it.
        Assert.False(File.Exists(SitePath("Archive", "index.html")));
        Assert.Empty(orphans);
    }

    [AvaloniaFact]
    public void AnEmptiedPromotedFolder_IsNotPromotedToTheHomePage()
    {
        // The reported case. A '+' folder is promoted on its name alone, and nothing in that
        // decision ever looked at whether the folder still had anything in it — so emptying one put
        // a "0 items" card on the front page of the site.
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");
        var promoted = MakeFolder("Photographs", "Newspapers+");
        MakePhoto(promoted, "Gazette.jpg", "The Gazette");

        Generate();
        Assert.Contains("Newspapers", ReadPage());

        EmptyInFinder(promoted);
        Generate();

        Assert.DoesNotContain("0 items", ReadPage());
        Assert.DoesNotContain("Newspapers", ReadPage());
    }

    [AvaloniaFact]
    public void AnEmptiedFolderDeeperIn_IsNotACardOnItsOwnParentsPage()
    {
        // Every other case here empties a folder at the top level, where "the page above" is the
        // home page. A collection page is built by the same code, but it is the page a reader is
        // actually on when browsing a decade, and it is where a ghost would be least conspicuous.
        var photos = MakeFolder("Photographs");
        var decade = MakeFolder("Photographs", "1890s");
        MakePhoto(decade, "Portrait.jpg", "A Portrait");
        var empty = MakeFolder("Photographs", "1900s");
        MakePhoto(empty, "Street.jpg", "A Street");
        _ = photos;

        Generate();
        Assert.Contains("1900s/", ReadPage("Photographs"));
        Assert.Contains("2 items", ReadPage());

        EmptyInFinder(empty);
        Generate();

        Assert.DoesNotContain("0 items", ReadPage("Photographs"));
        Assert.DoesNotContain("1900s", ReadPage("Photographs"));

        // The decade that still has something is untouched, and its parent's count follows.
        Assert.Contains("1890s/", ReadPage("Photographs"));
        Assert.Contains("1 item", ReadPage());
    }

    // ---- what must not disappear with it ------------------------------------

    [AvaloniaFact]
    public void AFolderHoldingOnlyAnIntroduction_KeepsItsCardAndSaysNoCount()
    {
        // Not empty: it renders a page of prose, which is the whole point of an introduction. But
        // the introduction is deliberately not one of the folder's children, so the count that is
        // meant to say how many cards the page shows says nought — and nought is right, and useless.
        var about = MakeFolder("About");
        MakeIntro(about, "What this collection is.\n");
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");

        Generate();

        Assert.Contains("About/", ReadPage());
        Assert.DoesNotContain("0 items", ReadPage());
        Assert.Contains("What this collection is.", ReadPage("About"));

        // And the glyph standing in for the missing cover picture does not become an unnamed image
        // on the way. It took its accessible name from the badge, and this is the first card that
        // has no badge — role="img" with an empty name is announced as an image nobody labelled.
        Assert.DoesNotContain("aria-label=\"\"", ReadPage());
    }

    [AvaloniaFact]
    public void AFolderWhoseContentIsAllDeeper_Stays()
    {
        // Its own children are folders rather than artifacts, which is an ordinary way to organise a
        // site and must not read as empty.
        var photographs = MakeFolder("Photographs");
        var decade = MakeFolder("Photographs", "1890s");
        MakePhoto(decade, "Portrait.jpg", "A Portrait");
        MakePhoto(decade, "Landscape.jpg", "A Landscape");
        _ = photographs;

        Generate();

        // Photographs holds one thing — the decade folder — and that decade holds two photos.
        Assert.Contains("Photographs/", ReadPage());
        Assert.Contains("1 item", ReadPage());
        Assert.Contains("2 items", ReadPage("Photographs"));
    }

    [AvaloniaFact]
    public void AFolderWithOneArtifact_StillSaysOneItem()
    {
        // The guard against over-hiding: the fix is about nought, and must leave every other count
        // exactly as it was.
        var photos = MakeFolder("Photographs");
        MakePhoto(photos, "Portrait.jpg", "A Portrait");
        var archive = MakeFolder("Archive");
        MakePhoto(archive, "Letter.jpg", "A Letter");
        MakePhoto(archive, "Memo.jpg", "A Memo");

        Generate();

        Assert.Contains("2 items", ReadPage());
    }
}

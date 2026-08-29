// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using dir2site.Models;
using dir2site.Services;
using dir2site.ViewModels;
using ImageMagick;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// Replacing a document with a different one under the same name, and the page images the reader
/// has to be showing afterwards.
/// </summary>
/// <remarks>
/// A PDF is not one derived file but a set: two thumbnails, a BookReader manifest, and a page image
/// per page. Every one of them has to follow the source, and the ways they can fail to are
/// different enough that each gets its own case here — which document was on disk, whether anything
/// was watching when it changed, and whether the previous render finished.
///
/// The documents are real, built with the same library the app reads them with, for the reason
/// <see cref="StaleDerivedFileTests"/> gives about JPEGs: what is being tested is that page images
/// are re-derived, so something has to derive them. Each page is one flat colour, so a published
/// page can be asked which document it came from without decoding anything the app wrote.
/// </remarks>
public class ReplacedPdfTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "d2s-repdf-" + Guid.NewGuid().ToString("N"));

    public ReplacedPdfTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string At(params string[] parts) => Path.Combine([_root, .. parts]);
    private string SitePath(params string[] parts) => Path.Combine([_root, "_site", .. parts]);

    // Resizing off and a generous width, so a page keeps the embedded JPEG it was built from rather
    // than being re-encoded. The colour then survives the round trip intact and the assertions are
    // about the document, not about the encoder.
    private static Dir2SiteModel Config() => new()
    {
        Title = "My Site",
        Footer = "© 2026",
        PdfResizeEnabled = false,
    };

    /// <summary>
    /// A real PDF of <paramref name="pages"/> flat-colour pages, written with the library the app
    /// itself uses to read them.
    /// </summary>
    private static void MakePdf(string path, MagickColor colour, int pages = 1)
    {
        using var image = new MagickImage(colour, 400, 300);
        image.Format = MagickFormat.Jpeg;
        var jpeg = image.ToByteArray();

        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
            builder.AddPage(400, 300).AddJpeg(jpeg, new PdfRectangle(0, 0, 400, 300));

        File.WriteAllBytes(path, builder.Build());
    }

    /// <summary>
    /// A real PDF whose pages carry no embedded image — the shape most documents actually are.
    /// </summary>
    /// <remarks>
    /// Worth having separately from <see cref="MakePdf"/> because the two take different routes
    /// through the generator, and the one the colour fixture takes is the friendlier of them.
    /// A page built from <c>AddJpeg</c> is extracted by <c>TryGetOriginalJpeg</c>, which reports the
    /// page's dimensions on its way past; a vector page is rendered instead, and every path that
    /// measures it sits inside the render. So a test built only on embedded JPEGs cannot see a bug
    /// in what happens when the render is skipped.
    /// </remarks>
    private static void MakeVectorPdf(string path, int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++) builder.AddPage(400, 300);
        File.WriteAllBytes(path, builder.Build());
    }

    /// <summary>What the BookReader manifest says each page measures, in order.</summary>
    private static (int Width, int Height)[] ManifestPageSizes(string manifest)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        return [.. json.RootElement.GetProperty("data").EnumerateArray()
            .SelectMany(spread => spread.EnumerateArray())
            .Select(page => (page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32()))];
    }

    private static string MakeFolder(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private DirectoryTreeItem Scan() =>
        DirectoryTraverser.BuildTree(_root, new List<string>(), new List<string>());

    /// <summary>
    /// One generate, in the order the app runs one: scan the project, make the previews the scan
    /// found wanting, then write the site from that same tree.
    /// </summary>
    private IReadOnlyList<string> Generate(RenderScope? scope = null)
    {
        var tree = Scan();
        DirectoryTraverser.GeneratePreviews(tree, Config(), null);
        return SiteGenerator.Generate(_root, tree, Config(), null, scope).Orphans;
    }

    /// <summary>A scope built the way the app builds one, from the freshly scanned tree.</summary>
    private RenderScope Scope(params SourceChange[] changes) =>
        RenderScope.For(_root, changes, Scan());

    /// <summary>
    /// Which document a rendered page came from, judged by its dominant channel. JPEG is lossy, so
    /// the flat colour comes back near enough rather than exact — which channel won is the question
    /// that survives that.
    /// </summary>
    private static string ColourOf(string file)
    {
        using var image = new MagickImage(file);
        var pixel = image.GetPixels().GetPixel((int)image.Width / 2, (int)image.Height / 2).ToColor()!;
        return pixel.R > pixel.B ? "red" : "blue";
    }

    /// <summary>The page images the site is publishing for a document, in page order.</summary>
    private string[] PublishedPages(params string[] folders)
    {
        var dir = SitePath([.. folders, "Report_pages"]);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal)];
    }

    /// <summary>Which document the site is showing on page one.</summary>
    private string PublishedColour(params string[] folders)
    {
        var pages = PublishedPages(folders);
        Assert.True(pages.Length > 0, "the site is publishing no page images at all");
        return ColourOf(pages[0]);
    }

    private string StemDir(params string[] parts) =>
        Path.Combine([_root, .. parts, ".dir2site", "Report"]);

    // ---- 1. the app was closed when it happened ----------------------------

    [AvaloniaFact]
    public void ADocumentReplacedWhileNothingWatched_IsTheOneThatGetsPublished()
    {
        // Nothing saw the delete, so the yaml and the whole .dir2site/Report folder are still
        // sitting there when a different document arrives under the same name. The previews they
        // hold are of a file that no longer exists.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red);
        Generate();
        Assert.Equal("red", PublishedColour("Documents", "Report"));

        File.Delete(pdf);
        MakePdf(pdf, MagickColors.Blue);
        Generate();

        Assert.Equal("blue", PublishedColour("Documents", "Report"));
    }

    // ---- 2. the watcher saw it ---------------------------------------------

    [AvaloniaFact]
    public void ADocumentReplacedWithTheAppRunning_IsTheOneThatGetsPublished()
    {
        // The witnessed path, driven the way the view model drives it: the deletion takes its
        // yaml and previews with it, and _site is brought into line before the generate.
        // Deliberately not through a real FileSystemWatcher — what is under test is what the app
        // does with a classified change, not which events this machine's backend happens to emit.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red);
        Generate();

        var removal = new SourceChange(SourceChangeKind.Removed, pdf);
        File.Delete(pdf);
        SourceLeftovers.RemoveFor(pdf);
        SiteChangeApplier.Apply(_root, [removal]);

        MakePdf(pdf, MagickColors.Blue);
        var arrival = new SourceChange(SourceChangeKind.Updated, pdf);
        Generate(Scope(removal, arrival));

        Assert.Equal("blue", PublishedColour("Documents", "Report"));
    }

    // ---- 3. gone and back inside one debounce window -----------------------

    [AvaloniaFact]
    public void ADocumentGoneAndBackInOneBurst_IsTheOneThatGetsPublished()
    {
        // The watcher settles a burst before classifying it, and it settles against the disk: the
        // path is there when the dust clears, so this reads as one update rather than as a delete
        // and an add. Nothing is taken away, which means the previews of the previous document
        // survive — this is the case where the witnessed cleanup does not run.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red);
        Generate();

        File.Delete(pdf);
        MakePdf(pdf, MagickColors.Blue);

        var batch = SourceChangeCoalescer.Coalesce(
            [new RawSourceEvent(RawChangeKind.Deleted, pdf), new RawSourceEvent(RawChangeKind.Created, pdf)]);

        var change = Assert.Single(batch.Changes);
        Assert.Equal(SourceChangeKind.Updated, change.Kind);

        SiteChangeApplier.Apply(_root, batch.Changes);
        Generate(Scope([.. batch.Changes]));

        Assert.Equal("blue", PublishedColour("Documents", "Report"));
    }

    // ---- 4. the replacement is shorter -------------------------------------

    [AvaloniaFact]
    public void AShorterReplacement_LeavesNoPagesOfTheOldDocumentBehind()
    {
        // Page images are written one per page and nothing removed the ones a shorter document has
        // no use for. Worse than clutter: they were copied into _site wholesale and registered by
        // the ledger as wanted, so the sweep could not offer them either — published and uploaded,
        // belonging to a document that is gone, and no way to notice.
        //
        // Taking them out of _site is still the sweep's job. It can only do it now that they reach
        // it at all, and it does it without asking, because they are pages this generator wrote and
        // this generator no longer has a document with that many pages.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red, pages: 3);
        Generate();
        Assert.Equal(3, PublishedPages("Documents", "Report").Length);

        File.Delete(pdf);
        MakePdf(pdf, MagickColors.Blue, pages: 1);
        var orphans = Generate();

        Assert.Empty(orphans);
        Assert.Equal("blue", ColourOf(Assert.Single(PublishedPages("Documents", "Report"))));

        // And the source folder no longer holds them either, which is where the copies came from.
        Assert.Single(Directory.GetFiles(Path.Combine(StemDir("Documents"), "Report_pages")));
    }

    // ---- 5. the previous render did not finish -----------------------------

    [AvaloniaFact]
    public void ARenderThatDidNotFinish_IsTriedAgainOnTheNextRun()
    {
        // The thumbnails are written from page one, inside the page loop; the manifest is written
        // only after the last page. So a run that was cancelled, or that threw on some later page,
        // leaves thumbnails present and no manifest — and the survey that decides whether there is
        // work to do looks at the thumbnails alone. Left as it is the artifact declares itself
        // finished on every run from here on, and the reader never gets its manifest.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red, pages: 2);
        Generate();

        var manifest = Path.Combine(StemDir("Documents"), "Report.bookreader.json");
        Assert.True(File.Exists(manifest));
        File.Delete(manifest);

        Generate();

        Assert.True(File.Exists(manifest), "the manifest was never rebuilt");
    }

    [AvaloniaFact]
    public void AManifestRebuiltOverIntactPages_StillSaysHowBigTheyAre()
    {
        // The repair above, done to the document most documents are. The page images are already
        // there and current, so the render is skipped — and every line that works out a page's size
        // is inside the render. The reader lays out from the manifest, so a rebuild that recorded
        // nothing published a book of zero-sized pages, and then never came back to it: a manifest
        // that exists and is current is a document PdfOutputIsCurrent has no further questions about.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakeVectorPdf(pdf, pages: 2);
        Generate();

        var manifest = Path.Combine(StemDir("Documents"), "Report.bookreader.json");
        var rendered = ManifestPageSizes(manifest);
        Assert.All(rendered, size => Assert.True(size.Width > 0 && size.Height > 0,
            "the first render recorded no page size, so this test proves nothing"));

        File.Delete(manifest);
        Generate();

        Assert.Equal(rendered, ManifestPageSizes(manifest));
    }

    // ---- 6. same name, different folder, one burst --------------------------

    [AvaloniaFact]
    public void ADifferentDocumentArrivingAsAnotherLeaves_DoesNotInheritItsPages()
    {
        // A departure and an arrival under one name in a single burst read as a move, and a move
        // carries the yaml and the whole previews folder to the new path. When the two files are
        // genuinely different documents that transplants one document's page images onto another.
        var from = MakeFolder(At("Archive"));
        var to = MakeFolder(At("Documents"));
        var oldPdf = Path.Combine(from, "Report.pdf");
        var newPdf = Path.Combine(to, "Report.pdf");

        MakePdf(oldPdf, MagickColors.Red);
        Generate();

        File.Delete(oldPdf);
        MakePdf(newPdf, MagickColors.Blue);

        var batch = SourceChangeCoalescer.Coalesce(
            [new RawSourceEvent(RawChangeKind.Deleted, oldPdf), new RawSourceEvent(RawChangeKind.Created, newPdf)]);

        foreach (var change in batch.Changes)
            if (change is { Kind: SourceChangeKind.Moved, From: { } source })
                ArtifactRename.Apply(source, change.Path);

        SiteChangeApplier.Apply(_root, batch.Changes);
        Generate(Scope([.. batch.Changes]));

        Assert.Equal("blue", PublishedColour("Documents", "Report"));
    }

    // ---- 8. the settings were taken away but the previews were not ----------

    [AvaloniaFact]
    public void ADocumentWhoseSettingsWereTakenAway_DoesNotInheritTheOldPreviews()
    {
        // The leftovers dialog offers the yaml and the previews folder as two separate items, and
        // RemoveFor's delete of the folder is allowed to fail — so "yaml gone, previews still
        // there" is an ordinary state, and it is also what deleting Report.pdf.yaml by hand to reset
        // a caption produces. The scan then scaffolds a fresh yaml: nothing on disk describes this
        // file, which is as good as saying the app has never seen it. Previews sitting beside it
        // describe something else.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red);
        Generate();

        File.Delete(pdf + ".yaml");
        // As a project generated before stamps existed looks: previews, and nothing recording what
        // they were made from.
        File.Delete(PreviewGenerator.StampPath(pdf));

        MakePdf(pdf, MagickColors.Blue);
        File.SetLastWriteTimeUtc(pdf, DateTime.UtcNow.AddHours(-1));
        Generate();

        Assert.Equal("blue", PublishedColour("Documents", "Report"));
    }

    [AvaloniaFact]
    public void ASettledDocument_IsNotReRenderedOnEveryRun()
    {
        // The other half of the rule above, and the reason it is safe. Scaffolding happens during
        // the scan, so the yaml is on disk before the previews stage runs and the next run finds
        // it: the flag fires once, for the run that created it, and never again. Were that not so,
        // every document in the project would be re-rendered on every single generate.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red, pages: 2);
        Generate();

        var pages = Directory.GetFiles(Path.Combine(StemDir("Documents"), "Report_pages"))
            .ToDictionary(p => p, File.GetLastWriteTimeUtc);

        Generate();

        foreach (var (page, stamp) in pages)
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(page));
    }

    [AvaloniaFact]
    public void DeletingTheSettingsFile_RebuildsEvenAnUnchangedDocument()
    {
        // The escape hatch, and the reason it is worth having. Everything else here infers — from a
        // timestamp, from a recorded length — and an inference that goes wrong leaves the user
        // looking at a document they know is out of date with no way to say so. Deleting the yaml
        // is that way: it means build this again whatever you think you know, and nothing gets to
        // overrule it, not even a stamp that agrees the document has not changed.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red, pages: 2);
        Generate();

        var pagesDir = Path.Combine(StemDir("Documents"), "Report_pages");
        var before = Directory.GetFiles(pagesDir).ToDictionary(p => p, File.GetLastWriteTimeUtc);
        Assert.NotEmpty(before);

        File.Delete(pdf + ".yaml");

        // A scan first, which is what pressing Rescan to watch the tree update does. It parses the
        // file, finds no yaml and writes one — so by the time Generate scans again there is a yaml
        // and nothing to notice. The instruction has to outlast that, and it does by being carried
        // out rather than remembered.
        _ = Scan();
        Generate();

        foreach (var (page, stamp) in before)
            Assert.True(File.GetLastWriteTimeUtc(page) > stamp, $"{Path.GetFileName(page)} was not rebuilt");
    }

    // ---- 7. the replacement arrives with an older timestamp -----------------

    [AvaloniaFact]
    public void AReplacementWithAnOlderTimestamp_IsStillTheOneThatGetsPublished()
    {
        // Put Back from the Trash, cp -p, unzip, rsync -t and a restore from backup all land a file
        // with the timestamp it had before it travelled. Staleness measured as "is the preview older
        // than the source" says the preview is fine, and the previous document is published for good.
        var docs = MakeFolder(At("Documents"));
        var pdf = Path.Combine(docs, "Report.pdf");

        MakePdf(pdf, MagickColors.Red);
        Generate();

        File.Delete(pdf);
        MakePdf(pdf, MagickColors.Blue);
        File.SetLastWriteTimeUtc(pdf, DateTime.UtcNow.AddHours(-1));
        Generate();

        Assert.Equal("blue", PublishedColour("Documents", "Report"));
    }
}

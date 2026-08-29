// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using dir2site.Models;
using dir2site.Services;
using ImageMagick;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// Replacing a file in place, and the derived copies that have to follow it.
/// </summary>
/// <remarks>
/// Dropping a corrected scan over the old one under the same name is the ordinary way to replace a
/// photo, and it is the case "does the thumbnail exist" gets wrong. The survey already asks the
/// right question — it enqueues the artifact — so a generator that asked a narrower one took the
/// work and declined it, on every run, for as long as the project existed.
/// </remarks>
public class StaleDerivedFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "d2s-stale-" + Guid.NewGuid().ToString("N"));

    public StaleDerivedFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A scan and a previews pass, the way a generate runs them.
    /// </summary>
    /// <remarks>
    /// Through <c>BuildTree</c> rather than by calling a generator, and that distinction is the
    /// point rather than tidiness. The survey in <c>CollectPreviewJobs</c> decides which artifacts
    /// have work to do; a generator called directly runs whether or not the survey would ever have
    /// reached it. A test that reaches past the survey proved the generator's own staleness check
    /// consults the stamp while nothing in the app consulted the generator — the thumbnail on the
    /// published site stayed as it was, and the test was green beside it.
    /// </remarks>
    private void Scan()
    {
        var tree = DirectoryTraverser.BuildTree(_root, new List<string>(), new List<string>());
        DirectoryTraverser.GeneratePreviews(tree, new Dir2SiteModel { Title = "S", Footer = "f" }, null);
    }

    /// <summary>
    /// A real JPEG of one flat colour, written with the library the app itself uses.
    /// </summary>
    /// <remarks>
    /// Through Magick.NET rather than the <c>magick</c> command, which is what this asked for first
    /// and is the reason it passed here and failed on CI: the CLI is not part of the build, nothing
    /// declares it, and windows-latest has no reason to have it. The app has never needed it either
    /// — <c>PreviewGenerator</c> works in-process, and the package reaches the tests through it.
    ///
    /// It has to be a real image, not bytes pretending to be one: what these tests are about is the
    /// thumbnail and the web copy being re-derived, which means something has to derive them.
    /// </remarks>
    private static void MakeJpeg(string path, MagickColor colour)
    {
        using var image = new MagickImage(colour, 400, 300);
        image.Write(path);
    }

    [AvaloniaFact]
    public void ReplacingAPhotoRemakesItsThumbnailAndItsWebCopy()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "Photographs")).FullName;
        var jpeg = Path.Combine(photos, "Portrait.jpg");
        MakeJpeg(jpeg, MagickColors.Red);

        Assert.NotNull(PreviewGenerator.GeneratePreviews(jpeg, _root));

        var preview = Path.Combine(photos, ".dir2site", "Portrait", "preview-Portrait.webp");
        var large   = Path.Combine(photos, ".dir2site", "Portrait", "preview-lg-Portrait.webp");
        // The one the viewer actually shows, so getting this wrong publishes the wrong picture
        // rather than merely a wrong thumbnail.
        var webCopy = Path.Combine(photos, ".dir2site", "Portrait", "Portrait_q90.webp");

        var before = new[] { preview, large, webCopy }.Select(File.ReadAllBytes).ToList();

        // A whole second, because the check is a timestamp comparison and a filesystem that stores
        // them to the second would otherwise call the new file the same age as the old one.
        Thread.Sleep(1100);
        MakeJpeg(jpeg, MagickColors.Blue);

        PreviewGenerator.GeneratePreviews(jpeg, _root);

        var after = new[] { preview, large, webCopy }.Select(File.ReadAllBytes).ToList();

        Assert.False(after[0].SequenceEqual(before[0]), "the thumbnail is still of the old photo");
        Assert.False(after[1].SequenceEqual(before[1]), "the large thumbnail is still of the old photo");
        Assert.False(after[2].SequenceEqual(before[2]), "the published web copy is still the old photo");
    }

    [AvaloniaFact]
    public void APhotoReplacedWithAnOlderTimestamp_IsStillRemade()
    {
        // The half a timestamp comparison cannot see. "Is the thumbnail older than the photo" only
        // notices a source moving forward, and a file very often arrives carrying the timestamp it
        // had before it travelled — Put Back from the Trash, cp -p, rsync -t, unzip, a restore from
        // backup, a sync down from cloud storage. Replacing a photo that way left the previous one
        // published for good: the thumbnail really was newer than its source, and the rule had no
        // way to mind.
        var photos = Directory.CreateDirectory(Path.Combine(_root, "Photographs")).FullName;
        var jpeg = Path.Combine(photos, "Portrait.jpg");
        MakeJpeg(jpeg, MagickColors.Red);

        Scan();

        var webCopy = Path.Combine(photos, ".dir2site", "Portrait", "Portrait_q90.webp");
        var before = File.ReadAllBytes(webCopy);

        MakeJpeg(jpeg, MagickColors.Blue);
        File.SetLastWriteTimeUtc(jpeg, DateTime.UtcNow.AddHours(-1));

        Scan();

        Assert.False(File.ReadAllBytes(webCopy).SequenceEqual(before),
            "the published copy is still the photo that used to be there");
    }

    [AvaloniaFact]
    public void DeletingAPhotosSettingsFile_RemakesItsDerivedCopies()
    {
        // The escape hatch, and it is not only the PDF path that has it. Every other rule here
        // infers — from a timestamp, from a recorded length — and an inference that goes wrong leaves
        // the user looking at a picture they know is out of date with no way to say so. Deleting the
        // yaml is that way: nothing on disk describes the file any more, so nothing beside it can
        // be trusted to be of it.
        var photos = Directory.CreateDirectory(Path.Combine(_root, "Photographs")).FullName;
        var jpeg = Path.Combine(photos, "Portrait.jpg");
        MakeJpeg(jpeg, MagickColors.Red);

        Scan();

        var preview = Path.Combine(photos, ".dir2site", "Portrait", "preview-Portrait.webp");
        var stamp = File.GetLastWriteTimeUtc(preview);

        File.Delete(jpeg + ".yaml");
        Thread.Sleep(1100);

        Scan();

        Assert.True(File.GetLastWriteTimeUtc(preview) > stamp, "the thumbnail was not remade");
    }

    [AvaloniaFact]
    public void AnUntouchedPhotoIsLeftAlone()
    {
        // The other half: staleness must not mean "rebuild every run", which would burn the work
        // and rewrite the site's assets on every save.
        var photos = Directory.CreateDirectory(Path.Combine(_root, "Photographs")).FullName;
        var jpeg = Path.Combine(photos, "Portrait.jpg");
        MakeJpeg(jpeg, MagickColors.Red);

        PreviewGenerator.GeneratePreviews(jpeg, _root);
        var preview = Path.Combine(photos, ".dir2site", "Portrait", "preview-Portrait.webp");
        var stamp = File.GetLastWriteTimeUtc(preview);

        Thread.Sleep(1100);
        PreviewGenerator.GeneratePreviews(jpeg, _root);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(preview));
    }
}

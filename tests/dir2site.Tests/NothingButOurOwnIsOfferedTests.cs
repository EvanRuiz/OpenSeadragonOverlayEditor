// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using dir2site.Models;
using dir2site.Services;
using ImageMagick;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// What <see cref="SourceLeftovers"/> is allowed to put in front of the user for deletion.
/// </summary>
/// <remarks>
/// This class is the only thing in the app that decides a path inside the user's project folder is
/// disposable, and the dialog it feeds runs <c>Directory.Delete(recursive: true)</c> on whatever the
/// user accepts. The boundary it has to keep is stated on <see cref="SourceLeftovers"/> itself:
/// <c>_site</c> and <c>.dir2site</c> are ours, the yaml is the user's work and is the one thing worth
/// asking about, and nothing else in the project folder may be named at all.
///
/// A total assertion, deliberately — every returned path must be one of two shapes, rather than a
/// case for each thing that has gone wrong. That distinction is the whole point of this file. The
/// rule was broken by a change that decided a folder was disposable by listing what didn't count in
/// it, using an ignore list borrowed from the artifact walk: it answers "the walk does not descend
/// into this", which is not "nobody would miss this". <c>_media/</c> is skipped by the walk and is
/// the user's, holding files this app never wrote and cannot recreate — and a folder holding one
/// came out on a list whose own heading promised it held nothing but this app's workings, one
/// click from being deleted with it.
///
/// A guard written as "and also allow for _media" would have caught that one and nothing after it.
/// So the question asked here is never "is this thing safe" but "is this one of the two shapes we
/// are permitted to name".
/// </remarks>
public class NothingButOurOwnIsOfferedTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "d2s-owned-" + Guid.NewGuid().ToString("N"));

    public NothingButOurOwnIsOfferedTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string At(params string[] parts) => Path.Combine([_root, .. parts]);

    /// <summary>
    /// The only shape this app may offer: a yaml. Everything else it could name is either the user's,
    /// and none of its business, or its own, and taken without asking.
    /// </summary>
    private static bool MayBeOffered(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a path is this app's own workings rather than the user's file.</summary>
    private static bool IsOurs(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals(".dir2site", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The shapes a project can be in when the artifacts have gone, each of which must yield the
    /// same answer.
    /// </summary>
    /// <remarks>
    /// Two of them, because they pin different things and one fixture cannot do both. The rich one
    /// is the invariant: every kind of file a person keeps beside their work, ordinary ones
    /// included, and none of them may be named. The sparse one is the regression: it holds only what
    /// a name-based ignore list waves through, because the rule that broke bailed out on the first
    /// ordinary file it saw — so a fixture holding an <c>index.md</c> never reached the bug and
    /// passed against the broken code. Narrowing the fixture to catch that would have given up the
    /// breadth; keeping only the broad one would have given up the regression.
    /// </remarks>
    public static TheoryData<string> ProjectShapes() => new() { Rich, Sparse };

    private const string Rich = "everything a person keeps beside their work";
    private const string Sparse = "only what a name-based ignore list waves through";

    /// <summary>Builds one of the shapes above, with the artifact then taken away.</summary>
    private void MakeEmptiedFolder(string shape)
    {
        var articles = Directory.CreateDirectory(At("Articles")).FullName;

        // An artifact, with the two things this app writes beside it.
        File.WriteAllText(Path.Combine(articles, "Piece.jpg"), "not really a jpeg");
        File.WriteAllText(Path.Combine(articles, "Piece.jpg.yaml"), "type: photo\ncaption: A Piece\n");
        Directory.CreateDirectory(Path.Combine(articles, ".dir2site", "Piece"));
        File.WriteAllText(Path.Combine(articles, ".dir2site", "Piece", "preview-Piece.webp"), "thumb");
        PreviewGenerator.WriteStamp(Path.Combine(articles, "Piece.jpg"));

        // The user's own things that a name rule skips: `_media` and `.git` because the artifact
        // walk does not descend into them, `data.json` because the yaml test counts `.json`,
        // `.envrc` because it starts with a dot. None was written by this app.
        Directory.CreateDirectory(Path.Combine(articles, "_media"));
        File.WriteAllText(Path.Combine(articles, "_media", "diagram.png"), "the user's diagram");
        File.WriteAllText(Path.Combine(articles, "data.json"), "{\"notes\": \"the user's\"}");
        File.WriteAllText(Path.Combine(articles, ".envrc"), "export SECRET=1");
        Directory.CreateDirectory(Path.Combine(articles, ".git"));
        File.WriteAllText(Path.Combine(articles, ".git", "HEAD"), "ref: refs/heads/main");

        if (shape == Rich)
        {
            // And the ordinary ones, which no rule here has ever had trouble with — which is exactly
            // why they belong in the fixture that is about the invariant rather than the bug.
            File.WriteAllText(Path.Combine(articles, "index.md"), "# Prose the user wrote\n");
            File.WriteAllText(Path.Combine(articles, "notes.txt"), "the user's notes");
            File.WriteAllText(Path.Combine(articles, "Original.psd"), "the user's layered original");
            Directory.CreateDirectory(Path.Combine(articles, "Scans"));
            File.WriteAllText(Path.Combine(articles, "Scans", "plate-01.tif"), "the user's scan");
        }

        // Now the artifact goes, while nothing is watching — the state this class exists for.
        File.Delete(Path.Combine(articles, "Piece.jpg"));
    }

    [Theory]
    [MemberData(nameof(ProjectShapes))]
    public void EveryPathOfferedIsAYaml(string shape)
    {
        MakeEmptiedFolder(shape);

        var offered = SourceLeftovers.FindLeftoverYamls(_root);

        Assert.NotEmpty(offered);
        foreach (var path in offered)
            Assert.True(MayBeOffered(path),
                $"{Path.GetRelativePath(_root, path)} is not a yaml and must not be put to the user");
    }

    [Theory]
    [MemberData(nameof(ProjectShapes))]
    public void OurOwnLeftoversAreTakenWithoutAsking(string shape)
    {
        // The other side of the same rule. Previews and stamps used to be listed beside the yaml,
        // which put a folder this app owns into a list the user was being asked to approve deleting
        // — and made the dialog's own wording untrue of what it held.
        MakeEmptiedFolder(shape);

        var stamp = At("Articles", ".dir2site", "Piece.stamp");
        var previews = At("Articles", ".dir2site", "Piece");
        Assert.True(File.Exists(stamp) && Directory.Exists(previews));

        SourceLeftovers.RemoveGeneratedLeftovers(_root);

        Assert.False(File.Exists(stamp), "the stamp of a file that is gone was kept");
        Assert.False(Directory.Exists(previews), "the previews of a file that is gone were kept");

        // And the user's yaml is still there to be asked about.
        Assert.True(File.Exists(At("Articles", "Piece.jpg.yaml")));
    }

    [AvaloniaFact]
    public void APreviewBorrowedFromAnArtifactThatGoes_RepointsRatherThanBreaks()
    {
        // The sweep takes .dir2site/{stem} for any stem the folder no longer has, and a hand-written
        // preview: may name another artifact's generated thumbnail. So the question is what becomes
        // of the borrower when the lender goes — a reference broken without a word would be a poor
        // trade for tidiness.
        //
        // It answers itself. A preview path naming a file that is not there is broken rather than
        // chosen, which is the rule NeedsPath already applies: the borrower is surveyed as needing
        // work, rendered, and its yaml repointed at its own thumbnail. Nothing is left pointing at
        // nothing.
        var photos = Directory.CreateDirectory(At("Photographs")).FullName;
        MakeRealPhoto(Path.Combine(photos, "Lender.jpg"));
        MakeRealPhoto(Path.Combine(photos, "Borrower.jpg"));
        Generate();

        File.WriteAllText(Path.Combine(photos, "Borrower.jpg.yaml"),
            "type: photo\ncaption: Borrower\n" +
            "preview: .dir2site/Lender/preview-Lender.webp\n" +
            "previewLarge: .dir2site/Lender/preview-lg-Lender.webp\n");
        Generate();
        Assert.Contains(".dir2site/Lender/", File.ReadAllText(Path.Combine(photos, "Borrower.jpg.yaml")));

        File.Delete(Path.Combine(photos, "Lender.jpg"));
        File.Delete(Path.Combine(photos, "Lender.jpg.yaml"));
        SourceLeftovers.RemoveGeneratedLeftovers(_root);
        Assert.False(Directory.Exists(Path.Combine(photos, ".dir2site", "Lender")));

        Generate();

        var yaml = File.ReadAllText(Path.Combine(photos, "Borrower.jpg.yaml"));
        Assert.DoesNotContain(".dir2site/Lender/", yaml);
        Assert.Contains(".dir2site/Borrower/preview-Borrower.webp", yaml);
        Assert.True(File.Exists(Path.Combine(photos, ".dir2site", "Borrower", "preview-Borrower.webp")));
    }

    /// <summary>A real JPEG, because this one has to survive being rendered from.</summary>
    private static void MakeRealPhoto(string path)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 400, 300);
        image.Write(path);
    }

    /// <summary>A scan and a generate, the way the app runs them.</summary>
    private void Generate()
    {
        var tree = DirectoryTraverser.BuildTree(_root, new List<string>(), new List<string>());
        var config = new Dir2SiteModel { Title = "S", Footer = "f" };
        DirectoryTraverser.GeneratePreviews(tree, config, null);
        SiteGenerator.Generate(_root, tree, config);
    }

    [SkippableFact]
    public void NothingBehindASymlinkIsNamedOrTaken()
    {
        // The walk used to step through a symlinked folder, which put everything under its target in
        // range of both halves of this class — offered to the user as though it were theirs to tidy,
        // and swept outright where it looked like ours. A photo library on another drive, a synced
        // folder, or another dir2site project, whose previews look stranded to us precisely because
        // its artifacts are not ours to see.
        //
        // Lexical containment does not catch this: Path.GetFullPath leaves links alone, so the path
        // passes a StartsWith test while pointing somewhere else entirely. Not stepping through the
        // link is the check, so this drives the two public entry points and asks what survived.
        var outside = Directory.CreateDirectory(Path.Combine(_root, "..",
            "outside-" + Path.GetFileName(_root))).FullName;

        try
        {
            var theirYaml = Path.Combine(outside, "Family.jpg.yaml");
            File.WriteAllText(theirYaml, "type: photo\ncaption: our family\n");
            Directory.CreateDirectory(Path.Combine(outside, ".dir2site", "Ghost"));
            var theirPreview = Path.Combine(outside, ".dir2site", "Ghost", "preview-Ghost.webp");
            File.WriteAllText(theirPreview, "somebody else's thumbnail");

            var articles = Directory.CreateDirectory(At("Articles")).FullName;
            try { Directory.CreateSymbolicLink(Path.Combine(articles, "Linked"), outside); }
            catch (Exception ex)
            {
                // Windows needs a privilege for this that a build agent may not have. Where links
                // cannot be made the case cannot arise either, so saying why beats a false green.
                Skip.If(true, $"this system would not make a symlink: {ex.Message}");
            }

            Assert.DoesNotContain(SourceLeftovers.FindLeftoverYamls(_root),
                p => p.Contains("Family.jpg.yaml", StringComparison.Ordinal));

            SourceLeftovers.RemoveGeneratedLeftovers(_root);

            Assert.True(File.Exists(theirYaml), "a file outside the project was offered and taken");
            Assert.True(File.Exists(theirPreview), "previews outside the project were swept");
        }
        finally
        {
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(".travis.yml")]
    [InlineData(".pre-commit-config.yaml")]
    [InlineData("docker-compose.override.yml")]
    [InlineData("kustomization.prod.yaml")]
    public void AConfigFileOfTheirsIsNotMistakenForALeftover(string name)
    {
        // The leftover shape is "name.ext.yaml with no name.ext beside it", and read as "any second
        // extension" it matches files this app never wrote: .travis, .override, .prod all look like
        // an inner extension. They were listed under a heading saying they had no artifact left, and
        // --force-clean then deleted them — someone's CI config, on every run, from a tool that had
        // never touched it.
        //
        // The question that settles it is the one that would have made a yaml in the first place: is
        // the thing underneath something this app makes artifacts of. Nothing is lost by asking, so
        // this is not a list of exceptions that the next config file gets added to.
        File.WriteAllText(At(name), "the user's own configuration\n");

        Assert.DoesNotContain(At(name), SourceLeftovers.FindLeftoverYamls(_root));
    }

    [Fact]
    public void AGenuineLeftoverIsStillFound()
    {
        // The other side of it: the rule above must not have turned the sweep off.
        File.WriteAllText(At("Portrait.jpg"), "not really a jpeg");
        File.WriteAllText(At("Portrait.jpg.yaml"), "type: photo\ncaption: A Portrait\n");
        File.Delete(At("Portrait.jpg"));

        Assert.Contains(At("Portrait.jpg.yaml"), SourceLeftovers.FindLeftoverYamls(_root));
    }

    [SkippableFact]
    public void ASymlinkedDir2siteIsNotSweptThrough()
    {
        // The one folder neither walk enters: .dir2site is skipped by name on the way past, so it is
        // reached by joining rather than by walking — and a join follows a link. Previews are the
        // heaviest thing in a project, so pointing .dir2site at another volume is the obvious reason
        // to link it; every folder at the target this project has no artifact for is then "stranded"
        // by definition, and swept, with no flag and no dialog.
        var theirs = Directory.CreateDirectory(Path.Combine(_root, "..",
            "elsewhere-" + Path.GetFileName(_root))).FullName;

        try
        {
            var someoneElse = Directory.CreateDirectory(Path.Combine(theirs, "SomeoneElse")).FullName;
            File.WriteAllText(Path.Combine(someoneElse, "important.txt"), "another project's previews");

            var photos = Directory.CreateDirectory(At("Photographs")).FullName;
            File.WriteAllText(Path.Combine(photos, "Portrait.jpg"), "not really a jpeg");

            try { Directory.CreateSymbolicLink(Path.Combine(photos, ".dir2site"), theirs); }
            catch (Exception ex)
            {
                Skip.If(true, $"this system would not make a symlink: {ex.Message}");
            }

            SourceLeftovers.RemoveGeneratedLeftovers(_root);

            Assert.True(Directory.Exists(someoneElse), "a folder outside the project was swept");
            Assert.True(File.Exists(Path.Combine(someoneElse, "important.txt")));
        }
        finally
        {
            try { Directory.Delete(At("Photographs", ".dir2site")); } catch { }
            try { Directory.Delete(theirs, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public void AScanDoesNotDiscardThroughALinkedDir2site()
    {
        // The last of the five joins to .dir2site, and the only one a scan alone can reach: a file
        // with no yaml has one scaffolded, which sends the walk straight to DiscardGenerated to throw
        // away previews that must be of something else. Through a link that is a folder at the
        // target, deleted by opening the project — no generate, no flag, no dialog.
        var theirs = Directory.CreateDirectory(Path.Combine(_root, "..",
            "linked-" + Path.GetFileName(_root))).FullName;

        try
        {
            var mine = Directory.CreateDirectory(Path.Combine(theirs, "Portrait")).FullName;
            File.WriteAllText(Path.Combine(mine, "preview-Portrait.webp"), "somebody else's");

            var photos = Directory.CreateDirectory(At("Photographs")).FullName;
            File.WriteAllText(Path.Combine(photos, "Portrait.jpg"), "not really a jpeg");

            try { Directory.CreateSymbolicLink(Path.Combine(photos, ".dir2site"), theirs); }
            catch (Exception ex)
            {
                Skip.If(true, $"this system would not make a symlink: {ex.Message}");
            }

            // A scan, which is all it takes: no yaml beside the photo, so one is written.
            DirectoryTraverser.BuildTree(_root, new List<string>(), new List<string>());

            Assert.True(Directory.Exists(mine), "a folder outside the project was discarded by a scan");
            Assert.True(File.Exists(Path.Combine(mine, "preview-Portrait.webp")));
        }
        finally
        {
            try { File.Delete(At("Photographs", "Portrait.jpg.yaml")); } catch { }
            try { Directory.Delete(At("Photographs", ".dir2site")); } catch { }
            try { Directory.Delete(theirs, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeletingAFolderDoesNotTakeAYamlNamedAfterIt()
    {
        // The watcher reports a deleted folder here too, and a folder's name has no extension — so
        // "Photos" + ".yaml" is exactly the legacy shape RemoveFor refuses to guess at for files,
        // and a hand-written Photos.yaml beside a folder the user deleted went with it, silently.
        var folder = At("Photos");
        Directory.CreateDirectory(folder);
        File.WriteAllText(At("Photos.yaml"), "notes the user wrote about the folder\n");

        Directory.Delete(folder);
        SourceLeftovers.RemoveFor(folder);

        Assert.True(File.Exists(At("Photos.yaml")), "a hand-written yaml went with a deleted folder");
    }

    [SkippableFact]
    public void TheBoundaryRefusesAPathThatIsWrittenInsideButLeadsOut()
    {
        // The invariant the four separate walk fixes were each approximating: no delete resolves
        // outside the tree it was pointed at. Asked at the delete, so it holds whatever a walk does
        // next — which matters because every one of those four was found by someone probing that
        // particular walk, and the fifth would have been too.
        //
        // Tested directly rather than through a walk, because the walks now make it unreachable, and
        // a guard nobody can reach is a guard nobody has checked.
        var theirs = Directory.CreateDirectory(Path.Combine(_root, "..",
            "beyond-" + Path.GetFileName(_root))).FullName;

        try
        {
            var target = Path.Combine(theirs, "theirs.txt");
            File.WriteAllText(target, "the user's");

            var inside = Directory.CreateDirectory(At("Inside")).FullName;
            File.WriteAllText(Path.Combine(inside, "ours.txt"), "ours");

            try { Directory.CreateSymbolicLink(Path.Combine(inside, "out"), theirs); }
            catch (Exception ex)
            {
                Skip.If(true, $"this system would not make a symlink: {ex.Message}");
            }

            // Written inside the project, and leading out of it. A StartsWith test says yes.
            var throughTheLink = Path.Combine(inside, "out", "theirs.txt");
            Assert.StartsWith(_root, Path.GetFullPath(throughTheLink), StringComparison.Ordinal);
            Assert.False(SourceListing.ResolvesInside(_root, throughTheLink));

            // And the ordinary cases still answer yes, so this is a boundary and not a refusal.
            Assert.True(SourceListing.ResolvesInside(_root, Path.Combine(inside, "ours.txt")));
            Assert.False(SourceListing.ResolvesInside(_root, target));
            Assert.False(SourceListing.ResolvesInside(_root, Path.Combine(_root, "..", "escape.txt")));
        }
        finally
        {
            try { Directory.Delete(At("Inside", "out")); } catch { }
            try { Directory.Delete(theirs, recursive: true); } catch { }
        }
    }

    [Fact]
    public void APresentArtifactKeepsItsPreviews()
    {
        // The guard against sweeping the live site out from under itself.
        MakeEmptiedFolder(Rich);
        File.WriteAllText(At("Articles", "Piece.jpg"), "the artifact is back");

        SourceLeftovers.RemoveGeneratedLeftovers(_root);

        Assert.True(Directory.Exists(At("Articles", ".dir2site", "Piece")));
        Assert.True(File.Exists(At("Articles", ".dir2site", "Piece.stamp")));
    }

    [Theory]
    [MemberData(nameof(ProjectShapes))]
    public void NothingOfTheUsersIsGoneAfterAcceptingEverythingOffered(string shape)
    {
        // The assertion above is about the list; this one is about what accepting it does. The
        // dialog deletes directories recursively, so a folder on the list takes everything under it
        // — which is how a list that looked like two harmless rows removed a folder of diagrams.
        MakeEmptiedFolder(shape);

        var before = UsersFiles();

        foreach (var path in SourceLeftovers.FindLeftoverYamls(_root))
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }

        Assert.Equal(before, UsersFiles());
    }

    /// <summary>
    /// Everything under the project that is the user's: not a yaml, and not this app's workings.
    /// </summary>
    private string[] UsersFiles() =>
        [.. Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Where(p => !MayBeOffered(p) && !IsOurs(p))
            .Select(p => Path.GetRelativePath(_root, p))
            .OrderBy(p => p, StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(ProjectShapes))]
    public void AnEmptiedFolderIsNotOfferedAtAll(string shape)
    {
        // Stated as its own case because it is the mistake that broke the rule. A folder with
        // nothing left to publish is a reason not to *generate* a page for it — which
        // SiteGenerator.HasPublishableContent does, and EmptyFolderTests proves with the folder
        // still sitting on disk. It is not a reason to propose deleting the user's folder.
        MakeEmptiedFolder(shape);

        Assert.DoesNotContain(At("Articles"), SourceLeftovers.FindLeftoverYamls(_root));
    }
}

// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace dir2site.Services;

/// <summary>
/// Finds yaml files and preview folders whose artifact is no longer beside them.
/// </summary>
/// <remarks>
/// <para><b>What this class may name, and nothing else.</b> It is the only thing in the app that
/// decides a path inside the user's project folder is disposable, and everything it returns is
/// offered to the user for recursive deletion. So the boundary is stated here, where the decision
/// is made, rather than left to be reconstructed from four files:</para>
/// <list type="bullet">
/// <item><description><c>_site/*</c>, and what the deploy sends to the server — ours. Removed
/// without asking, and none of it comes through here.</description></item>
/// <item><description><c>.dir2site</c> — ours. Removed without asking, by
/// <see cref="RemoveGeneratedLeftovers"/> when nothing was watching and by <see cref="RemoveFor"/> when
/// something was. It never reaches a dialog.</description></item>
/// <item><description>The yaml — <em>user data</em>, despite this app having scaffolded the file.
/// The captions and credits in it are the user's work, and it is the one thing worth asking about —
/// and only ever on evidence that this app once worked beside the artifact it names.</description></item>
/// <item><description>Everything else in the project folder — the user's, and never named here
/// however disposable it looks.</description></item>
/// </list>
/// <para>Which gives the two halves of what this class finds, and they are not treated alike. A
/// leftover is either <em>ours</em> — a previews folder, a stamp — and goes without asking, or it is
/// the user's yaml, and is found and put to them. Nothing else is either, and nothing else may be
/// named. The methods are named for that split: <see cref="RemoveGeneratedLeftovers"/> takes the
/// first kind, <see cref="FindLeftoverYamls"/> reports the second.</para>
///
/// <para><b>Asked once, and then not again.</b> The evidence a yaml is offered on — the artifact's
/// previews folder, its stamp — is the same thing <see cref="RemoveGeneratedLeftovers"/> takes on
/// the run that offers it. So a yaml the user declines has nothing backing it afterwards and is
/// never raised a second time. That is deliberate: the alternative is an app that asks the same
/// question on every generate for the life of the project, and a nag teaches people to click
/// through dialogs rather than read them. Declining leaves the yaml where it is, permanently and
/// quietly, which is the user's folder behaving as they left it.</para>
///
/// <para>One artifact deleted before a generate ever ran for it leaves a yaml that is never offered,
/// because nothing was ever written beside it to show for. That is litter, and litter is the trade
/// this class makes every time the alternative is deleting something on inference.</para>
///
/// <para>The rule is a whitelist and has to stay one. It was broken once by a change that decided a
/// folder was disposable by listing the things that don't count in it — an ignore list borrowed from
/// the artifact walk, which answers "we don't descend into this", not "nobody would miss this". A
/// folder holding the user's <c>_media/</c> came out the other side on a list headed "settings or
/// preview file", one click from a recursive delete. <c>NothingButOurOwnIsOfferedTests</c> asserts
/// the shape of what <see cref="FindAll"/> returns, totally rather than case by case, because a rule
/// enforced by listing exceptions catches only the last exception.</para>
///
/// Both are named after the file they belong to, so a rename or a deletion carried out while
/// dir2site wasn't running leaves them behind with nothing pointing at them. The watcher would have
/// said which of the two happened; with nothing watching, the shapes are all there is — and they can
/// answer the rename case but not the deletion one. A leftover paired against a file that has
/// appeared under a new name is a rename. A leftover with nothing to pair against could be a
/// deletion or could be almost anything, so it is offered rather than assumed.
/// </remarks>
public static class SourceLeftovers
{
    /// <param name="YamlFiles">Yaml files in <c>name.ext.yaml</c> form with no <c>name.ext</c> beside them.</param>
    /// <param name="PreviewDirs">
    /// What is left under <c>.dir2site/</c> for a stem nothing in the folder has: the previews
    /// folder, and the <c>{stem}.stamp</c> beside it. Both are ours, and both are named after the
    /// artifact rather than living inside anything named after it, so a deletion nobody watched
    /// strands them in the same way.
    /// </param>
    public sealed record Analysis(
        IReadOnlyList<string> YamlFiles,
        IReadOnlyList<string> PreviewDirs);

    public static readonly Analysis Nothing = new([], []);

    /// <summary>
    /// What is left over in one directory: yaml files and preview folders whose artifact is gone.
    /// </summary>
    public static Analysis InDirectory(string dir)
    {
        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch { return Nothing; }

        var present = new HashSet<string>(files.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);

        var stems = new HashSet<string>(
            files.Where(f => !DirectoryTraverser.IsYamlName(Path.GetFileName(f)))
                 .Select(Path.GetFileNameWithoutExtension)!,
            StringComparer.OrdinalIgnoreCase);

        var previewDirs = new List<string>();
        var worked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir2site = Path.Combine(dir, ".dir2site");

        // The walk's symlink guard never sees this one: .dir2site is skipped by name on the way past,
        // so it is reached by joining rather than by walking, and a join follows a link. Point
        // .dir2site at another volume — previews are the heaviest thing in a project, so relocating
        // them is the obvious reason to — and every folder at the target that this project has no
        // artifact for is "stranded" by definition, and swept, with no flag and no dialog.
        if (Directory.Exists(dir2site) && !SourceListing.IsLinkedDirectory(dir2site))
        {
            try
            {
                foreach (var sub in Directory.GetDirectories(dir2site))
                {
                    worked.Add(Path.GetFileName(sub));
                    if (!stems.Contains(Path.GetFileName(sub))) previewDirs.Add(sub);
                }

                // The stamp is a file directly in .dir2site rather than inside the previews folder,
                // deliberately — anything in that folder is copied wholesale into the site and
                // published. But this sweep asked for directories only, so a stamp whose artifact
                // had gone could never be found by it. RemoveFor takes it on a deletion we watched;
                // nothing watching is the case this whole class exists for.
                foreach (var file in Directory.GetFiles(dir2site, "*.stamp"))
                {
                    worked.Add(Path.GetFileNameWithoutExtension(file));
                    if (!stems.Contains(Path.GetFileNameWithoutExtension(file))) previewDirs.Add(file);
                }
            }
            catch { /* unreadable is not the same as empty; say nothing about this folder */ }
        }

        // "Portrait.jpg.yaml" belongs to "Portrait.jpg". The legacy "Portrait.yaml" form is
        // deliberately not considered: beside a missing Portrait.jpg it is indistinguishable from a
        // hand-written file that happens to share the name, and there is no way to tell which
        // without asking.
        //
        // And the shape is not enough on its own. Somebody keeping hand-written metadata beside
        // images that live elsewhere has a folder where every file matches it — that is what a yaml
        // looks like, which is exactly the problem — so on first contact the whole collection was
        // offered for deletion. What settles it is evidence in our own folder that this app once
        // worked beside that artifact: a previews directory of its own, or a stamp. An empty
        // previews directory counts, because every generator makes one before it tries anything, so
        // a video whose poster never downloaded still left the mark.
        var yamlFiles = files
            .Where(f => IsCurrentConventionYaml(Path.GetFileName(f))
                     && !present.Contains(Path.GetFileNameWithoutExtension(Path.GetFileName(f)))
                     && worked.Contains(PreviewStem(Path.GetFileName(f))))
            .ToList();

        return new Analysis(yamlFiles, previewDirs);
    }

    private static bool IsCurrentConventionYaml(string name)
    {
        var ext = Path.GetExtension(name);
        if (!ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return false;

        // name.ext.yaml has a second extension underneath; the legacy name.yaml does not. But any
        // second extension is not enough, and reading it that way put people's own files on a list
        // headed "no artifact left beside them": .travis.yml, .pre-commit-config.yaml,
        // docker-compose.override.yml — .travis, .override and the rest all look like an inner
        // extension. Under the app's own flag they were then deleted.
        //
        // So the question is the one that would have made a yaml in the first place: is the thing
        // underneath something this app makes artifacts of. Nothing is lost by asking — a scaffolded
        // yaml is only ever written beside a file whose extension is in that table, so a name this
        // turns away is a name we could not have written.
        var underneath = Path.GetExtension(Path.GetFileNameWithoutExtension(name));
        return underneath.Length > 0 && YamlParser.ExtensionToType.ContainsKey(underneath);
    }

    /// <summary>The name an artifact's previews folder and stamp are called after, from its yaml.</summary>
    /// <remarks>Two extensions come off: <c>Portrait.jpg.yaml</c> is kept under <c>Portrait</c>.</remarks>
    private static string PreviewStem(string yamlName) =>
        Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(yamlName));

    /// <summary>
    /// Takes away the yaml and previews belonging to an artifact the user deleted.
    /// </summary>
    /// <remarks>
    /// Only ever called for a deletion the watcher saw happen, which is what makes doing it rather
    /// than offering it defensible. The same files reached by inference — noticed only because the
    /// artifact is missing — could equally be a rename we failed to pair or a file that never had
    /// one, so those go to <see cref="InDirectory"/> and get asked about.
    /// </remarks>
    public static void RemoveFor(string sourcePath, IProgress<string>? progress = null)
    {
        var dir  = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var name = Path.GetFileName(sourcePath);
        var removed = false;

        // The current convention only. A legacy "Portrait.yaml" could just as easily be a file the
        // user wrote and named for the same subject, and nothing here can tell the difference.
        // Only for something with an extension. The watcher reports a deleted *folder* here too, and
        // a folder's name has none — so "Photos" + ".yaml" is exactly the legacy shape this method
        // refuses to guess at two lines below, and a hand-written Photos.yaml beside a folder the
        // user deleted went with it, silently. The offering side already applies this test.
        if (Path.GetExtension(name).Length == 0) return;

        foreach (var ext in new[] { ".yaml", ".yml" })
        {
            var yaml = Path.Combine(dir, name + ext);
            if (!File.Exists(yaml)) continue;

            try { File.Delete(yaml); removed = true; } catch { /* leave it for the sweep */ }
        }

        var previews = Path.Combine(dir, ".dir2site", stem);
        if (Directory.Exists(previews) && !SourceListing.IsLinkedDirectory(Path.Combine(dir, ".dir2site")))
        {
            try { Directory.Delete(previews, recursive: true); removed = true; } catch { }
        }

        // The stamp sits beside that folder rather than in it, so it has to be named here too. Left
        // behind it is inert — it describes previews that are gone, and a missing preview is stale
        // whatever a stamp says — but it is still litter for a file nobody has any more.
        var stamp = PreviewGenerator.StampPath(sourcePath);
        if (File.Exists(stamp))
        {
            try { File.Delete(stamp); removed = true; } catch { }
        }

        if (removed) progress?.Report($"Removed the yaml and previews for {name}");
    }

    /// <summary>
    /// The yaml files left beneath <paramref name="root"/> with no artifact to belong to — everything
    /// there is to ask the user about.
    /// </summary>
    /// <remarks>
    /// Yamls and nothing else, which is why this is no longer called <c>FindAll</c>: it once
    /// returned both kinds of leftover, and a name saying "all" outlived the day it stopped.
    /// What <see cref="InDirectory"/> finds under <c>.dir2site</c> is left out on purpose — it is
    /// ours, so <see cref="RemoveGeneratedLeftovers"/> takes it rather than asking. A dialog is for a
    /// decision only the user can make, and whether to keep this app's own thumbnails for a picture
    /// that is gone is not one.
    /// </remarks>
    public static IReadOnlyList<string> FindLeftoverYamls(string root)
    {
        var found = new List<string>();

        foreach (var dir in Walk(root))
            found.AddRange(InDirectory(dir).YamlFiles);

        return found;
    }

    /// <summary>
    /// Takes away the previews and stamps beneath <paramref name="root"/> that belong to artifacts
    /// which are no longer there. Returns how many artifacts were cleaned up after.
    /// </summary>
    /// <remarks>
    /// The unwitnessed twin of <see cref="RemoveFor"/>, and done rather than offered for the same
    /// reason that one is: <c>.dir2site</c> is this app's, and taking away what it wrote needs no
    /// more permission than overwriting it did. The uncertainty that makes a deletion worth asking
    /// about — was this a rename we failed to pair, or a file the user meant to remove — is a
    /// question about <em>their</em> file, and it is answered by leaving their yaml alone and asking
    /// about that. Our thumbnails are wrong either way.
    ///
    /// Only stems nothing in the folder claims, which is the same rule <see cref="InDirectory"/>
    /// already applies, so a rename that has already been paired is not swept out from under itself.
    /// </remarks>
    public static int RemoveGeneratedLeftovers(string root, IProgress<string>? progress = null)
    {
        // Counted by artifact rather than by file. One artifact leaves two things behind — a folder
        // and a stamp — so counting entries reported twice as many as there were pictures, which is
        // a number nobody could match against anything they could see.
        var cleaned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Walk(root))
        {
            foreach (var path in InDirectory(dir).PreviewDirs)
            {
                // The walks are guarded, and this asks anyway. Four separate walks have led out of
                // the tree at one time or another, each fixed where it was found; the boundary is
                // the thing that holds whatever the next walk does.
                if (!SourceListing.ResolvesInside(root, path)) continue;

                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                    else continue;

                    cleaned.Add(Path.Combine(dir, Path.GetFileNameWithoutExtension(path)));
                }
                catch { /* it will be here next time, and this is not worth interrupting a run for */ }
            }
        }

        if (cleaned.Count > 0)
            progress?.Report(cleaned.Count == 1
                ? "Removed the previews of 1 artifact that is no longer there"
                : $"Removed the previews of {cleaned.Count} artifacts that are no longer there");

        return cleaned.Count;
    }

    /// <summary>Every folder inside the project, root included — and no further.</summary>
    /// <remarks>
    /// A symlinked directory is not descended into, and that is a rule about deletion rather than
    /// about walking. Everything this class produces is either removed outright or put to the user
    /// with a tick beside it, so a walk that steps through a link takes the whole of somewhere else
    /// with it: a photo library kept on another drive, a synced folder — or another dir2site project,
    /// whose previews then look stranded to us because its artifacts are not ours to see, and get
    /// swept on an ordinary generate.
    ///
    /// The paths also come back written as though they were local, so a leftovers dialog and a CI log
    /// both name <c>Photographs/Linked/Family.jpg.yaml</c> for a file that is nowhere near the
    /// project. A lexical containment check does not catch it — <see cref="Path.GetFullPath"/>
    /// normalises <c>..</c> and leaves links alone, so the path passes a StartsWith test while
    /// pointing outside. Not stepping through the link is the check.
    ///
    /// The artifact walk does follow links, so content kept behind one is still published and still
    /// gets previews written beside it. What that costs is only that its leftovers are never tidied,
    /// which is litter — and litter is the right thing to trade for not deleting somewhere the user
    /// never pointed us.
    /// </remarks>
    private static IEnumerable<string> Walk(string root)
    {
        yield return root;

        IEnumerable<string> children;
        try { children = Directory.GetDirectories(root); }
        catch { yield break; }

        foreach (var child in children)
        {
            if (DirectoryTraverser.IsIgnoredDirectoryName(Path.GetFileName(child))) continue;
            if (SourceListing.IsLinkedDirectory(child)) continue;
            foreach (var nested in Walk(child)) yield return nested;
        }
    }


}

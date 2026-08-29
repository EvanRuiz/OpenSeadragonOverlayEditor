// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dir2site.Models;

namespace dir2site.Services;

/// <summary>
/// The <c>--generate</c> command: generates a project's site without opening a window, and reports
/// what happened on the console the way the app reports it on the status line.
/// </summary>
/// <remarks>
/// The three stages are called directly rather than through anything shared with the Generate
/// button. What the button does around them is all window: a cancellation token, the tree on
/// screen, the watcher's change batch and render scope, and a dialog for anything unaccounted for.
/// A run with no window has none of that, and a runner parameterised until it could serve both
/// would be longer than the three calls it hid.
/// </remarks>
public static class GenerateCommand
{
    public const int Success = 0;
    public const int GeneratedWithErrors = 1;
    public const int BadArguments = 2;

    /// <summary>
    /// Generates <paramref name="projectFolder"/>'s site. Avalonia must already be initialised —
    /// the templates and the article thumbnails' fonts are loaded through its asset system.
    /// </summary>
    /// <param name="forceClean">
    /// Remove the two things this command otherwise only lists, rather than leaving them for the
    /// app. Both are decisions it will not take on its own — they are the user's files — so this is
    /// how the user takes them, once, in writing, in their own script.
    /// </param>
    public static int Run(
        string projectFolder, bool quiet, TextWriter output, TextWriter error, bool forceClean = false)
    {
        if (!Directory.Exists(projectFolder))
        {
            error.WriteLine($"dir2site: no such folder: {projectFolder}");
            return BadArguments;
        }

        var configPath = Path.Combine(projectFolder, "dir2site.yaml");
        var hadConfig  = File.Exists(configPath);

        string? yaml = null;
        Dir2SiteModel config;
        try
        {
            // Read once and kept: the unknown-key check below wants the same text, and reading the
            // file twice is two chances for it to differ.
            if (hadConfig) yaml = File.ReadAllText(configPath);

            config = yaml is not null
                ? YamlParser.DeserializeAs<Dir2SiteModel>(yaml)
                : new Dir2SiteModel
                {
                    Title  = Path.GetFileName(projectFolder) is { Length: > 0 } n ? n : "My Site",
                    Footer = $"© {DateTime.Now.Year}",
                };
        }
        catch (Exception ex)
        {
            // A dir2site.yaml that doesn't parse is a typo to fix, not a reason to generate the
            // site with defaults that would silently discard the author's colours and title.
            error.WriteLine($"dir2site: could not read {configPath}: {ex.Message}");
            return BadArguments;
        }

        // A folder that has never been opened in the app has no config, so it gets the same defaults
        // and the same new file the app would have given it: the run is already writing the site and
        // completing the artifacts' own yamls, so withholding this one file protects nothing.
        if (!hadConfig)
        {
            try
            {
                YamlParser.SaveDir2SiteConfig(configPath, config);
            }
            catch (Exception ex)
            {
                // Its own arm, because a folder that cannot be written is a different problem from
                // one whose yaml cannot be parsed — and borrowing the read's sentence for it named
                // the wrong operation in the line a script's log keeps.
                error.WriteLine($"dir2site: could not write {configPath}: {ex.Message}");
                return BadArguments;
            }
        }

        // A key that parses but means nothing — primarycolour for primaryColor — takes the setting
        // with it silently, and a scripted run has nobody watching a window to notice the colours
        // came out wrong. The app says this when the project opens; this is the only chance here.
        var configWarnings = new List<string>();
        if (yaml is not null)
            YamlParser.ReportUnknownConfigKeys(yaml, configPath, configWarnings);

        output.WriteLine($"Generating {config.Title} from {projectFolder}");
        if (!hadConfig) output.WriteLine($"Wrote {Path.GetFileName(configPath)}, with the settings a new project starts from.");

        // No sink: every stage of the pipeline reports per file — one line per copied framework
        // asset, per preview, per page — and the framework assets alone are well over a hundred
        // lines whatever the project holds. The stages below are printed here instead, which is
        // what the run is doing.
        var tracker = new GenerateProgressTracker();

        void Stage(string message)
        {
            tracker.Report(message);
            if (!quiet) output.WriteLine($"  {message}");
        }

        (string Summary, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
            IReadOnlyList<string> Orphans) result;
        IReadOnlyList<string> leftoverYamls = [];

        // Read before the generate below writes it. A folder this app has never generated has no
        // history to call anything a leftover against — every judgement it could make would come
        // from the shape of a filename, which is the reasoning SourceLeftovers refuses everywhere
        // else. So a first run says what it found and offers none of it; the record it writes at the
        // end opens the gate for the next one.
        var generatedBefore = File.Exists(SiteGenerator.GeneratedManifestPath(projectFolder));
        try
        {
            Stage("Scanning for changes...");
            var files        = new List<string>();
            var artifacts    = new List<string>();
            var updatedYamls = new List<string>();
            var root = DirectoryTraverser.BuildTree(projectFolder, files, artifacts, tracker, updatedYamls);
            ReportUpdatedYamls(updatedYamls, output);

            // Nothing was watching — nothing ever is, from here — so this run is the unwitnessed
            // case by definition, and the previews and stamps of artifacts that have gone are still
            // lying about. They are ours, so they go without asking, the same as in the app. Before
            // the previews stage, so a stem a different file has taken over starts from nothing.
            //
            // Only that half. The yamls the same sweep finds are the user's captions and credits,
            // and this command has nobody to ask — so it leaves them alone rather than deciding for
            // them, which is the same call it makes about files in _site it cannot account for.
            //
            // Asked before they are deleted, and that order is the whole of "asks once": the
            // evidence that an artifact was ever ours is the previews folder and stamp that
            // deletion takes, so a yaml looked at afterwards has nothing backing it and would never
            // be named at all rather than named once.
            leftoverYamls = SourceLeftovers.FindLeftoverYamls(projectFolder);

            // Not on a run with no history, and that is the stronger reading of the same rule: a
            // first run deletes nothing at all, not even our own. Otherwise this takes the previews
            // and stamps that are the evidence the report below is built from, so the promise it
            // prints — offered on the next generate — is broken by the run that makes it.
            if (generatedBefore)
            {
                Stage("Deleting previews of artifacts that have gone...");
                SourceLeftovers.RemoveGeneratedLeftovers(projectFolder, tracker);
            }

            // Previews first, so the config's PDF resize and quality settings reach what they make.
            Stage("Generating previews...");
            DirectoryTraverser.GeneratePreviews(root, config, tracker);

            Stage("Generating site...");
            result = SiteGenerator.Generate(projectFolder, root, config, tracker);
        }
        catch (Exception ex)
        {
            error.WriteLine($"dir2site: generate failed: {ex.Message}");
            return GeneratedWithErrors;
        }

        output.WriteLine(result.Summary);
        output.WriteLine(tracker.Snapshot().Counters);

        foreach (var message in configWarnings.Concat(result.Warnings))
            output.WriteLine($"warning: {message}");

        // Reported, never removed. In the app this is a question with a Delete button; a scripted
        // run has nobody to ask, and deleting a published file because a scan didn't see its source
        // is not a thing to do on the strength of an exit code.
        // The two things this run found and will not decide about on its own. They were not reported
        // alike: the site's orphans were listed and the leftover yamls were passed over in silence,
        // so a project generated only from a script accumulated the captions of artifacts that had
        // gone with nothing ever saying so. Same situation, same treatment.

        var removing = forceClean && generatedBefore;

        Report(output, result.Orphans.Count, "file(s) in _site no longer have a source:",
            result.Orphans, removing, generatedBefore);

        Report(output, leftoverYamls.Count, "yaml file(s) have no artifact left beside them:",
            leftoverYamls.Select(y => Path.GetRelativePath(projectFolder, y)), removing, generatedBefore);

        if (removing)
        {
            var removed = SiteGenerator.RemoveOrphans(projectFolder + Path.DirectorySeparatorChar + "_site",
                result.Orphans);
            foreach (var failure in removed.Errors) error.WriteLine($"dir2site: {failure}");

            foreach (var stranded in leftoverYamls)
            {
                // Asked at the delete, not left to the walk that found it. This is the one deletion
                // here with no human in front of it, so it is the one that most wants the boundary
                // checked where the boundary matters.
                if (!SourceListing.ResolvesInside(projectFolder, stranded))
                {
                    error.WriteLine($"dir2site: {stranded}: not removed — it resolves outside the project folder.");
                    continue;
                }

                try { File.Delete(stranded); }
                catch (Exception ex)
                {
                    error.WriteLine($"dir2site: {Path.GetRelativePath(projectFolder, stranded)}: {ex.Message}");
                }
            }
        }

        if (result.Errors.Count == 0) return Success;

        error.WriteLine($"{result.Errors.Count} error(s):");
        foreach (var message in result.Errors)
            error.WriteLine($"  {message}");
        return GeneratedWithErrors;
    }

    /// <summary>
    /// Says when the scan filled in settings a yaml was missing, because that edits files in the
    /// project — a scripted run would otherwise leave the user to find it as an unexplained diff.
    /// </summary>
    private static void ReportUpdatedYamls(IReadOnlyList<string> updatedYamls, TextWriter output)
    {
        if (updatedYamls.Count == 0) return;

        var subject = updatedYamls.Count == 1
            ? $"1 yaml file ({Path.GetFileName(updatedYamls[0])})"
            : $"{updatedYamls.Count:N0} yaml files";
        output.WriteLine($"Added the settings that were missing to {subject}. " +
                         "Values you had already written are unchanged.");
    }

    /// <summary>
    /// Lists one kind of thing the run found and would not act on, and says how to make it act.
    /// </summary>
    /// <remarks>
    /// One method for both kinds on purpose. They are the same situation — something is here that no
    /// longer belongs to anything, and deciding is the user's — and when each had its own code one of
    /// them was written and the other was not, which is how the yamls went unmentioned for as long as
    /// they did.
    /// </remarks>
    private static void Report(
        TextWriter output, int count, string heading, IEnumerable<string> items,
        bool removing, bool generatedBefore)
    {
        if (count == 0) return;

        output.WriteLine($"{count} {heading}");
        foreach (var item in items) output.WriteLine($"  {item}");

        output.WriteLine(!generatedBefore
            ? "This folder has not been generated before, so nothing is offered. It will be on the next generate."
            : removing
                ? "Removed, as --force-clean asked."
                : "Left in place. Open the project in the app, or re-run with --force-clean to remove them.");
    }
}

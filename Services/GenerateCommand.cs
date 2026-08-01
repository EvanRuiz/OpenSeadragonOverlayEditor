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
    public static int Run(string projectFolder, bool quiet, TextWriter output, TextWriter error)
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
        try
        {
            Stage("Scanning for changes...");
            var files        = new List<string>();
            var artifacts    = new List<string>();
            var updatedYamls = new List<string>();
            var root = DirectoryTraverser.BuildTree(projectFolder, files, artifacts, tracker, updatedYamls);
            ReportUpdatedYamls(updatedYamls, output);

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
        if (result.Orphans.Count > 0)
        {
            output.WriteLine($"{result.Orphans.Count} file(s) in _site no longer have a source:");
            foreach (var orphan in result.Orphans)
                output.WriteLine($"  {orphan}");
            output.WriteLine("Left in place. Open the project in the app to remove them.");
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
}

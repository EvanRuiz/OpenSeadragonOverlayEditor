// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Runtime.Versioning;
using Avalonia.Headless.XUnit;
using dir2site.Services;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// The <c>--generate</c> command end to end, minus the process: a project folder in, a site and an
/// exit code out. Run headless, which is also how the real command initialises Avalonia.
/// </summary>
public class GenerateCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "d2s-cli-" + Guid.NewGuid().ToString("N"));

    public GenerateCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private void WriteConfig(string yaml) =>
        File.WriteAllText(Path.Combine(_root, "dir2site.yaml"), yaml);

    /// <summary>A folder with one artifact in it, so the run has something to render.</summary>
    private void WriteArtifact(string folderName, string caption)
    {
        var folder = Path.Combine(_root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Plate.jpg"), "not really a jpeg");
        File.WriteAllText(Path.Combine(folder, "Plate.jpg.yaml"),
            $"type: photo\ncaption: {caption}\n");
    }

    private (int Code, string Out, string Error) Run(
        bool quiet = true, string? folder = null, bool forceClean = false)
    {
        var output = new StringWriter();
        var error  = new StringWriter();
        var code = GenerateCommand.Run(folder ?? _root, quiet, output, error, forceClean);
        return (code, output.ToString(), error.ToString());
    }

    [AvaloniaFact]
    public void GeneratesTheSiteAndSucceeds()
    {
        WriteConfig("title: Riverbend\nfooter: © 2026\n");
        WriteArtifact("Prints", "A Plate");

        var (code, output, error) = Run();

        Assert.Equal(GenerateCommand.Success, code);
        Assert.Equal(string.Empty, error);
        Assert.True(File.Exists(Path.Combine(_root, "_site", "index.html")));
        Assert.True(File.Exists(Path.Combine(_root, "_site", "Prints", "index.html")));
        // The counters the app shows on its status line, so a scripted run reports the same numbers.
        Assert.Contains("Pages", output);
    }

    /// <summary>
    /// The whole point of a real dir2site.yaml: the site is built with the project's own settings,
    /// not with whatever defaults the caller happened to have.
    /// </summary>
    [AvaloniaFact]
    public void UsesTheSettingsFromTheProjectsOwnYaml()
    {
        WriteConfig("title: Riverbend Press\nprimaryColor: '#2e7d5b'\n");
        WriteArtifact("Prints", "A Plate");

        Run();

        var css  = File.ReadAllText(Path.Combine(_root, "_site", "css", "site.css"));
        var home = File.ReadAllText(Path.Combine(_root, "_site", "index.html"));
        Assert.Contains("#2e7d5b", css);
        Assert.Contains("Riverbend Press", home);
    }

    [AvaloniaFact]
    public void QuietPrintsTheSummaryButNotEveryStage()
    {
        WriteConfig("title: Riverbend\n");
        WriteArtifact("Prints", "A Plate");

        var loud = Run(quiet: false);
        Directory.Delete(Path.Combine(_root, "_site"), recursive: true);
        var quiet = Run(quiet: true);

        Assert.Contains("Generating site...", loud.Out);
        Assert.DoesNotContain("Generating site...", quiet.Out);
        Assert.Contains("Site generated", quiet.Out);
    }

    /// <summary>
    /// A config key that parses but means nothing takes its setting with it. The app says so when
    /// the project opens; a scripted run has only this to go on.
    /// </summary>
    [AvaloniaFact]
    public void AMisspelledConfigSettingIsReported()
    {
        WriteConfig("title: Riverbend\nprimarycolour: '#2e7d5b'\n");
        WriteArtifact("Prints", "A Plate");

        var (code, output, _) = Run();

        Assert.Equal(GenerateCommand.Success, code);
        Assert.Contains("primarycolour", output);
        // A typo is not a failure: the site is still generated.
        Assert.True(File.Exists(Path.Combine(_root, "_site", "index.html")));
    }

    /// <summary>
    /// The stages, not the files. Every framework asset copied reports itself through the same
    /// progress channel, and there are well over a hundred of them whatever the project holds.
    /// </summary>
    [AvaloniaFact]
    public void TheDefaultOutputIsStagesRatherThanEveryFile()
    {
        WriteConfig("title: Riverbend\n");
        WriteArtifact("Prints", "A Plate");

        var (_, output, _) = Run(quiet: false);

        Assert.Contains("Generating site...", output);
        Assert.DoesNotContain("Copying", output);
        Assert.True(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length < 12,
            $"expected a handful of lines, got:\n{output}");
    }

    /// <summary>
    /// A folder it cannot write is not a folder whose yaml it could not parse, and the line a
    /// script's log keeps should name the operation that actually failed.
    /// </summary>
    /// <remarks>
    /// The one <see cref="SkippableFact"/> among this class's Avalonia facts, because Skip.If under
    /// an AvaloniaFact throws instead of skipping. It works because the write fails before anything
    /// needs Avalonia — move the config write after the first stage and this stops testing the
    /// message and starts testing an uninitialised toolkit.
    /// </remarks>
    [SkippableFact]
    [UnsupportedOSPlatform("windows")]   // Says to the analyzer what the Skip.If says at runtime.
    public void AFolderItCannotWriteSaysSo()
    {
        // Read-only is an ACL on Windows, not a mode bit, and the two calls below throw
        // PlatformNotSupportedException there rather than doing anything — so this case has to be
        // written differently for Windows or not at all, and it is not written here.
        Skip.If(OperatingSystem.IsWindows(), "Unix file modes only.");

        WriteArtifact("Prints", "A Plate");
        var mode = File.GetUnixFileMode(_root);
        File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var (code, _, error) = Run();

            Assert.Equal(GenerateCommand.BadArguments, code);
            Assert.Contains("could not write", error);
            Assert.DoesNotContain("could not read", error);
        }
        finally
        {
            File.SetUnixFileMode(_root, mode);
        }
    }

    [AvaloniaFact]
    public void AMissingFolderIsAnArgumentError()
    {
        var (code, _, error) = Run(folder: Path.Combine(_root, "nope"));

        Assert.Equal(GenerateCommand.BadArguments, code);
        Assert.Contains("no such folder", error);
        Assert.False(Directory.Exists(Path.Combine(_root, "nope", "_site")));
    }

    /// <summary>
    /// A config that doesn't parse stops the run. Generating with the defaults instead would
    /// quietly publish a site with the wrong title and none of the author's colours.
    /// </summary>
    [AvaloniaFact]
    public void AnUnparseableConfigStopsTheRun()
    {
        WriteConfig("title: [unclosed\n");
        WriteArtifact("Prints", "A Plate");

        var (code, _, error) = Run();

        Assert.Equal(GenerateCommand.BadArguments, code);
        Assert.Contains("dir2site.yaml", error);
        Assert.False(Directory.Exists(Path.Combine(_root, "_site")));
    }

    /// <summary>
    /// A folder that has never been opened in the app has no dir2site.yaml. It gets the defaults and
    /// the file, as it would from the app — this writes to the project either way, so there is no
    /// reading on which leaving it out would be the more careful choice.
    /// </summary>
    [AvaloniaFact]
    public void AProjectWithNoConfigGetsOneWrittenForIt()
    {
        WriteArtifact("Prints", "A Plate");

        var (code, output, _) = Run(quiet: false);

        Assert.Equal(GenerateCommand.Success, code);
        Assert.True(File.Exists(Path.Combine(_root, "_site", "index.html")));

        var configPath = Path.Combine(_root, "dir2site.yaml");
        Assert.True(File.Exists(configPath));
        // The app's defaults: the folder's own name, and a copyright line for this year.
        Assert.Contains(Path.GetFileName(_root), File.ReadAllText(configPath));
        Assert.Contains("dir2site.yaml", output);
    }

    [AvaloniaFact]
    public void ItTakesAwayThePreviewsOfArtifactsThatHaveGone()
    {
        // The command line is the unwitnessed case by definition — nothing is ever watching from
        // here — so the previews and stamps of deleted artifacts are exactly what it finds lying
        // about. They are this app's own files, so they go without asking, as they do in the app.
        WriteConfig("title: Riverbend\nfooter: © 2026\n");
        WriteArtifact("Prints", "A Plate");
        WriteArtifact("Drawings", "A Drawing");
        Run();

        var plate = Path.Combine(_root, "Prints", "Plate.jpg");
        var previews = Path.Combine(_root, "Prints", ".dir2site", "Plate");
        Assert.True(Directory.Exists(previews), "setup: the run made no previews to leave behind");

        File.Delete(plate);
        Run();

        Assert.False(Directory.Exists(previews), "the previews of a deleted artifact were kept");
        Assert.False(File.Exists(PreviewGenerator.StampPath(plate)), "its stamp was kept");
    }

    [AvaloniaFact]
    public void ItLeavesTheUsersYamlAlone()
    {
        // The other half of the same sweep, and the half this command may not act on: a yaml holds
        // captions and credits the user wrote, and there is nobody here to ask about it. The same
        // call it already makes about files in _site it cannot account for — list, never delete.
        WriteConfig("title: Riverbend\nfooter: © 2026\n");
        WriteArtifact("Prints", "A Plate");
        WriteArtifact("Drawings", "A Drawing");
        Run();

        File.Delete(Path.Combine(_root, "Prints", "Plate.jpg"));
        Run();

        Assert.True(File.Exists(Path.Combine(_root, "Prints", "Plate.jpg.yaml")),
            "the user's yaml was deleted by a command that has nobody to ask");
    }

    // ---- what it found and will not decide about ----------------------------

    /// <summary>A project with one artifact deleted, so both kinds of leftover exist at once.</summary>
    private void MakeLeftovers()
    {
        WriteConfig("title: Riverbend\nfooter: © 2026\n");
        WriteArtifact("Prints", "A Plate");
        WriteArtifact("Drawings", "A Drawing");
        Run();

        File.Delete(Path.Combine(_root, "Prints", "Plate.jpg"));
        File.WriteAllText(Path.Combine(_root, "_site", "CNAME"), "example.test\n");
    }

    [AvaloniaFact]
    public void ItSaysWhatItFoundAndWillNotRemove()
    {
        // Both kinds, reported alike. They were not: files in _site were listed and leftover yamls
        // were passed over in silence, so a project only ever generated from a script accumulated
        // the captions of artifacts that had gone with nothing anywhere saying so.
        MakeLeftovers();

        var (code, output, _) = Run();

        Assert.Equal(GenerateCommand.Success, code);
        Assert.Contains("CNAME", output);
        Assert.Contains(Path.Combine("Prints", "Plate.jpg.yaml"), output);
        Assert.Contains("--force-clean", output);

        // Said, not done — this command has nobody to ask.
        Assert.True(File.Exists(Path.Combine(_root, "Prints", "Plate.jpg.yaml")));
        Assert.True(File.Exists(Path.Combine(_root, "_site", "CNAME")));
    }

    [AvaloniaFact]
    public void ForceCleanRemovesBothKinds()
    {
        // The way a script says yes once, in writing, to what the app would otherwise ask about.
        MakeLeftovers();

        var (code, output, error) = Run(forceClean: true);

        Assert.Equal(GenerateCommand.Success, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("Removed, as --force-clean asked.", output);

        Assert.False(File.Exists(Path.Combine(_root, "Prints", "Plate.jpg.yaml")));
        Assert.False(File.Exists(Path.Combine(_root, "_site", "CNAME")));

        // And nothing that still has an artifact goes with them.
        Assert.True(File.Exists(Path.Combine(_root, "Drawings", "Plate.jpg.yaml")));
        Assert.True(File.Exists(Path.Combine(_root, "_site", "Drawings", "index.html")));
    }

    [AvaloniaFact]
    public void ARunWithNothingLeftOverSaysNothing()
    {
        // The guard against a command that nags. Most runs have no leftovers at all, and neither
        // list may appear when there is nothing in it.
        WriteConfig("title: Riverbend\nfooter: © 2026\n");
        WriteArtifact("Prints", "A Plate");
        Run();

        var (_, output, _) = Run();

        Assert.DoesNotContain("no longer have a source", output);
        Assert.DoesNotContain("no artifact left", output);
        Assert.DoesNotContain("--force-clean", output);
    }
}

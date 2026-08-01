// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using dir2site.Services;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// The command line is the seam where a mistyped argument either says so or silently opens a
/// window that nobody launching from a script is watching. These pin which is which.
/// </summary>
public class CommandLineTests
{
    [Fact]
    public void NoArgumentsOpensTheApp()
    {
        Assert.Equal(CommandLineMode.Gui, CommandLine.Parse([]).Mode);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpIsRecognised(string arg)
    {
        Assert.Equal(CommandLineMode.Help, CommandLine.Parse([arg]).Mode);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void VersionIsRecognised(string arg)
    {
        Assert.Equal(CommandLineMode.Version, CommandLine.Parse([arg]).Mode);
    }

    [Theory]
    [InlineData("--generate", "/tmp/site")]
    [InlineData("-g", "/tmp/site")]
    [InlineData("--generate=/tmp/site", null)]
    public void GenerateTakesItsFolderInEveryForm(string first, string? second)
    {
        var options = CommandLine.Parse(second is null ? [first] : [first, second]);

        Assert.Equal(CommandLineMode.Generate, options.Mode);
        Assert.Equal(Path.GetFullPath("/tmp/site"), options.ProjectFolder);
        Assert.False(options.Quiet);
    }

    [Theory]
    [InlineData("--quiet")]
    [InlineData("-q")]
    public void QuietIsRecognisedOnEitherSideOfGenerate(string quiet)
    {
        Assert.True(CommandLine.Parse(["--generate", "site", quiet]).Quiet);
        Assert.True(CommandLine.Parse([quiet, "--generate", "site"]).Quiet);
    }

    [Fact]
    public void RelativeFoldersAreResolvedAgainstTheWorkingDirectory()
    {
        var options = CommandLine.Parse(["--generate", "some-project"]);

        Assert.Equal(Path.GetFullPath("some-project"), options.ProjectFolder);
    }

    [Fact]
    public void GenerateWithoutAFolderIsRejected()
    {
        var options = CommandLine.Parse(["--generate"]);

        Assert.Equal(CommandLineMode.Invalid, options.Mode);
        Assert.Contains("folder", options.Error);
    }

    /// <summary>
    /// The switch that follows must not be eaten as the folder — otherwise a generate would run
    /// against a directory called "--quiet" and report that it doesn't exist.
    /// </summary>
    [Fact]
    public void AFlagIsNotMistakenForTheFolder()
    {
        var options = CommandLine.Parse(["--generate", "--quiet"]);

        Assert.Equal(CommandLineMode.Invalid, options.Mode);
    }

    [Fact]
    public void AnUnknownSwitchIsRejectedRatherThanIgnored()
    {
        var options = CommandLine.Parse(["--publish"]);

        Assert.Equal(CommandLineMode.Invalid, options.Mode);
        Assert.Contains("--publish", options.Error);
    }

    /// <summary>
    /// A bare path with no --generate is a mistake worth naming: opening the window instead would
    /// look like the command had hung.
    /// </summary>
    [Fact]
    public void ABarePathIsNotAnImpliedGenerate()
    {
        Assert.Equal(CommandLineMode.Invalid, CommandLine.Parse(["/tmp/site"]).Mode);
    }
}

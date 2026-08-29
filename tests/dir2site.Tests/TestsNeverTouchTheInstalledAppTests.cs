// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dir2site.Services;
using dir2site.SftpSync.Core;
using dir2site.SftpSync.Core.Credentials;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// A test run must not open the files an installed dir2site is using.
///
/// It did. Five stores composed <c>%AppData%/dir2site/&lt;area&gt;</c> from a hard-coded name and
/// both keychain stores filed secrets under that same word, so running the suite on a machine with
/// the app open put two processes on one set of files. The suite passed or failed depending on
/// whether the app happened to be running, and the real store had accumulated 3,950 leftover
/// deploy records from test runs.
///
/// <see cref="AppDataPaths"/> now takes the name from the running program instead, which separates
/// the two without either side having to know about the other. These pin both halves: that a test
/// host lands somewhere else, and that the app itself still lands exactly where it always did — the
/// second mattering because a path that "isolates" by moving production too would silently orphan
/// every existing user's profiles, credentials and recent projects.
/// </summary>
public class TestsNeverTouchTheInstalledAppTests
{
    /// <summary>
    /// Where an installed copy keeps its state. Written out rather than taken from the code under
    /// test, so that changing that code cannot quietly move a real user's files.
    /// </summary>
    private static string InstalledAppRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dir2site");

    [Fact]
    public void TheSuiteIsNotRunningAsTheApp()
    {
        Assert.False(
            AppDataPaths.IsTheApp,
            $"the test host calls itself \"{AppDataPaths.AppName}\", which is the app's own name — "
            + "every store below is about to resolve to the installed app's files");
    }

    /// <summary>
    /// The stronger property, and the one that keeps this from recurring: a run writes nowhere
    /// under Application Support at all. Landing there under another name would still leave a pile
    /// that nothing empties, and would still be one pile shared by every concurrent run.
    /// </summary>
    [Fact]
    public void TheRunWritesNowhereUnderApplicationSupport()
    {
        var appSupport = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        Assert.False(
            Path.GetFullPath(AppDataPaths.Root)
                .StartsWith(appSupport + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"this run keeps its state in {AppDataPaths.Root}, which is inside the real "
            + $"{appSupport}. See {nameof(AppDataSandbox)}.");
    }

    /// <summary>
    /// And it is this run's own, so two suites at once — two worktrees, a CI matrix — cannot land
    /// on each other the way a test and the app used to.
    /// </summary>
    [Fact]
    public void TheRunHasItsOwnDirectoryRatherThanASharedOne()
    {
        Assert.Equal(
            Environment.GetEnvironmentVariable(AppDataPaths.RootOverrideVariable),
            AppDataPaths.Root);

        Assert.Contains("d2s-appdata-", AppDataPaths.Root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(AppDataPaths.Root));
    }

    /// <summary>
    /// Production may not build a per-user path for itself — <see cref="AppDataPaths"/> is the only
    /// place that knows the app's name or reaches for the folder it lives in.
    ///
    /// The theory below lists the five stores that exist. That is a list, and a list is true of what
    /// is on it: a sixth store composing <c>%AppData%/dir2site/&lt;area&gt;</c> by hand would simply
    /// be absent from it, and every test here would pass while it wrote into the installed app's
    /// folder — the failure that put 3,950 records in a real user's store. These two ask the tree
    /// instead, so a store nobody added to the list is still caught.
    /// </summary>
    [Theory]
    [InlineData("\"dir2site\"", "names the app itself")]
    [InlineData("SpecialFolder.ApplicationData", "reaches for the per-user folder")]
    public void OnlyAppDataPathsKnowsWhereStateLives(string needle, string what)
    {
        var root = RepoFiles.Root();

        // The installer picks between MyDocuments and ApplicationData as a destination for a file it
        // hands the user; it composes nothing, taking AppDataPaths.Root for the app-data case.
        var allowed = new[] { "Services/AppDataPaths.cs", "Services/VsCodeExtensionInstaller.cs" };

        var offenders = ProductionSources(root)
            .Select(f => (File: Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'),
                          Lines: File.ReadAllLines(f)))
            .Where(x => !allowed.Contains(x.File))
            .SelectMany(x => x.Lines
                .Select((text, i) => (Line: i + 1, Text: text))
                .Where(l => l.Text.Contains(needle, StringComparison.Ordinal))
                .Select(l => $"  {x.File}:{l.Line}  {l.Text.Trim()}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"only AppDataPaths may say what {what}. Use AppDataPaths.Area(...) instead:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>Shipping code — not the tests, which say the app's name on purpose.</summary>
    private static IEnumerable<string> ProductionSources(string root)
    {
        var shipped = new[] { "Services", "SftpSync", "ViewModels", "Models", "Views", "Converters" };

        // Program.cs, App.axaml.cs and ViewLocator.cs sit at the root and ship like the rest — and
        // startup is exactly where someone would reach for a path.
        return RepoFiles.Sources(root, "*.cs")
            .Where(f =>
            {
                var parts = RepoFiles.Segments(root, f);
                return parts.Length == 1 || shipped.Contains(parts[0]);
            });
    }

    /// <summary>
    /// Every store that keeps per-user state. Each is the property production actually reads, so a
    /// listed one moving into the app's folder fails here — with the pair above covering the ones
    /// nobody thought to list.
    /// </summary>
    public static TheoryData<string, string> Stores() => new()
    {
        { "recent projects", AppDataPaths.Area("ui") },
        { "window geometry", AppDataPaths.Area("ui") },
        { "sftp profiles", SftpProfileStore.ProfilesDir },
        { "deploy records", DeployLocalStore.LocalDir },
        { "credentials", CredentialStoreFactory.CredentialsDir },
    };

    [Theory]
    [MemberData(nameof(Stores))]
    public void NoStoreResolvesInsideTheInstalledAppsFolder(string what, string path)
    {
        var full = Path.GetFullPath(path);
        Assert.False(
            full.StartsWith(Path.GetFullPath(InstalledAppRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase),
            $"the {what} store resolves to {full}, inside the installed app's own folder");
    }

    /// <summary>
    /// The keychain is the one that would be felt outside this repo: a service name shared with the
    /// app means a test writing a password into the entry a real deploy reads back.
    /// </summary>
    [Fact]
    public void TheCredentialServiceIsNotTheApps()
    {
        Assert.NotEqual("dir2site", AppDataPaths.CredentialService);
    }

    /// <summary>
    /// And the other direction, which is the one that would be felt outside this repo: an installed
    /// copy has to keep composing the path it always did, or every existing user's profiles,
    /// credentials and recent projects are orphaned by an upgrade.
    ///
    /// Asked of <see cref="AppDataPaths.ComposeRoot"/> with the app's inputs, because nothing in
    /// this process runs as the app: <see cref="AppDataPaths.Root"/> resolved once, at load, under
    /// the sandbox's override.
    /// </summary>
    [Theory]
    [InlineData("ui")]
    [InlineData("profiles")]
    [InlineData("local")]
    [InlineData("credentials")]
    public void TheAppItselfStillLandsWhereItAlwaysDid(string area)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // The app's case: no override set, and the entry assembly named after the release build.
        var asTheApp = Path.Combine(AppDataPaths.ComposeRoot(null, appData, "dir2site"), area);

        // The shape the five stores were written as before any of this existed, verbatim.
        var historical = Path.Combine(appData, "dir2site", area);

        Assert.Equal(historical, asTheApp);
        Assert.Equal(Path.Combine(InstalledAppRoot, area), asTheApp);
    }

    /// <summary>
    /// And the name itself, which is the input the two literals above stand in for.
    ///
    /// Where a real user's files land is decided at runtime by the production assembly's name.
    /// <c>dir2site.csproj</c> sets no <c>&lt;AssemblyName&gt;</c>, so it defaults to the project
    /// filename and is right today — but adding one, or renaming the project, would move every
    /// installed copy to a new folder while the path tests above stayed green, since both of their
    /// sides are written out by hand. This is the one assertion that reads the real value.
    /// </summary>
    [Fact]
    public void TheShippingAssemblyIsStillCalledDir2Site()
    {
        Assert.Equal("dir2site", typeof(AppDataPaths).Assembly.GetName().Name);
    }

    /// <summary>
    /// A relative value is not an override. It would resolve against the process working directory
    /// — "/" for a double-clicked .app — putting a real user's state somewhere they would never
    /// find it, and orphaning what they already had.
    /// </summary>
    [Theory]
    [InlineData("data")]
    [InlineData("./state")]
    [InlineData("..")]
    public void ARelativeOverrideIsIgnored(string overrideValue)
    {
        Assert.Equal(
            Path.Combine("/app-data", "dir2site"),
            AppDataPaths.ComposeRoot(overrideValue, "/app-data", "dir2site"));
    }

    /// <summary>
    /// An empty variable is not an override either. Otherwise a stray <c>DIR2SITE_APP_DATA=</c> in
    /// a shell profile would silently move a real user's data to the process's working directory.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentOrEmptyOverrideLeavesTheAppWhereItIs(string? overrideValue)
    {
        Assert.Equal(
            Path.Combine("/app-data", "dir2site"),
            AppDataPaths.ComposeRoot(overrideValue, "/app-data", "dir2site"));
    }
}

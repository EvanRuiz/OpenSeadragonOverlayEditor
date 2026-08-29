// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;

namespace dir2site.Services;

/// <summary>
/// Where this program keeps its per-user state, and the name it files it under.
///
/// The name is the running program's, not the constant "dir2site". Under the app they are the same
/// word, so an installed copy reads and writes exactly what it always has. Under a test host they
/// are not, and that is the point: a test run gets its own folder and its own keychain service
/// instead of opening the files an installed dir2site is using at that moment.
///
/// It used to be the constant everywhere. Five stores composed
/// <c>%AppData%/dir2site/&lt;area&gt;</c> by hand and the two keychain stores hard-coded the same
/// word as their service, so `dotnet test` on a machine with dir2site open had both processes on
/// one set of files: a suite that passed or failed depending on whether the app happened to be
/// running, and 3,950 leftover deploy records in a real user's store.
/// </summary>
public static class AppDataPaths
{
    /// <summary>
    /// The running program's name — "dir2site" in the app, the test host's name under test.
    /// Falls back to the app's name if there is no managed entry assembly to ask.
    /// </summary>
    public static string AppName { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name is { Length: > 0 } name ? name : "dir2site";

    /// <summary>
    /// Set this to put the whole tree somewhere else. It exists for test runs, not for users: it is
    /// in no README and no page under <c>docs/</c>, and nothing in the app ever sets it.
    ///
    /// It is an environment variable rather than a field the suite assigns because a variable is the
    /// only seam that survives a process boundary. A test that drives the app as a child process —
    /// the generate CLI, a published build — cannot reach a static inside its own process, and that
    /// child is a genuine dir2site, so it would write its recent projects and deploy records into
    /// the real Application Support folder: the failure this whole arrangement exists to prevent,
    /// arriving from a test. For the same reason it must not be gated on <see cref="IsTheApp"/>.
    ///
    /// Redirecting an app's state directory by environment is the ordinary shape of this —
    /// <c>GIT_CONFIG_GLOBAL</c>, <c>XDG_STATE_HOME</c> — and a variable is inspectable in a way a
    /// hidden static is not, which matters on the day someone is working out where their data went.
    ///
    /// A relative value is ignored; see <see cref="ComposeRoot"/>.
    /// </summary>
    public const string RootOverrideVariable = "DIR2SITE_APP_DATA";

    /// <summary>This program's folder, e.g. <c>%AppData%/dir2site</c>.</summary>
    public static string Root { get; } = ComposeRoot(
        Environment.GetEnvironmentVariable(RootOverrideVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    /// <summary>
    /// The rule itself, with its three inputs passed in. <see cref="Root"/> is a static resolved
    /// once per process, so a test cannot re-resolve it under different conditions; this is what
    /// lets one assert that an installed copy — no override, the app's own name — still composes
    /// the exact path every existing user already has their files under.
    /// </summary>
    internal static string ComposeRoot(string? rootOverride, string appDataFolder, string appName) =>
        // Rooted or nothing. A relative value resolves against the process working directory, which
        // for a double-clicked .app is "/" — so a stray DIR2SITE_APP_DATA=data would scatter
        // profiles and credentials somewhere nobody would look for them, and quietly lose the ones
        // already written. Ignoring it leaves the app where it has always been.
        rootOverride is { Length: > 0 } overridden && Path.IsPathRooted(overridden)
            ? overridden
            : Path.Combine(appDataFolder, appName);

    /// <summary>One area within it, e.g. <c>%AppData%/dir2site/ui</c>.</summary>
    public static string Area(string area) => Path.Combine(Root, area);

    /// <summary>
    /// What the OS credential stores file secrets under. The same name again, so a test never
    /// writes into the login keychain entry the installed app is reading.
    /// </summary>
    public static string CredentialService => AppName;

    /// <summary>
    /// True when this is the installed app rather than a test host or a tool. Lets a test assert
    /// that it is not about to touch real state without hard-coding the release name itself.
    /// </summary>
    public static bool IsTheApp => AppName == "dir2site";
}

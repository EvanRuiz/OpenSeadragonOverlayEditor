// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Runtime.CompilerServices;
using dir2site.Services;

namespace dir2site.Tests;

/// <summary>
/// Gives this test run a per-user state directory of its own, in temp, and takes it away again when
/// the run ends.
///
/// Two things make it have to be per-run rather than just "not the app's". Runs are concurrent —
/// two worktrees, or a CI matrix on one machine — and a fixed directory puts them back in the
/// position this whole change is about, only with two test processes instead of a test and the app.
/// And nothing ever deletes it: four runs left 39 files behind, which is how the real store reached
/// 3,950 in the first place. A fixed name under Application Support would move the pile, not stop
/// it growing.
///
/// The redirect goes through an environment variable rather than a field this class sets, and that
/// is deliberate: a variable is inherited by child processes, so a test that runs the app itself —
/// the generate CLI, a published build — isolates that child too. Anything spawned by a run is a
/// real dir2site by name, so without the variable it would write to the developer's own Application
/// Support folder. Do not tidy this into a static, and do not make the app ignore the variable.
///
/// This runs as a module initializer because <see cref="AppDataPaths.Root"/> is a static that
/// resolves the first time anything asks for it. The variable has to be set before that happens,
/// and a module initializer runs when the test assembly loads — before any test, fixture or
/// <c>[ModuleInitializer]</c>-free static in the code under test can get there first.
/// </summary>
internal static class AppDataSandbox
{
    private static string? _directory;

    [ModuleInitializer]
    internal static void Redirect()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "d2s-appdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        Environment.SetEnvironmentVariable(AppDataPaths.RootOverrideVariable, _directory);

        // Best effort: the OS reaps temp anyway, and a run that is killed outright never gets here.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        try
        {
            if (_directory is not null && Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // A file still held open is not worth failing a green run over.
        }
    }
}

// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// The command line as a process, which is the only way to reach what <c>Program.Main</c> does
/// before <c>GenerateCommand.Run</c> is called: the argument dispatch, the exit code the shell
/// sees, and — on Windows — attaching to the parent's console and setting its code page.
/// </summary>
/// <remarks>
/// The rest of the tests call <c>GenerateCommand.Run</c> directly and cannot see any of that. It was
/// verified by hand on macOS and by nothing at all on Windows, where those lines exist precisely
/// because Windows differs.
///
/// Starting a process here is not the CLI-on-PATH smell the other tests avoid: the muxer is derived
/// from the runtime executing this test and the app is the dll the test project references and
/// copies beside itself. Neither is looked up in PATH and neither can be absent.
/// </remarks>
public class CommandLineProcessTests
{
    private static string AppDll => Path.Combine(AppContext.BaseDirectory, "dir2site.dll");

    /// <summary>
    /// The <c>dotnet</c> that is running this test, found from the runtime directory rather than
    /// from <see cref="Environment.ProcessPath"/>.
    /// </summary>
    /// <remarks>
    /// ProcessPath is whatever launched the test host, and that differs by platform: VSTest runs
    /// <c>dotnet exec testhost.dll</c> on Unix, where it is the muxer, but launches a native
    /// <c>testhost.exe</c> on Windows, where it is not — and handing testhost a dll to run gives it
    /// arguments its own entry point rejects. The runtime directory is
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>, so the muxer is three levels
    /// above it, wherever that root happens to be — under mise here, under Program Files on a runner.
    /// </remarks>
    private static string Muxer => Path.GetFullPath(Path.Combine(
        RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    /// <summary>
    /// The app's own executable, beside the dll in the test output. On Windows this is the real
    /// shipped artifact: an apphost built from <c>WinExe</c>, so GUI-subsystem and console-less,
    /// which is the condition <c>AttachToParentConsole</c> exists for.
    /// </summary>
    /// <remarks>
    /// Not used for the ordinary tests. An apphost resolves its framework from the standard install
    /// locations, so it does not start on a machine whose runtime lives somewhere else — mise, here —
    /// while <see cref="Muxer"/> works anywhere. Only the console test needs the real binary, and it
    /// skips rather than fails when the apphost cannot start.
    /// </remarks>
    private static string AppHost => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "dir2site.exe" : "dir2site");

    private static ProcessStartInfo Start(params string[] args)
    {
        var psi = new ProcessStartInfo(Muxer) { UseShellExecute = false };
        psi.ArgumentList.Add(AppDll);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return psi;
    }

    private static ProcessStartInfo StartAppHost(params string[] args)
    {
        var psi = new ProcessStartInfo(AppHost) { UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return psi;
    }

    [Fact]
    public void HelpPrintsUsageAndExitsCleanly()
    {
        var psi = Start("--help");
        psi.RedirectStandardOutput = true;

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "dir2site --help did not exit within a minute.");

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("--generate", output);
    }

    [Fact]
    public void AnUnrecognisedArgumentIsRejectedRatherThanOpeningAWindow()
    {
        var psi = Start("--publish");
        psi.RedirectStandardError = true;

        using var process = Process.Start(psi)!;
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "dir2site --publish did not exit within a minute.");

        // A window would never exit, so the timeout above is half the assertion.
        Assert.Equal(2, process.ExitCode);
        Assert.Contains("--publish", error);
    }

    [Fact]
    public void GeneratingAProjectExitsZeroAndWritesTheSite()
    {
        var root = Path.Combine(Path.GetTempPath(), "d2s-proc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Prints"));
        File.WriteAllText(Path.Combine(root, "Prints", "Plate.jpg"), "not really a jpeg");
        try
        {
            var psi = Start("--generate", root, "--quiet");
            psi.RedirectStandardOutput = true;

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(120_000), "the generate did not finish within two minutes.");

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Site generated", output);
            Assert.True(File.Exists(Path.Combine(root, "_site", "index.html")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The Windows-only half: a GUI-subsystem exe starts with no console, so the command attaches to
    /// its parent's and sets that console's output code page to UTF-8. Without it the arrows and
    /// separators it prints are mangled by whatever code page the shell happened to have.
    /// </summary>
    /// <remarks>
    /// Tested by becoming the parent: this process allocates a console, the child attaches to that
    /// same console, and the code page is then readable from here because the two share it. That is
    /// four P/Invokes and no pipes — the alternative, hosting a real console through ConPTY and
    /// parsing the VT stream back, tests glyph rendering rather than the mechanism, for ten times
    /// the machinery.
    ///
    /// Output is deliberately not redirected: a redirected child has valid inherited handles and no
    /// reason to attach, which is exactly the path this is here to exercise.
    ///
    /// Freeing first and last leaves the host with no console for the rest of the run — a
    /// process-global side effect from one test. It is safe because VSTest reports over a socket
    /// rather than a console, and it is deliberate: the console this allocates must not outlive the
    /// test that made it.
    /// </remarks>
    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void OnWindowsTheChildSetsTheConsoleToUtf8()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Consoles and code pages are a Windows concern.");

        // A test host may or may not already have one; either way we want ours, and we want it gone
        // afterwards, because a console outlives the test that allocated it.
        FreeConsole();
        SkipOrFail(!AllocConsole(), $"Could not allocate a console (error {Marshal.GetLastWin32Error()}).");

        try
        {
            SetConsoleOutputCP(437);   // The code page that mangles what this prints.

            // The apphost, not `dotnet dir2site.dll`: the muxer is console-subsystem, so the child
            // would inherit this console, AttachConsole would fail with ERROR_ACCESS_DENIED for
            // being attached already, and the run would prove nothing about the shipped binary.
            Process? process = null;
            try
            {
                process = Process.Start(StartAppHost("--help"));
            }
            catch (Exception ex)
            {
                SkipOrFail(true, $"The apphost could not start: {ex.Message}");
            }

            // Process.Start is declared to return null, and `using (null)` is legal — so without
            // this the body would dereference it and throw where a sentence belongs.
            Assert.NotNull(process);

            using (process)
            {
                Assert.True(process.WaitForExit(60_000), "dir2site --help did not exit within a minute.");
                SkipOrFail(process.ExitCode == 150, "The apphost could not resolve a framework to run on.");
                Assert.Equal(0, process.ExitCode);
            }

            Assert.Equal(65001u, GetConsoleOutputCP());
        }
        finally
        {
            FreeConsole();
        }
    }

    /// <summary>Set in CI, where this test not running is a hole rather than an environment.</summary>
    internal const string RequireEnvVar = "DIR2SITE_REQUIRE_CONSOLE_TESTS";

    /// <summary>
    /// Skips, or throws when the run has declared this test must execute.
    /// </summary>
    /// <remarks>
    /// Windows is the only platform this test runs on, so every one of its guards — no console to
    /// allocate, an apphost that will not start, an apphost that cannot find a framework — is a way
    /// for the one job that covers this behaviour to report green while covering nothing. Same shape
    /// and same reasoning as <see cref="SftpServerFixture.ReasonOrThrow"/>, which is here for the
    /// same reason. Locally it stays a skip, so an unusual runtime layout does not block anyone.
    /// </remarks>
    internal static void SkipOrFail(bool condition, string reason)
    {
        if (!condition) return;

        if (Environment.GetEnvironmentVariable(RequireEnvVar) == "1")
            throw new InvalidOperationException($"{reason} {RequireEnvVar}=1, so this test must run.");

        Skip.If(true, reason);
    }

    // DllImport rather than LibraryImport, which needs AllowUnsafeBlocks in the test project —
    // and matches how Program.cs declares AttachConsole, the call these are here to check.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);
}

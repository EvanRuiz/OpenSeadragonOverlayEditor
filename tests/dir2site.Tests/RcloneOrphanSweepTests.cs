// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace dir2site.Tests;

/// <summary>
/// The sweep kills processes it did not start, on the strength of a number in a file, so what it
/// refuses to kill matters more than what it does.
///
/// A process id is reused as soon as its process is gone. A record left by a run that was killed
/// last Tuesday names a number that today belongs to whatever the developer happens to be running —
/// an editor, a build, a database. "The pid is alive" is therefore not evidence that the pid is the
/// server; the sweep has to look at what the process actually is.
/// </summary>
[Collection("rclone-orphans")]
public sealed class RcloneOrphanSweepTests : IDisposable
{
    private readonly string _cache = RcloneTool.CacheDirectory;
    private readonly string _record;

    /// <summary>A pid whose process has certainly exited, so the record reads as an orphan's.</summary>
    private readonly int _deadOwner;

    public RcloneOrphanSweepTests()
    {
        Directory.CreateDirectory(_cache);

        using var brief = StartSleeper(seconds: 0);
        brief.WaitForExit();
        _deadOwner = brief.Id;

        _record = Path.Combine(_cache, $"serve-{_deadOwner}.pids");
    }

    public void Dispose()
    {
        try { if (File.Exists(_record)) File.Delete(_record); } catch { }
    }

    /// <summary>
    /// A real live process that is definitely not rclone, standing in for a reused pid.
    ///
    /// Windows waits with <c>ping</c> rather than <c>timeout</c>: <c>timeout.exe</c> reads the
    /// console so it can offer "press any key", and prints "Input redirection is not supported"
    /// and exits at once when its input is redirected — which it always is under a test host. That
    /// would hand back a corpse where these tests need something alive, and as a race between exit
    /// and assert it would fail flakily rather than honestly. The process is still <c>cmd</c>, so
    /// the "not rclone" name check reads the same.
    /// </summary>
    private static Process StartSleeper(int seconds)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c ping -n {seconds + 1} 127.0.0.1")
            : new ProcessStartInfo("sleep", seconds.ToString());
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        return Process.Start(psi)!;
    }

    [Fact]
    public void ItLeavesAProcessThatIsNotRcloneAlone()
    {
        using var bystander = StartSleeper(seconds: 30);
        try
        {
            File.WriteAllText(_record, bystander.Id + Environment.NewLine);

            RcloneOrphans.SweepForTest();

            Assert.False(
                bystander.HasExited,
                "the sweep killed a live process that was not rclone — a reused pid in a stale "
                + "record is somebody else's program");
        }
        finally
        {
            try { bystander.Kill(entireProcessTree: true); } catch { }
        }
    }

    /// <summary>
    /// The file goes either way. Left behind, its owner stays dead forever and every later run
    /// re-reads a number that means less each time.
    /// </summary>
    [Fact]
    public void ItClearsTheRecordEvenWhenItKillsNothing()
    {
        using var bystander = StartSleeper(seconds: 30);
        try
        {
            File.WriteAllText(_record, bystander.Id + Environment.NewLine);

            RcloneOrphans.SweepForTest();

            Assert.False(File.Exists(_record));
        }
        finally
        {
            try { bystander.Kill(entireProcessTree: true); } catch { }
        }
    }

    /// <summary>
    /// This run's own record is not swept. xUnit builds several fixtures at once, so a live server
    /// of ours is routinely recorded while another fixture is starting — and that is exactly when
    /// the sweep runs.
    /// </summary>
    [Fact]
    public void ItDoesNotTouchARecordBelongingToALiveRun()
    {
        // A live owner that is not this process. Writing to serve-<our own pid>.pids would mean
        // overwriting the record a fixture starting in parallel is keeping its server in — losing
        // that pid is how an orphan gets made, which is the opposite of the point.
        using var liveOwner = StartSleeper(seconds: 30);
        var theirs = Path.Combine(_cache, $"serve-{liveOwner.Id}.pids");

        try
        {
            File.WriteAllText(theirs, "999999" + Environment.NewLine);

            RcloneOrphans.SweepForTest();

            Assert.True(File.Exists(theirs), "the sweep cleared a record whose owner is still alive");
        }
        finally
        {
            try { File.Delete(theirs); } catch { }
            try { liveOwner.Kill(entireProcessTree: true); } catch { }
        }
    }

    /// <summary>
    /// The residue the name check cannot reach: the pid was reissued to another rclone.
    ///
    /// A record names pid 1234; that process and its owner die; the OS hands 1234 to the developer's
    /// own <c>rclone</c> transfer. A sweep that asks only "is it alive and called rclone" says yes
    /// and kills their sync mid-flight. The recorded start time is what separates them, so this
    /// records a start time that is deliberately wrong — a stand-in for the earlier process whose
    /// number this now is — and expects the live one to be left alone.
    ///
    /// The stand-in is a copy of the real rclone, because the check is on the process's name; using
    /// <c>sleep</c> here would pass for the wrong reason.
    /// </summary>
    [SkippableFact]
    public void ItLeavesARcloneThatMerelyInheritedTheRecordedPid()
    {
        var rclone = RcloneTool.Resolve(out var reason);
        Skip.If(rclone is null, reason);

        using var stranger = StartSleeperNamed(rclone!);
        try
        {
            Assert.StartsWith("rclone", stranger.ProcessName, StringComparison.OrdinalIgnoreCase);

            // Its pid, with a start time from an hour before it existed.
            var wrongStart = DateTime.UtcNow.AddHours(-1).Ticks;
            File.WriteAllText(_record, $"{stranger.Id} {wrongStart}{Environment.NewLine}");

            RcloneOrphans.SweepForTest();

            Assert.False(
                stranger.HasExited,
                "the sweep killed a live rclone that had merely been given the recorded pid — the "
                + "recorded start time is what tells it from the server we started");
        }
        finally
        {
            try { stranger.Kill(entireProcessTree: true); } catch { }
        }
    }

    /// <summary>
    /// And the other half: a record whose start time matches is ours, and is killed. Without this
    /// the check above would pass just as well if the sweep had stopped killing anything at all.
    /// </summary>
    [SkippableFact]
    public void ItStillKillsTheRcloneItActuallyRecorded()
    {
        var rclone = RcloneTool.Resolve(out var reason);
        Skip.If(rclone is null, reason);

        var ours = StartSleeperNamed(rclone!);
        try
        {
            var started = ours.StartTime.ToUniversalTime().Ticks;
            File.WriteAllText(_record, $"{ours.Id} {started}{Environment.NewLine}");

            RcloneOrphans.SweepForTest();

            Assert.True(ours.WaitForExit(10_000), "the recorded server was not killed");
        }
        finally
        {
            try { if (!ours.HasExited) ours.Kill(entireProcessTree: true); } catch { }
            ours.Dispose();
        }
    }

    /// <summary>
    /// A real rclone process that stays up without needing a port: it reads from a pipe nobody
    /// writes to. Named rclone, which is the whole point of using the binary rather than a stub.
    /// </summary>
    private static Process StartSleeperNamed(string rclonePath)
    {
        var psi = new ProcessStartInfo(rclonePath, "rcat --retries 1 /dev/null")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi)!;
    }

    /// <summary>
    /// A record naming a pid that is simply gone — the ordinary case, where the orphan died on its
    /// own before anyone swept — is cleared without incident.
    /// </summary>
    [Fact]
    public void ItClearsARecordWhosePidNoLongerExists()
    {
        using var gone = StartSleeper(seconds: 0);
        gone.WaitForExit();

        File.WriteAllText(_record, gone.Id + Environment.NewLine);

        RcloneOrphans.SweepForTest();

        Assert.False(File.Exists(_record));
    }
}

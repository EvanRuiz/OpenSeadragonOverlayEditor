// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace dir2site.Tests;

/// <summary>
/// Stops a killed test run from leaving its <c>rclone serve sftp</c> behind.
///
/// <see cref="SftpServerFixture"/> kills the server it started when it is disposed, which covers
/// every run that ends normally. A run that is killed outright — Ctrl-C, an IDE stop button, an
/// agent timing out — never disposes anything, and the server it started is reparented and keeps
/// going with nothing left that knows about it. One was found still serving at 300% CPU long after
/// the run that started it had gone.
///
/// So each fixture records its server's process id in the rclone cache directory, next to the
/// binary, and removes it on a clean dispose. The next run sweeps what is left.
///
/// The record is filed under the id of the <em>test host</em> that owns it, and this run's own file
/// is skipped. A file whose owner is still running is somebody's business; a file whose owner is
/// gone belongs to nobody, and that is precisely an orphan.
///
/// Nothing today depends on that skip. The production sweep is latched and called once, from
/// <see cref="SftpServerFixture"/> before it starts anything, so it runs when this host has
/// recorded nothing — and with class fixtures and parallelism off, only one server is ever up.
/// The guard is for <see cref="SweepForTest"/>, which is not latched and is called repeatedly with
/// records already on disk, and for the day the latch moves or two hosts share a cache directory.
/// A sweep that is safe only because of where it happens to be called is one refactor away from
/// killing our own server.
/// </summary>
internal static class RcloneOrphans
{
    private static readonly object Gate = new();
    private static bool _swept;

    /// <summary>Beside the cached binary, so it shares that directory's lifetime and .gitignore.</summary>
    private static string Directory => RcloneTool.CacheDirectory;

    private static string FileFor(int ownerProcessId) =>
        Path.Combine(Directory, $"serve-{ownerProcessId}.pids");

    /// <summary>
    /// Kills any server left behind by a run that is no longer alive. Runs once per test host,
    /// before the first server is started.
    /// </summary>
    internal static void SweepOnce()
    {
        lock (Gate)
        {
            if (_swept) return;
            _swept = true;

            try { Sweep(); }
            catch { /* Never let tidying up stop the tests that need the server. */ }
        }
    }

    /// <summary>
    /// The sweep on its own, without the once-per-run latch. Only for the tests that cover what it
    /// will and will not kill — the behaviour worth pinning, since it sends signals to processes it
    /// did not start.
    /// </summary>
    internal static void SweepForTest() => Sweep();

    private static void Sweep()
    {
        if (!System.IO.Directory.Exists(Directory)) return;

        foreach (var file in System.IO.Directory.GetFiles(Directory, "serve-*.pids"))
        {
            var owner = OwnerOf(file);

            // Our own file, or another live run's. Its servers are in use.
            if (owner is null || owner == Environment.ProcessId || IsAlive(owner.Value)) continue;

            foreach (var (pid, started) in ReadRecords(file)) KillIfStillOurs(pid, started);

            try { File.Delete(file); } catch { }
        }
    }

    /// <summary>The owning test host's id, from the file name. Null if it isn't one of ours.</summary>
    private static int? OwnerOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.StartsWith("serve-", StringComparison.Ordinal)
            && int.TryParse(name["serve-".Length..], out var pid)
                ? pid
                : null;
    }

    internal static void Remember(int pid)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                // The start time is what tells our server from a stranger that inherited its number.
                // A pid alone is not an identity: it is reissued the moment the process ends, and a
                // record read tomorrow may name the developer's own rclone transfer.
                var started = StartTicks(pid);
                var line = started is null ? pid.ToString() : $"{pid} {started}";

                File.AppendAllText(FileFor(Environment.ProcessId), line + Environment.NewLine);
            }
            catch { }
        }
    }

    /// <summary>When a process began, or null if it cannot be asked.</summary>
    private static long? StartTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch { return null; }
    }

    /// <summary>Drops one server from this run's record, removing the file once it is empty.</summary>
    internal static void Forget(int pid)
    {
        lock (Gate)
        {
            try
            {
                var file = FileFor(Environment.ProcessId);
                if (!File.Exists(file)) return;

                var remaining = ReadRecords(file).Where(r => r.Pid != pid).ToArray();
                if (remaining.Length == 0) File.Delete(file);
                else File.WriteAllLines(
                    file, remaining.Select(r => r.Started is null ? $"{r.Pid}" : $"{r.Pid} {r.Started}"));
            }
            catch { }
        }
    }

    /// <summary>One recorded server: its pid, and when that process began if we could read it.</summary>
    private static IEnumerable<(int Pid, long? Started)> ReadRecords(string file)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch { yield break; }

        foreach (var line in lines)
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], out var pid)) continue;

            long? started = parts.Length > 1 && long.TryParse(parts[1], out var ticks) ? ticks : null;
            yield return (pid, started);
        }
    }

    private static bool IsAlive(int pid)
    {
        try { using var _ = Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Kills the process only if it is still the one that was recorded.
    ///
    /// The name alone separates rclone from everything else, but not our server from a transfer the
    /// developer started: a stale record naming pid 1234 says nothing once 1234 has been reissued,
    /// and reissuing it to another rclone is exactly the case where a name check agrees. The start
    /// time settles it — a reused pid always began later than the one that was written down.
    ///
    /// A record with no start time is one written before this was recorded, or one whose process
    /// could not be asked. Those fall back to the name, which is where this began: no worse than
    /// leaving a server at 300% CPU, and it applies to nothing written from now on.
    /// </summary>
    private static void KillIfStillOurs(int pid, long? recordedStart)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            if (!process.ProcessName.StartsWith("rclone", StringComparison.OrdinalIgnoreCase))
                return;

            if (recordedStart is not null)
            {
                var actual = process.StartTime.ToUniversalTime().Ticks;

                // A second of slack: the same process read twice, not a different one. Anything
                // that inherited this pid started after the recorded server exited.
                if (Math.Abs(actual - recordedStart.Value) > TimeSpan.FromSeconds(1).Ticks)
                    return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch
        {
            // Already gone, or not ours to kill.
        }
    }
}

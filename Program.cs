// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
﻿using Avalonia;
using Avalonia.Headless;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using dir2site.Services;
using Velopack;

namespace dir2site;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        // Velopack's install/update hooks are arguments too, and it exits the process for its own.
        VelopackApp.Build().Run();

        var options = CommandLine.Parse(args);
        if (options.Mode != CommandLineMode.Gui)
        {
            Environment.Exit(RunCommandLine(options));
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Everything the process does when it was started with arguments rather than double-clicked.
    /// </summary>
    private static int RunCommandLine(CommandLineOptions options)
    {
        AttachToParentConsole();

        switch (options.Mode)
        {
            case CommandLineMode.Help:
                Console.WriteLine(CommandLine.Usage);
                return GenerateCommand.Success;

            case CommandLineMode.Version:
                Console.WriteLine(Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? "unknown");
                return GenerateCommand.Success;

            case CommandLineMode.Invalid:
                Console.Error.WriteLine($"dir2site: {options.Error}");
                Console.Error.WriteLine();
                Console.Error.WriteLine(CommandLine.Usage);
                return GenerateCommand.BadArguments;

            case CommandLineMode.Generate:
                // Headless rather than the desktop platform: the generator reads its page templates
                // through Avalonia's asset system and the article thumbnails need its bundled font,
                // so Avalonia has to be initialised — but no window is ever shown, and this is the
                // one platform that also works on a build agent with no display.
                BuildAvaloniaApp()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();

                return GenerateCommand.Run(
                    options.ProjectFolder!, options.Quiet, Console.Out, Console.Error);

            default:
                // Gui never reaches here — Main handles it before calling — and a mode added later
                // lands on this rather than on the generate arm, whose folder it would not have set.
                Console.Error.WriteLine($"dir2site: {options.Mode} is not something this can run.");
                return GenerateCommand.BadArguments;
        }
    }

    /// <summary>
    /// Reattaches stdout/stderr to the terminal that launched us on Windows, where a GUI subsystem
    /// executable starts with no console and every write would otherwise go nowhere. A no-op
    /// elsewhere, and harmless when there is no parent console to attach to.
    /// </summary>
    private static void AttachToParentConsole()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            const uint attachParentProcess = 0xFFFFFFFF;
            if (!AttachConsole(attachParentProcess)) return;

            // Every line this prints carries characters outside code page 437/1252 — the arrow in
            // "Site generated → _site/", the separators in the counters, the © in a default footer.
            // Setting this sets the console's output code page as well, so the bytes written and the
            // bytes read agree; without it cmd.exe renders them as mojibake.
            Console.OutputEncoding = Encoding.UTF8;

            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch
        {
            // Console output is a convenience; losing it must not cost the exit code.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "dir2site-crash.txt"),
                $"[{DateTime.Now}] Unhandled exception (terminating={e.IsTerminating}):\n{e.ExceptionObject}\n");
        }
        catch { }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "dir2site-crash.txt"),
                $"[{DateTime.Now}] Unobserved task exception:\n{e.Exception}\n\n");
            e.SetObserved();
        }
        catch { }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
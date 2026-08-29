// SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;

namespace dir2site.Services;

/// <summary>What the command line asked the process to do.</summary>
public enum CommandLineMode
{
    /// <summary>No recognised switch — open the window, which is what a double-click does.</summary>
    Gui,
    /// <summary>Generate a project's site and exit.</summary>
    Generate,
    /// <summary>Print usage and exit successfully.</summary>
    Help,
    /// <summary>Print the version and exit successfully.</summary>
    Version,
    /// <summary>The arguments don't parse; <see cref="CommandLineOptions.Error"/> says why.</summary>
    Invalid,
}

/// <param name="Mode">What to do.</param>
/// <param name="ProjectFolder">The folder to generate, absolute, for <see cref="CommandLineMode.Generate"/>.</param>
/// <param name="Quiet">Suppress the per-file progress lines; the summary is still printed.</param>
/// <param name="ForceClean">
/// Remove what the run would otherwise only list — leftover yamls, and files in <c>_site</c> with no
/// source. Off by default, because both are things this app declines to decide about on its own.
/// </param>
/// <param name="Error">Why the arguments were rejected, for <see cref="CommandLineMode.Invalid"/>.</param>
public sealed record CommandLineOptions(
    CommandLineMode Mode,
    string? ProjectFolder = null,
    bool Quiet = false,
    string? Error = null,
    bool ForceClean = false);

/// <summary>
/// Parses the process arguments. Pure and total: it never touches the filesystem beyond resolving
/// a relative path, and never exits — so the whole surface can be tested without running anything.
/// </summary>
/// <remarks>
/// Deliberately hand-rolled and tiny. The app is a GUI first: the command line exists so a site can
/// be regenerated from a script or from CI, not to grow into a second interface to every setting.
/// </remarks>
public static class CommandLine
{
    public const string Usage = """
        dir2site — turn a folder into a static site

        Usage:
          dir2site                          open the app
          dir2site --generate <folder>      generate <folder>/_site and exit
          dir2site --help | --version

        Options for --generate:
          -q, --quiet                       print only the summary, not every file
              --force-clean                 also remove what the run would only list: yaml
                                            files whose artifact has gone, and files in
                                            _site with no source left. Says "force" because
                                            it deletes files in your project folder without
                                            asking, including ones dir2site never wrote.
                                            A folder dir2site has never generated has no
                                            leftovers by definition, so on a fresh clone
                                            this removes nothing until the first generate.

        The folder's dir2site.yaml supplies the title, colours and other settings; a folder
        without one is generated with the defaults the app would have shown for it.

        Exit codes: 0 success · 1 generate reported errors · 2 bad arguments, or a project
        folder it cannot use.
        """;

    public static CommandLineOptions Parse(string[] args)
    {
        if (args.Length == 0) return new CommandLineOptions(CommandLineMode.Gui);

        string? folder = null;
        var quiet = false;
        var forceClean = false;
        var generate = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    return new CommandLineOptions(CommandLineMode.Help);

                case "--version" or "-v":
                    return new CommandLineOptions(CommandLineMode.Version);

                case "--generate" or "-g":
                    generate = true;
                    // The folder may follow as its own argument, or be attached with '='.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        folder = args[++i];
                    break;

                case "--quiet" or "-q":
                    quiet = true;
                    break;

                case "--force-clean":
                    forceClean = true;
                    break;

                default:
                    if (arg.StartsWith("--generate=", StringComparison.Ordinal))
                    {
                        generate = true;
                        folder = arg["--generate=".Length..];
                        break;
                    }

                    // A bare path alongside --generate is the folder; on its own it is a mistake
                    // worth naming, since silently opening the window would look like a hang.
                    if (!arg.StartsWith('-') && generate && folder == null)
                    {
                        folder = arg;
                        break;
                    }

                    return new CommandLineOptions(CommandLineMode.Invalid,
                        Error: $"Unrecognised argument '{arg}'.");
            }
        }

        // Velopack's own hooks (--squirrel-install and friends) never reach here: they are handled
        // before parsing, in Program.Main.
        if (!generate)
            return new CommandLineOptions(CommandLineMode.Invalid,
                Error: "Nothing to do — did you mean --generate <folder>?");

        if (string.IsNullOrWhiteSpace(folder))
            return new CommandLineOptions(CommandLineMode.Invalid,
                Error: "--generate needs the project folder to generate.");

        return new CommandLineOptions(
            CommandLineMode.Generate, Path.GetFullPath(folder), quiet, ForceClean: forceClean);
    }
}

using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.Launcher.Programs;
using ViennaDotNet.Launcher.Utils;

namespace ViennaDotNet.Launcher;

internal sealed class LauncherWindow : Window
{
    private static readonly string[] expectedStaticFiles = [
        "catalog/itemEfficiencyCategories.json",
        "catalog/itemJournalGroups.json",
        "catalog/items.json",
        "catalog/nfc.json",
        "catalog/recipes.json",
        "catalog/recipes.json",
        "tile_renderer/tagMap.json",
    ];

    private static readonly string[] expectedStaticDirectories = [
        "catalog",
        "encounters",
        "levels",
        "resourcepacks",
        "tappables",
        "tile_renderer",
    ];

    private static Settings settings => Program.Settings;

    public LauncherWindow()
    {
        Title = "ViennaDotNet Launcher";

        var startBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Absolute(1),
            Text = "_Start",
        };
        startBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Start(settings);
        };

        var stopBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(startBtn) + 1,
            Text = "_Stop",
        };
        stopBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Stop();
        };

        var optionsBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(stopBtn) + 1,
            Text = "_Options",
        };
        optionsBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            using var options = new OptionsWindow(settings)
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                //Modal = true,
            };

            Application.Run(options);

            settings.Save(Program.SettingsFile);
        };

        var importBuildplateBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(optionsBtn) + 1,
            Text = "_Import buildplate",
        };
        importBuildplateBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            using var importBuildplate = new ImportBuildplateWindow(settings)
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                //Modal = true,
            };

            Application.Run(importBuildplate);
        };

        var dataBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(importBuildplateBtn) + 1,
            Text = "_Modify data",
        };

        var exitBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(dataBtn) + 1,
            Text = "_Exit",
        };
        exitBtn.Accepting += (s, e) =>
        {
            Application.RequestStop();

            e.Handled = true;
        };

        Add(startBtn, stopBtn, optionsBtn, importBuildplateBtn, dataBtn, exitBtn);
    }

    private void Start(Settings settings) 
        => UIUtils.RunWithLogs(this, async (logger, cancellationToken) =>
        {
            if (settings.SkipFileChecks is not true)
            {
                Check(settings, logger);
            }
            else
            {
                logger.Warning("Skipped file validation, you can turn it back on in 'Configure/Skip file validation before starting'");
            }

            cancellationToken.ThrowIfCancellationRequested();

            EventBusServer.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectStoreServer.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            ApiServer.Run(settings, logger);

            await Task.Delay(1000, cancellationToken); // wait a bit for them to start
        });

    private void Stop()
    {
         int selected = MessageBox.Query("Confirm", "Are you sure you want to stop all currently runnning server instances?", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ["OK", "Cancel"] : ["Cancel", "OK"]);

        if (selected != (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 0 : 1))
        {
            return;
        }

        UIUtils.RunWithLogs(this, async (logger, cancellationToken) =>
            {
                foreach (string programName in (IEnumerable<string>)[ApiServer.ExeName, ObjectStoreServer.ExeName, EventBusServer.ExeName])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await StopProgram(programName, logger, cancellationToken);
                }
            });
    }

    private static async Task StopProgram(string name, ILogger logger, CancellationToken cancellationToken)
    {
        logger.Information($"Stopping {name}");

        string exePath = Path.GetFullPath(name);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Debug.Assert(name.EndsWith(".exe", StringComparison.Ordinal));
            name = name[..^4];
        }

        int stoppedCount = 0;
        foreach (var proc in Process.GetProcessesByName(name))
        {
            if (proc.MainModule is null || proc.MainModule.FileName != exePath)
            {
                continue;
            }

            await proc.StopGracefullyOrKillAsync(3000, cancellationToken);
            stoppedCount++;
        }

        logger.Information(stoppedCount switch
        {
            0 => $"No {name} processes found",
            1 => $"Stopped 1 {name} process",
            _ => $"Stopped {stoppedCount} {name} processes",
        });
    }

    private static void Check(Settings settings, ILogger logger)
    {
        Debug.Assert(settings.SkipFileChecks is not true);

        bool notFound = false;
        if (!EventBusServer.Check(settings, logger) ||
            !ObjectStoreServer.Check(settings, logger) ||
            !ApiServer.Check(settings, logger))
        {
            notFound = true;
        }

        foreach (string dir in expectedStaticDirectories)
        {
            string fullDir = Path.GetFullPath(Path.Combine(Program.StaticDataDir, dir));

            if (!Directory.Exists(fullDir))
            {
                logger.Error($"Static data directory '{fullDir}' does not exist");
                notFound = true;
            }
        }

        foreach (string file in expectedStaticFiles)
        {
            string fullFile = Path.GetFullPath(Path.Combine(Program.StaticDataDir, file));

            if (!File.Exists(fullFile))
            {
                logger.Error($"Static data file '{fullFile}' does not exist");
                notFound = true;
            }
        }

        string resourcePackPath = Path.GetFullPath(Path.Combine(Program.StaticDataDir, "resourcepacks", "vanilla.zip"));
        if (!File.Exists(resourcePackPath))
        {
            logger.Error($"Resourcepack file '{resourcePackPath}' does not exist");
            logger.Information("Download it from https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35 (using internet archive)");
            logger.Information($"Rename it to vanilla.zip and move it to: {Path.GetFullPath(Path.Combine(Program.StaticDataDir, "resourcepacks"))}");

            notFound = true;
        }

        if (notFound)
        {
            throw new Exception("File validation failed.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ViennaDotNet.Launcher;

internal sealed class LauncherWindow : Window
{
    private static Settings settings => Program.Settings;

    public LauncherWindow()
    {
        Title = "ViennaDotNet Launcher";

        var startBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Absolute(1),
            Text = "Start",
        };

        var optionsBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(startBtn) + 1,
            Text = "Options",
        };
        optionsBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            var options = new OptionsWindow(settings)
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
            Text = "Import buildplate",
        };
        importBuildplateBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            var importBuildplate = new ImportBuildplateWindow(settings)
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
            Text = "Modify data",
        };

        var exitBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(dataBtn) + 1,
            Text = "Exit",
        };
        exitBtn.Accepting += (s, e) =>
        {
            Application.RequestStop();

            e.Handled = true;
        };

        Add(startBtn, optionsBtn, importBuildplateBtn, dataBtn, exitBtn);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ViennaDotNet.Launcher;

internal sealed class ImportBuildplateWindow : Window
{
    public ImportBuildplateWindow(Settings settings)
    {
        const int ChoiceButtonsGroup = 0;

        Title = "Import buidplate";

        var idLabel = new Label()
        {
            Text = "Player ID:",
        };

        var idInput = new TextField()
        {
            X = Pos.Right(idLabel) + 1,
            Y = Pos.Y(idLabel),
            Width = Dim.Fill(),
        };

        var worldFileLabel = new Label()
        {
            Text = "Buildplate file:",
            X = Pos.X(idLabel),
            Y = Pos.Bottom(idLabel) + 1,
        };
        var worldFileInput = new Button
        {
            Text = "Not selected",
            X = Pos.Right(worldFileLabel) + 1,
            Y = Pos.Y(worldFileLabel),
        };
        worldFileInput.Accepting += (s, e) =>
        {
            var dialog = new OpenDialog()
            {
                AllowedTypes = [new AllowedType("Vienna", ".zip"), new AllowedType("Project Earth", ".json")],
                AllowsMultipleSelection = false,
                MustExist = true,
                ShadowStyle = ShadowStyle.Transparent,
                OpenMode = OpenMode.File,
            };

            Application.Run(dialog);

            string? file = dialog.FilePaths.FirstOrDefault();

            if (string.IsNullOrEmpty(file))
            {
                worldFileInput.Text = "Not selected";
            }
            else
            {
                worldFileInput.Text = file;
            }

            e.Handled = true;
        };

        var cancelBtn = new Button()
        {
            Text = "Cancel",
            X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems, ChoiceButtonsGroup),
            Y = Pos.AnchorEnd(),
        };
        cancelBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Application.RequestStop();
        };

        var importBtn = new Button()
        {
            Text = "Import",
            X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems, ChoiceButtonsGroup),
            Y = Pos.AnchorEnd(),
        };

        Add(idLabel, idInput, worldFileLabel, worldFileInput, cancelBtn, importBtn);
    }
}

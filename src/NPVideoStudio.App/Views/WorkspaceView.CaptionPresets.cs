using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using NPVideoStudio.App.ViewModels;

namespace NPVideoStudio.App.Views;

/// <summary>
/// Adds a real preset picker to the existing caption toolbar without duplicating the workspace XAML.
/// It is injected after DataContext is attached, then talks directly to TimelineViewModel's undo-safe
/// preset application method. The row is created once per WorkspaceView and refreshed if its project
/// DataContext changes.
/// </summary>
public partial class WorkspaceView
{
    private ComboBox? _captionPresetCombo;
    private TextBlock? _captionPresetStatus;

    static WorkspaceView()
    {
        DataContextProperty.Changed.AddClassHandler<WorkspaceView>((view, _) =>
            Dispatcher.UIThread.Post(view.EnsureCaptionPresetToolbar));
    }

    private void EnsureCaptionPresetToolbar()
    {
        if (CaptionToolbar.Child is not StackPanel toolbarRoot)
        {
            return;
        }

        if (_captionPresetCombo is null)
        {
            var row = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };

            row.Children.Add(new TextBlock
            {
                Text = "Stil titla:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 6)
            });

            _captionPresetCombo = new ComboBox
            {
                Width = 300,
                Margin = new Thickness(0, 0, 8, 6),
                PlaceholderText = "Izaberi gotov stil"
            };
            _captionPresetCombo.SelectionChanged += (_, _) =>
            {
                if (DataContext is WorkspaceViewModel workspace &&
                    _captionPresetCombo.SelectedItem is TimelineViewModel.CaptionPresetChoice choice)
                {
                    workspace.Timeline.SelectedCaptionPresetChoice = choice;
                }
            };
            row.Children.Add(_captionPresetCombo);

            var applyButton = new Button
            {
                Content = "PRIMENI STIL NA IZABRANI TITL",
                Margin = new Thickness(0, 0, 8, 6)
            };
            applyButton.Classes.Add("cta");
            applyButton.Click += (_, _) =>
            {
                if (DataContext is not WorkspaceViewModel workspace)
                {
                    return;
                }

                workspace.Timeline.ApplySelectedCaptionPreset();
                if (_captionPresetStatus is not null)
                {
                    _captionPresetStatus.Text = workspace.Timeline.CaptionPresetStatusMessage;
                }
            };
            row.Children.Add(applyButton);

            _captionPresetStatus = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 620,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _captionPresetStatus.Classes.Add("subtle");
            row.Children.Add(_captionPresetStatus);

            toolbarRoot.Children.Add(row);
        }

        if (DataContext is WorkspaceViewModel vm)
        {
            _captionPresetCombo.ItemsSource = vm.Timeline.CaptionPresetChoices;
            _captionPresetCombo.SelectedItem = vm.Timeline.SelectedCaptionPresetChoice;
            if (_captionPresetStatus is not null)
            {
                _captionPresetStatus.Text = vm.Timeline.CaptionPresetStatusMessage;
            }
        }
    }
}

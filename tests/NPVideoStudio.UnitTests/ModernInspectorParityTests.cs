using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using NPVideoStudio.App.Views;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Regression test for the modern-editor rewrite: these controls already had real session/render
/// backends, but survived only inside a legacy clip card with IsVisible=False. A green test means the
/// normal selected-clip inspector exposes those existing capabilities again.
/// </summary>
public class ModernInspectorParityTests
{
    [AvaloniaFact]
    public void SelectedClipInspector_ContainsPreviouslyHiddenWorkingControls()
    {
        var view = new WorkspaceView();
        var window = new Window { Width = 1600, Height = 1000, Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<Button>("InspectorDuplicateButton"));
        Assert.NotNull(view.FindControl<ToggleButton>("InspectorMuteToggle"));
        Assert.NotNull(view.FindControl<ToggleButton>("InspectorFadeInToggle"));
        Assert.NotNull(view.FindControl<ToggleButton>("InspectorFadeOutToggle"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorPipScale"));
        Assert.NotNull(view.FindControl<Slider>("InspectorPipX"));
        Assert.NotNull(view.FindControl<Slider>("InspectorPipY"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorPipOpacity"));
        Assert.NotNull(view.FindControl<ComboBox>("InspectorTransitionType"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorTransitionDuration"));
        Assert.NotNull(view.FindControl<TextBox>("InspectorOutlineColor"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorOutlineWidth"));
        Assert.NotNull(view.FindControl<TextBox>("InspectorShadowColor"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorShadowOffset"));
        Assert.NotNull(view.FindControl<TextBox>("InspectorBackgroundColor"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorBackgroundOpacity"));
        Assert.NotNull(view.FindControl<ComboBox>("InspectorTextCase"));
        Assert.NotNull(view.FindControl<NumericUpDown>("InspectorLineSpacing"));

        window.Close();
    }
}

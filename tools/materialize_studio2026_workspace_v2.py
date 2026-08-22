from pathlib import Path

# Activate the modern media library and timeline in the same runtime replacement path as the header/inspector.
p = Path('src/NPVideoStudio.App/Views/WorkspaceView.axaml.cs')
s = p.read_text(encoding='utf-8')
old = '''        ProjectHeader.Child = new ModernWorkspaceHeaderView();
        CaptionToolbar.Child = new ModernWorkspaceCommandBarView();
        InspectorPanel.Child = new ModernInspectorView();'''
new = '''        ProjectHeader.Child = new ModernWorkspaceHeaderView();
        CaptionToolbar.Child = new ModernWorkspaceCommandBarView();
        MediaLibraryPanel.Child = new ModernMediaLibraryView();
        InspectorPanel.Child = new ModernInspectorView();
        TimelinePanel.Child = new ModernTimelineView();'''
if old not in s:
    raise SystemExit('Workspace modern chrome anchor missing')
s = s.replace(old, new, 1)
p.write_text(s, encoding='utf-8')

# Make Studio2026 an explicit theme resource mapping instead of relying on the modernized legacy fallback.
p = Path('src/NPVideoStudio.App/App.axaml.cs')
s = p.read_text(encoding='utf-8')
old = '''        var fileName = theme switch
        {
            AppTheme.DarkCinematic => "DarkCinematic",'''
new = '''        var fileName = theme switch
        {
            AppTheme.Studio2026 => "Studio2026",
            AppTheme.DarkCinematic => "DarkCinematic",'''
if old not in s:
    raise SystemExit('ApplyTheme switch anchor missing')
s = s.replace(old, new, 1)
p.write_text(s, encoding='utf-8')

# Extend contract coverage to the new runtime panels and explicit theme mapping.
p = Path('tests/NPVideoStudio.UnitTests/Studio2026UiContractTests.cs')
s = p.read_text(encoding='utf-8')
s = s.replace(
    '        Assert.Contains("CaptionToolbar.Child = new ModernWorkspaceCommandBarView()", code);\n        Assert.Contains("InspectorPanel.Child = new ModernInspectorView()", code);',
    '        Assert.Contains("CaptionToolbar.Child = new ModernWorkspaceCommandBarView()", code);\n        Assert.Contains("MediaLibraryPanel.Child = new ModernMediaLibraryView()", code);\n        Assert.Contains("InspectorPanel.Child = new ModernInspectorView()", code);\n        Assert.Contains("TimelinePanel.Child = new ModernTimelineView()", code);', 1)
s = s.replace(
    '        Assert.NotNull(new ModernWorkspaceCommandBarView());\n        Assert.NotNull(new ModernInspectorView());',
    '        Assert.NotNull(new ModernWorkspaceCommandBarView());\n        Assert.NotNull(new ModernMediaLibraryView());\n        Assert.NotNull(new ModernInspectorView());\n        Assert.NotNull(new ModernTimelineView());', 1)
insert = '''\n    [Fact]\n    public void Studio2026Theme_IsExplicitlyMappedAndTimelineCommandsAreGrouped()\n    {\n        var root = FindRepositoryRoot();\n        var app = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "App.axaml.cs"));\n        var timeline = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernTimelineView.axaml"));\n        var media = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernMediaLibraryView.axaml"));\n        Assert.Contains("AppTheme.Studio2026 => \\"Studio2026\\"", app);\n        foreach (var marker in new[] { "DODAJ", "+ Traka", "PRIKAZ", "AppendSelectedVideoCommand", "AddTextAtPlayheadCommand", "ZoomPixelsPerSecond" })\n            Assert.Contains(marker, timeline);\n        Assert.Contains("ImportMediaCommand", media);\n        Assert.Contains("GenerateProxyCommand", media);\n        Assert.Contains("RemoveCommand", media);\n    }\n'''
anchor = '\n    private static string FindRepositoryRoot()'
if insert.strip() not in s:
    if anchor not in s:
        raise SystemExit('Contract test anchor missing')
    s = s.replace(anchor, insert + anchor, 1)
p.write_text(s, encoding='utf-8')

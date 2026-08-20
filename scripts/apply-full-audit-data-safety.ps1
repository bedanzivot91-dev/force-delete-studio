$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) { [System.IO.File]::ReadAllText((Resolve-Path $Path), [Text.Encoding]::UTF8) }
function Write-Utf8([string]$Path,[string]$Text) { [System.IO.File]::WriteAllText((Resolve-Path $Path),$Text,(New-Object Text.UTF8Encoding($false))) }
function Replace-Once([string]$Path,[string]$Old,[string]$New,[string]$Label) {
  $t=Read-Utf8 $Path
  $i=$t.IndexOf($Old,[StringComparison]::Ordinal)
  if($i -lt 0){ throw "Anchor missing: $Label ($Path)" }
  if($t.IndexOf($Old,$i+$Old.Length,[StringComparison]::Ordinal) -ge 0){ throw "Anchor not unique: $Label ($Path)" }
  Write-Utf8 $Path ($t.Substring(0,$i)+$New+$t.Substring($i+$Old.Length))
}

# 1) Force an immediate autosave before the project is abandoned for the Home screen.
$main='src/NPVideoStudio.App/ViewModels/MainWindowViewModel.cs'
Replace-Once $main @'
    public async Task ShowStartScreenAsync()
    {
        CurrentProject = null;
'@ @'
    public async Task ShowStartScreenAsync()
    {
        // Do not throw away the last editing interval when Home is clicked. Periodic autosave can be up
        // to AutoSaveIntervalSeconds old; force the current saved project to its recovery slot first.
        if (CurrentProject is not null)
        {
            await _autoSaveService.TriggerNowAsync();
        }

        CurrentProject = null;
'@ 'force autosave before Home'

# 2) The Add-text shortcut used to create an unsaved in-memory Project, bypassing NewProjectViewModel's
# normal persisted-project path. Put it on disk first so normal save/autosave/recent-project machinery applies.
Replace-Once $main @'
        vm.AddTextToVideoRequested += () => CurrentPage = OpenWorkspaceForAddingText();
'@ @'
        vm.AddTextToVideoRequested += () => _ = OpenWorkspaceForAddingTextAsync();
'@ 'wire persisted add-text shortcut'

Replace-Once $main @'
    private WorkspaceViewModel OpenWorkspaceForAddingText()
    {
        var project = new Project { Name = "Video sa tekstom" };
        var workspace = OpenWorkspace(project);
        _ = workspace.StartAddTextToVideoFlowAsync();
        return workspace;
    }
'@ @'
    private async Task OpenWorkspaceForAddingTextAsync()
    {
        try
        {
            var project = new Project { Name = "Video sa tekstom" };
            var settings = _services.GetRequiredService<ISettingsService>();
            var projectRepository = _services.GetRequiredService<IProjectRepository>();
            var recentProjects = _services.GetRequiredService<IRecentProjectsService>();

            // Unique folder prevents a shortcut run from overwriting an earlier "Video sa tekstom" project.
            var folderName = $"Video-sa-tekstom-{DateTime.Now:yyyyMMdd-HHmmss}-{project.Id[..6]}";
            var projectDir = Path.Combine(settings.Current.ProjectsFolder, folderName);
            var projectFilePath = Path.Combine(projectDir, "Video sa tekstom.npvsproject");
            Directory.CreateDirectory(projectDir);
            await projectRepository.SaveAsync(project, projectFilePath);
            await recentProjects.RegisterOpenedAsync(project);

            var workspace = OpenWorkspace(project);
            CurrentPage = workspace;
            await workspace.StartAddTextToVideoFlowAsync();
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<Serilog.ILogger>().Error(ex, "Pokretanje bezbednog 'Dodaj tekst u video' toka nije uspelo");
            await ShowStartScreenAsync();
        }
    }
'@ 'persist add-text shortcut project'

# 3) Clean shutdown marker must be written only after one last autosave attempt.
$app='src/NPVideoStudio.App/App.axaml.cs'
Replace-Once $app @'
            desktop.ShutdownRequested += (_, _) =>
            {
                Task.Run(() => _autoSaveService.MarkCleanShutdownAsync()).GetAwaiter().GetResult();
                _logger.Information("NP Video Studio se zatvara čisto");
            };
'@ @'
            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    Task.Run(async () =>
                    {
                        await _autoSaveService.TriggerNowAsync();
                        await _autoSaveService.MarkCleanShutdownAsync();
                    }).GetAwaiter().GetResult();
                    _logger.Information("NP Video Studio se zatvara čisto posle poslednjeg autosave-a");
                }
                catch (Exception ex)
                {
                    // Do not write a false clean-shutdown marker if the final recovery save failed.
                    _logger.Error(ex, "Poslednji autosave pri gašenju nije uspeo; clean-shutdown marker nije upisan");
                }
            };
'@ 'final autosave before clean shutdown marker'

# 4) Destructive song-file deletion becomes a two-step action. First click only arms the warning.
$item='src/NPVideoStudio.App/ViewModels/SongLibraryItemViewModel.cs'
$t=Read-Utf8 $item
$t=$t.Replace('using System.Windows.Input;','using System.Windows.Input;`nusing CommunityToolkit.Mvvm.Input;')
$t=$t.Replace(@'
    public ICommand DeleteRecordAndFileCommand { get; }
'@,@'
    /// <summary>First click only reveals the destructive confirmation. It never touches disk.</summary>
    public ICommand DeleteRecordAndFileCommand { get; }
    public ICommand ConfirmDeleteRecordAndFileCommand { get; }
    public ICommand CancelDeleteRecordAndFileCommand { get; }

    private bool _isDeleteFileConfirmationVisible;
    public bool IsDeleteFileConfirmationVisible
    {
        get => _isDeleteFileConfirmationVisible;
        private set
        {
            if (_isDeleteFileConfirmationVisible == value) return;
            _isDeleteFileConfirmationVisible = value;
            OnPropertyChanged();
        }
    }
'@)
$t=$t.Replace(@'
        DeleteRecordOnlyCommand = deleteRecordOnlyCommand;
        DeleteRecordAndFileCommand = deleteRecordAndFileCommand;
'@,@'
        DeleteRecordOnlyCommand = deleteRecordOnlyCommand;
        ConfirmDeleteRecordAndFileCommand = deleteRecordAndFileCommand;
        DeleteRecordAndFileCommand = new RelayCommand(() => IsDeleteFileConfirmationVisible = true);
        CancelDeleteRecordAndFileCommand = new RelayCommand(() => IsDeleteFileConfirmationVisible = false);
'@)
if(-not $t.Contains('ConfirmDeleteRecordAndFileCommand')){ throw 'Song delete confirmation patch did not apply' }
Write-Utf8 $item $t

$view='src/NPVideoStudio.App/Views/MySongsView.axaml'
Replace-Once $view @'
                  <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                    <Button Classes="ghost" Content="Ponovo analiziraj" Command="{Binding ReanalyzeCommand}" />
                    <Button Classes="ghost" Content="Obriši zapis" Command="{Binding DeleteRecordOnlyCommand}" />
                    <Button Classes="ghost" Content="Obriši zapis i fajl" Command="{Binding DeleteRecordAndFileCommand}" />
                  </StackPanel>
'@ @'
                  <StackPanel Grid.Column="1" Spacing="8" VerticalAlignment="Center">
                    <StackPanel Orientation="Horizontal" Spacing="8" IsVisible="{Binding !IsDeleteFileConfirmationVisible}">
                      <Button Classes="ghost" Content="Ponovo analiziraj" Command="{Binding ReanalyzeCommand}" />
                      <Button Classes="ghost" Content="Obriši samo zapis" Command="{Binding DeleteRecordOnlyCommand}" />
                      <Button Classes="ghost" Content="Obriši zapis i AUDIO FAJL" Command="{Binding DeleteRecordAndFileCommand}" />
                    </StackPanel>
                    <Border Classes="panel" Padding="12" BorderBrush="#E06060" IsVisible="{Binding IsDeleteFileConfirmationVisible}">
                      <StackPanel Spacing="8">
                        <TextBlock Text="Ovo trajno briše originalni audio fajl sa diska. Ovu radnju nije moguće Undo." TextWrapping="Wrap" />
                        <StackPanel Orientation="Horizontal" Spacing="8">
                          <Button Content="DA, TRAJNO OBRIŠI FAJL" Command="{Binding ConfirmDeleteRecordAndFileCommand}" />
                          <Button Classes="ghost" Content="Otkaži" Command="{Binding CancelDeleteRecordAndFileCommand}" />
                        </StackPanel>
                      </StackPanel>
                    </Border>
                  </StackPanel>
'@ 'two-stage song file delete UI'

# Regression test: first destructive click may not call the repository; explicit confirmation must.
$tests='tests/NPVideoStudio.UnitTests/MySongsViewModelTests.cs'
$t=Read-Utf8 $tests
$anchor=@'
    [Fact]
    public async Task DeleteRecordOnlyCommand_OnLoadedItem_RemovesFromRepositoryAndList()
'@
$insert=@'
    [Fact]
    public async Task DeleteRecordAndFileCommand_RequiresExplicitSecondConfirmation()
    {
        var entry = new SongLibraryEntry { Title = "Važan original", OriginalAudioPath = "/original.mp3" };
        _repository.Entries.Add(entry);
        await _viewModel.InitializeAsync();
        var item = _viewModel.Songs[0];

        item.DeleteRecordAndFileCommand.Execute(null);

        Assert.True(item.IsDeleteFileConfirmationVisible);
        Assert.Single(_viewModel.Songs);
        Assert.Single(_repository.Entries);

        await ((IAsyncRelayCommand)item.ConfirmDeleteRecordAndFileCommand).ExecuteAsync(null);

        Assert.Empty(_viewModel.Songs);
        Assert.Empty(_repository.Entries);
    }

    [Fact]
    public async Task DeleteRecordAndFileCommand_CanBeCancelledWithoutDeletingAnything()
    {
        var entry = new SongLibraryEntry { Title = "Ne briši", OriginalAudioPath = "/keep.mp3" };
        _repository.Entries.Add(entry);
        await _viewModel.InitializeAsync();
        var item = _viewModel.Songs[0];

        item.DeleteRecordAndFileCommand.Execute(null);
        item.CancelDeleteRecordAndFileCommand.Execute(null);

        Assert.False(item.IsDeleteFileConfirmationVisible);
        Assert.Single(_viewModel.Songs);
        Assert.Single(_repository.Entries);
    }

'@ + $anchor
if(-not $t.Contains($anchor)){ throw 'MySongs test anchor missing' }
$t=$t.Replace($anchor,$insert)
Write-Utf8 $tests $t

Write-Host 'Audit data-safety patch applied.'

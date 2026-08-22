$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = [IO.File]::ReadAllText($Path)
    if (-not $text.Contains($Old)) { throw "Pattern not found in $Path`n---`n$Old" }
    [IO.File]::WriteAllText($Path, $text.Replace($Old, $New), [Text.UTF8Encoding]::new($false))
}

$path = 'src/NPVideoStudio.App/Views/ModernInspectorView.axaml'

Replace-Exact $path @'
                <Grid ColumnDefinitions="*,*">
                  <StackPanel Spacing="4"><TextBlock Text="Brzina" Classes="subtle"/><NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/></StackPanel>
                  <StackPanel Grid.Column="1" Spacing="4" Margin="8,0,0,0" IsVisible="{Binding IsVideoClip}">
                    <TextBlock Text="Prelaz iz prethodnog" Classes="subtle"/>
                    <ComboBox ItemsSource="{Binding AvailableTransitions}" SelectedItem="{Binding TransitionInType}"/>
                  </StackPanel>
                </Grid>
                <StackPanel Spacing="4" IsVisible="{Binding IsVideoClip}">
'@ @'
                <Grid ColumnDefinitions="*,*">
                  <StackPanel Spacing="4"><TextBlock Text="Brzina" Classes="subtle"/><NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/></StackPanel>
                  <StackPanel Grid.Column="1" Spacing="4" Margin="8,0,0,0" IsVisible="{Binding IsVideoClip}">
                    <TextBlock Text="Prelaz iz prethodnog" Classes="subtle"/>
                    <ComboBox ItemsSource="{Binding AvailableTransitions}" SelectedItem="{Binding TransitionInType}"/>
                  </StackPanel>
                </Grid>
                <Border Name="ModernVideoSpeedCurvePanel" Classes="inspectorSection" IsVisible="{Binding CanUseSpeedCurve}" Margin="0,4,0,0">
                  <StackPanel Spacing="5">
                    <TextBlock Text="Velocity / Speed Curve" Classes="section"/>
                    <ComboBox Name="ModernVideoSpeedCurve" ItemsSource="{Binding AvailableSpeedCurvePresets}" SelectedItem="{Binding SpeedCurvePreset}"/>
                    <TextBlock Text="Montage / Hero / Bullet / JumpCut / FlashIn / FlashOut menjaju stvarni video i audio timing. Ručna Brzina iznad vraća klip na konstantnu brzinu." Classes="subtle" TextWrapping="Wrap"/>
                  </StackPanel>
                </Border>
                <StackPanel Spacing="4" IsVisible="{Binding IsVideoClip}">
'@

Replace-Exact $path @'
              <TextBlock Text="Audio klip" Classes="section" />
              <TextBlock Text="Brzina" Classes="subtle"/>
              <NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
              <TextBlock Text="Utišavanje i fade kontrole su uvek dostupne u zaglavlju inspektora." Classes="subtle" TextWrapping="Wrap"/>
'@ @'
              <TextBlock Text="Audio klip" Classes="section" />
              <TextBlock Text="Brzina" Classes="subtle"/>
              <NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
              <Border Name="ModernAudioSpeedCurvePanel" Classes="inspectorSection" IsVisible="{Binding CanUseSpeedCurve}" Margin="0,4,0,0">
                <StackPanel Spacing="5">
                  <TextBlock Text="Velocity / Speed Curve" Classes="section"/>
                  <ComboBox Name="ModernAudioSpeedCurve" ItemsSource="{Binding AvailableSpeedCurvePresets}" SelectedItem="{Binding SpeedCurvePreset}"/>
                  <TextBlock Text="Kriva menja tempo uz očuvanje visine tona. Ručna Brzina iznad isključuje aktivnu krivu." Classes="subtle" TextWrapping="Wrap"/>
                </StackPanel>
              </Border>
              <TextBlock Text="Utišavanje i fade kontrole su uvek dostupne u zaglavlju inspektora." Classes="subtle" TextWrapping="Wrap"/>
'@

Remove-Item '.github/scripts/materialize-speed-modern-inspector-v2.ps1' -Force
Remove-Item '.github/workflows/materialize-speed-modern-inspector-v2.yml' -Force

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add src/NPVideoStudio.App/Views/ModernInspectorView.axaml .github/scripts/materialize-speed-modern-inspector-v2.ps1 .github/workflows/materialize-speed-modern-inspector-v2.yml
git commit -m 'Expose velocity curves in active Studio 2026 inspector'
git push origin HEAD:agent/velocity-speed-curves-v2

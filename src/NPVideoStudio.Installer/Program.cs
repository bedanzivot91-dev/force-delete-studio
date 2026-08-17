using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NPVideoStudio.Installer;

/// <summary>
/// Real "double-click and install" alternative to the Inno Setup installer
/// (installer/NPVideoStudio.iss), for machines that don't have Inno Setup and can't reach
/// jrsoftware.org to get it. Ships as NPVideoStudioSetup.exe inside the portable folder (see
/// build-release.ps1) - asks where to install (with a real folder-browse dialog), whether to add a
/// desktop shortcut, and whether to launch the app afterward, then copies everything next to it into
/// the chosen location, adds a Start Menu shortcut and an Add/Remove Programs entry, no admin rights
/// required (default location is %LocalAppData%\Programs, same convention as VS Code/most modern
/// per-user Windows installers, but the user can point it anywhere writable).
///
/// The folder-browse dialog is deliberately implemented by shelling out to powershell.exe running a
/// `System.Windows.Forms.FolderBrowserDialog` (same shell-out pattern <see cref="CreateShortcut"/>
/// already uses for its WScript.Shell COM call) instead of referencing System.Windows.Forms directly
/// from this project - this project's dev sandbox (see CLAUDE.md) has no WindowsDesktop SDK installed,
/// so it cannot build a project that targets net8.0-windows/UseWindowsForms at all. Every real Windows
/// machine already has powershell.exe with System.Windows.Forms available, so this works everywhere the
/// installer itself would run, without pulling in a UI framework this project can't even compile here.
/// </summary>
public static class Program
{
    private const string AppDisplayName = "NP Video Studio";
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NPVideoStudio";

    public static int Main(string[] args)
    {
        // Real, defensive fix: every previous version of this method let an exception thrown before
        // Install()'s own try/catch (or one from a source Install()/Uninstall() didn't anticipate)
        // propagate out of Main() with zero visible sign to the user - a WinExe with no console
        // attached shows nothing at all when that happens, which is indistinguishable from "double-
        // clicking did nothing." Wrapping the entire body here, plus a real log file next to the exe,
        // means any future failure - single-file bootstrap issue, a path this build didn't hit before,
        // anything - leaves real evidence instead of silence.
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Ovaj instalater radi samo na Windows-u.");
                return 1;
            }

            if (args.Contains("--uninstall"))
            {
                Uninstall();
                return 0;
            }

            Install();
            return 0;
        }
        catch (Exception ex)
        {
            TryLogFatal(ex);
            ShowMessage($"Neočekivana greška pri pokretanju instalatera:\n\n{ex}", "Greška pri instalaciji");
            return 1;
        }
    }

    /// <summary>Best-effort - written next to the exe (the same folder the user already has open),
    /// not AppData, so it's actually findable without knowing where to look. Never lets a logging
    /// failure itself replace the real error being reported.</summary>
    private static void TryLogFatal(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "NPVideoStudioSetup-greska.log");
            File.WriteAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch { /* best-effort */ }
    }

    private static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppDisplayName);

    private static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", $"{AppDisplayName}.lnk");

    private static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppDisplayName}.lnk");

    private static void Install()
    {
        var sourceDir = AppContext.BaseDirectory;

        var (choice, chosenDir, dialogError) = PromptForInstallDir(DefaultInstallDir);
        string installDir;
        switch (choice)
        {
            case InstallDirChoice.Cancelled:
                // User genuinely clicked "Cancel" in the dialog - quit quietly, same as clicking
                // "Cancel" in any real installer wizard, not an error worth a message box.
                return;
            case InstallDirChoice.Failed:
                // Real bug fixed here: the previous version of this method treated "the dialog itself
                // failed to run" (PowerShell/WinForms error, execution policy, AV interference,
                // anything) exactly the same as "user clicked Cancel" - both silently returned null,
                // which made Install() return with zero visible sign to the user. That reproduced the
                // *exact* silent "double-click, nothing happens" symptom this whole installer-robustness
                // pass exists to eliminate. Now a real failure shows a real message and falls back to
                // the default location instead of exiting silently.
                ShowMessage(
                    $"Dijalog za biranje foldera nije uspeo da se prikaže:\n\n{dialogError}\n\n" +
                    $"Instalacija će nastaviti u podrazumevani folder:\n{DefaultInstallDir}",
                    "Upozorenje pri instalaciji");
                installDir = DefaultInstallDir;
                break;
            default:
                installDir = chosenDir!;
                break;
        }

        // Real edge case the new free-choice folder picker makes possible that the old fixed-location
        // installer never had to worry about: the user can now browse to and pick the very folder
        // NPVideoStudioSetup.exe is already running from (or a parent of it) - without this guard,
        // CopyDirectory would then try to copy sourceDir into a subfolder of itself.
        var normalizedSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDir));
        var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        if (normalizedTarget.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            ShowMessage(
                "Ne možete instalirati program u isti folder iz kog pokrećete instalaciju (ili u njegov " +
                "podfolder). Pokrenite instalaciju ponovo i izaberite neki drugi folder.",
                "Nevažeći folder za instalaciju");
            return;
        }

        var createDesktopShortcut = AskYesNo(
            "Da li želite da se doda prečica na Desktopu?", "Prečica na Desktopu");
        var launchAfterInstall = AskYesNo(
            "Da li želite da se NP Video Studio pokrene odmah posle instalacije?", "Pokretanje posle instalacije");
        var resetOldState = AskYesNo(
            "Da li želite ČISTU INSTALACIJU?\n\n" +
            "Biće obrisana samo stara podešavanja, lista nedavnih projekata i autosave. " +
            "Vaši originalni video, audio i .npvsproject fajlovi neće biti obrisani.",
            "Čista instalacija");

        try
        {
            if (resetOldState)
            {
                ResetLocalApplicationState(AppDataDirectory);
            }
            CopyDirectory(sourceDir, installDir);

            var mainExePath = Path.Combine(installDir, "NPVideoStudio.exe");
            CreateShortcut(mainExePath, installDir, StartMenuShortcutPath);
            if (createDesktopShortcut)
            {
                CreateShortcut(mainExePath, installDir, DesktopShortcutPath);
            }
            RegisterUninstallEntry(installDir);

            ShowMessage(
                $"Instalacija je uspešno završena.\n\nNP Video Studio je instaliran u:\n{installDir}\n\n" +
                "Prečica je dodata u Start meni.",
                "Instalacija završena");

            if (launchAfterInstall)
            {
                Process.Start(new ProcessStartInfo(mainExePath) { UseShellExecute = true, WorkingDirectory = installDir });
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Instalacija nije uspela:\n\n{ex.Message}", "Greška pri instalaciji");
        }
    }

    private enum InstallDirChoice { Selected, Cancelled, Failed }

    private static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDisplayName);

    /// <summary>Removes only state generated by the application, never source media or project files.
    /// Public for a real filesystem regression test with an isolated temporary directory.</summary>
    public static void ResetLocalApplicationState(string appDataDirectory)
    {
        foreach (var fileName in new[] { "settings.json", "npvideostudio.db" })
        {
            var path = Path.Combine(appDataDirectory, fileName);
            if (File.Exists(path)) File.Delete(path);
        }

        foreach (var folderName in new[] { "AutoSave", "PreviewCache" })
        {
            var path = Path.Combine(appDataDirectory, folderName);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Real folder-browse dialog via powershell.exe + System.Windows.Forms.FolderBrowserDialog (see the
    /// class-level doc comment for why it's implemented this way instead of a direct UI reference).
    /// Deliberately distinguishes "user clicked Cancel" (<see cref="InstallDirChoice.Cancelled"/> - a
    /// normal, silent outcome) from "the dialog itself couldn't run or errored"
    /// (<see cref="InstallDirChoice.Failed"/> - always surfaced with a real message, never silent) -
    /// an earlier version of this method collapsed both into the same "return null", which made a real
    /// PowerShell/WinForms failure look exactly like a normal Cancel: Install() would just return with
    /// nothing shown, reproducing the silent "double-click, nothing happens" symptom this installer is
    /// supposed to have eliminated.
    /// </summary>
    private static (InstallDirChoice Choice, string? InstallDir, string? Error) PromptForInstallDir(
        string defaultInstallDir)
    {
        var startingParent = Path.GetDirectoryName(defaultInstallDir) ?? defaultInstallDir;

        var script =
            "Add-Type -AssemblyName System.Windows.Forms; " +
            "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog; " +
            "$dialog.Description = 'Izaberite folder u koji zelite da instalirate NP Video Studio'; " +
            $"$dialog.SelectedPath = '{startingParent}'; " +
            "$dialog.ShowNewFolderButton = $true; " +
            "if ($dialog.ShowDialog() -eq 'OK') { Write-Output $dialog.SelectedPath } else { Write-Output '__OTKAZANO__' }";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-STA");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr)
                    ? $"Dijalog za biranje foldera je vraćen sa greškom (kod {process.ExitCode})."
                    : stderr;
                return (InstallDirChoice.Failed, null, message);
            }

            if (string.IsNullOrEmpty(stdout) || stdout == "__OTKAZANO__")
            {
                return (InstallDirChoice.Cancelled, null, null);
            }

            return (InstallDirChoice.Selected, Path.Combine(stdout, AppDisplayName), null);
        }
        catch (Exception ex)
        {
            return (InstallDirChoice.Failed, null, ex.Message);
        }
    }

    private static void Uninstall()
    {
        try
        {
            if (File.Exists(StartMenuShortcutPath))
            {
                File.Delete(StartMenuShortcutPath);
            }

            if (File.Exists(DesktopShortcutPath))
            {
                File.Delete(DesktopShortcutPath);
            }

            var installDir = ReadInstallLocationFromRegistry() ?? DefaultInstallDir;

            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, throwOnMissingSubKey: false);

            ShowMessage($"{AppDisplayName} će sada biti uklonjen sa vašeg računara.", "Deinstalacija");

            // Can't delete our own running exe or its containing folder synchronously - hand off to a
            // short-lived cmd.exe that waits for this process to exit first, same pattern every
            // self-deleting Windows installer/uninstaller uses.
            ScheduleInstallDirDeleteAfterExit(installDir);
        }
        catch (Exception ex)
        {
            ShowMessage($"Deinstalacija nije uspela:\n\n{ex.Message}", "Greška pri deinstalaciji");
        }
    }

    /// <summary>
    /// The install location is now user-chosen (see <see cref="PromptForInstallDir"/>), so uninstall can no
    /// longer assume <see cref="DefaultInstallDir"/> - it has to read back the real path this specific
    /// install used, which <see cref="RegisterUninstallEntry"/> already writes to the registry.
    /// </summary>
    private static string? ReadInstallLocationFromRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey);
        return key?.GetValue("InstallLocation") as string;
    }

    /// <summary>
    /// Real bug found and fixed: this used to join paths via <c>dirPath.Replace(sourceDir, targetDir)</c>,
    /// a naive string swap that silently breaks whenever <paramref name="sourceDir"/> and
    /// <paramref name="targetDir"/> disagree on a trailing directory separator - which they always did
    /// here, since <c>AppContext.BaseDirectory</c> (this installer's real <paramref name="sourceDir"/>) is
    /// documented to always end with a trailing separator, while the install directory (built via
    /// <c>Path.Combine</c>) never has one. The swap ate the separator between the install folder and every
    /// single copied item's name, producing sibling folders like "NP Video Studiolibvlc" instead of a
    /// "libvlc" subfolder inside "NP Video Studio" - and, critically, the exact same collapse happened for
    /// every top-level file too, so the installed exe itself ended up at "...NP Video
    /// StudioNPVideoStudio.exe", a path the app never actually looks for, causing "the system cannot find
    /// the file specified" right after a real, honestly-reported "install succeeded" message. Never caught
    /// before this fix because this Linux sandbox cannot execute this Windows-only installer to test it end
    /// to end (the standing, disclosed CLAUDE.md constraint) and no automated test covered this method -
    /// fixed with <see cref="Path.GetRelativePath"/> + <see cref="Path.Combine"/>, which are correct
    /// regardless of either side's trailing separator, and now covered by a real test using real temporary
    /// directories (see NPVideoStudio.UnitTests/InstallerCopyDirectoryTests.cs).
    /// </summary>
    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dirPath);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, filePath);
            var destPath = Path.Combine(targetDir, relative);
            File.Copy(filePath, destPath, overwrite: true);
        }
    }

    /// <summary>
    /// Shells out to powershell.exe's WScript.Shell COM object to write a real .lnk file - the
    /// standard, well-tested way to create a Windows shortcut, instead of hand-writing the binary
    /// MS-SHLLINK format or depending on a COM interop assembly this Linux-hosted build can't generate.
    /// </summary>
    private static void CreateShortcut(string targetExePath, string workingDirectory, string shortcutPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var script =
            $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{shortcutPath}'); " +
            $"$s.TargetPath = '{targetExePath}'; " +
            $"$s.WorkingDirectory = '{workingDirectory}'; " +
            "$s.Save()";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"Pravljenje prečice nije uspelo (kod {process.ExitCode})."
                : stderr.Trim());
        }
    }

    private static void RegisterUninstallEntry(string installDir)
    {
        var version = typeof(Program).Assembly.GetName().Version;
        var versionText = version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";

        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey);
        key.SetValue("DisplayName", AppDisplayName);
        key.SetValue("DisplayVersion", versionText);
        key.SetValue("Publisher", AppDisplayName);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", $"\"{Path.Combine(installDir, "NPVideoStudioSetup.exe")}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void ScheduleInstallDirDeleteAfterExit(string dir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add($"timeout /t 2 /nobreak >nul & rmdir /s /q \"{dir}\"");
        Process.Start(psi);
    }

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private static void ShowMessage(string text, string caption) => MessageBoxW(0, text, caption, 0);

    private static bool AskYesNo(string text, string caption) =>
        MessageBoxW(0, text, caption, MB_YESNO | MB_ICONQUESTION) == IDYES;
}

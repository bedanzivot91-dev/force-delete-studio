using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NPVideoStudio.Installer;

/// <summary>
/// Minimal real "double-click and install" alternative to the Inno Setup installer
/// (installer/NPVideoStudio.iss), for machines that don't have Inno Setup and can't reach
/// jrsoftware.org to get it. Ships as NPVideoStudioSetup.exe inside the portable folder (see
/// build-release.ps1) - copies everything next to it into a per-user install location, adds a Start
/// Menu shortcut and an Add/Remove Programs entry, no admin rights required (installs under
/// %LocalAppData%\Programs, same convention as VS Code/most modern per-user Windows installers).
///
/// Deliberately not a general-purpose installer framework: one product, one install location, no
/// custom install path picker, no component selection - Inno Setup remains the "real" installer for
/// anyone who has/can install it; this is the honest fallback for anyone who can't.
/// </summary>
public static class Program
{
    private const string AppDisplayName = "NP Video Studio";
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NPVideoStudio";

    public static int Main(string[] args)
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

    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppDisplayName);

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", $"{AppDisplayName}.lnk");

    private static void Install()
    {
        var sourceDir = AppContext.BaseDirectory;
        var installDir = InstallDir;

        try
        {
            CopyDirectory(sourceDir, installDir);

            var mainExePath = Path.Combine(installDir, "NPVideoStudio.exe");
            CreateShortcut(mainExePath, installDir);
            RegisterUninstallEntry(installDir);

            ShowMessage(
                $"Instalacija je uspešno završena.\n\nNP Video Studio je instaliran u:\n{installDir}\n\n" +
                "Prečica je dodata u Start meni.",
                "Instalacija završena");

            Process.Start(new ProcessStartInfo(mainExePath) { UseShellExecute = true, WorkingDirectory = installDir });
        }
        catch (Exception ex)
        {
            ShowMessage($"Instalacija nije uspela:\n\n{ex.Message}", "Greška pri instalaciji");
        }
    }

    private static void Uninstall()
    {
        try
        {
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }

            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, throwOnMissingSubKey: false);

            ShowMessage($"{AppDisplayName} će sada biti uklonjen sa vašeg računara.", "Deinstalacija");

            // Can't delete our own running exe or its containing folder synchronously - hand off to a
            // short-lived cmd.exe that waits for this process to exit first, same pattern every
            // self-deleting Windows installer/uninstaller uses.
            ScheduleInstallDirDeleteAfterExit(InstallDir);
        }
        catch (Exception ex)
        {
            ShowMessage($"Deinstalacija nije uspela:\n\n{ex.Message}", "Greška pri deinstalaciji");
        }
    }

    /// <summary>
    /// Real bug found and fixed: this used to join paths via <c>dirPath.Replace(sourceDir, targetDir)</c>,
    /// a naive string swap that silently breaks whenever <paramref name="sourceDir"/> and
    /// <paramref name="targetDir"/> disagree on a trailing directory separator - which they always did
    /// here, since <c>AppContext.BaseDirectory</c> (this installer's real <paramref name="sourceDir"/>) is
    /// documented to always end with a trailing separator, while <see cref="InstallDir"/> (built via
    /// <c>Path.Combine</c>) never has one. The swap ate the separator between the install folder and every
    /// single copied item's name, producing sibling folders like "NP Video Studiolibvlc" instead of a
    /// "libvlc" subfolder inside "NP Video Studio" - and, critically, the exact same collapse happened for
    /// every top-level file too, so the installed exe itself ended up at "...NP Video
    /// StudioNPVideoStudio.exe", a path the app never actually looks for, causing "the system cannot find
    /// the file specified" right after a real, honestly-reported "install succeeded" message. Never caught
    /// before because this Linux sandbox cannot execute this Windows-only installer to test it end to end
    /// (the standing, disclosed CLAUDE.md constraint) and no automated test covered this method - fixed
    /// with <see cref="Path.GetRelativePath"/> + <see cref="Path.Combine"/>, which are correct regardless
    /// of either side's trailing separator, and now covered by a real test using real temporary
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
    private static void CreateShortcut(string targetExePath, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);

        var script =
            $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{ShortcutPath}'); " +
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private static void ShowMessage(string text, string caption) => MessageBoxW(0, text, caption, 0);
}

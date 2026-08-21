using System.Text.Json;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

/// <summary>
/// A template the user saved themselves, from a project they already set up. Unlike
/// <see cref="ProjectTemplate.BuiltIn"/> (a fixed starter list that ships with the app), these are
/// created, renamed and deleted by the user - which is what "Upravljanje šablonima" actually means.
/// </summary>
public sealed class UserTemplate
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>The starter tracks, same idea as a built-in template.</summary>
    public List<TimelineTrackKind> StarterTrackKinds { get; set; } = new();

    /// <summary>Export format saved with the template, so "my TikTok setup" restores 1080x1920 at the
    /// right frame rate too - not just the track layout. Fps is stored separately because a Custom preset
    /// can be 23.976/29.97/etc. and the enum alone cannot reconstruct that value.</summary>
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public FrameRatePreset FrameRate { get; set; } = FrameRatePreset.Fps30;
    public double Fps { get; set; } = 30.0;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Stores user-created templates as one JSON file per template under
/// <c>%LocalAppData%\NP Video Studio\Templates</c>.
///
/// One file per template on purpose rather than a single templates.json: a corrupt or half-written file
/// then costs the user exactly one template instead of all of them, and deleting a template is a plain
/// file delete with nothing to rewrite.
/// </summary>
public sealed class UserTemplateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;

    public UserTemplateRepository(string? folderOverride = null)
    {
        _folder = folderOverride ?? Path.Combine(AppSettings.AppDataRoot(), "Templates");
    }

    /// <summary>Every saved template, newest first. A single unreadable file is skipped rather than
    /// throwing - one bad file must not make the whole template list unopenable.</summary>
    public IReadOnlyList<UserTemplate> LoadAll()
    {
        if (!Directory.Exists(_folder))
        {
            return Array.Empty<UserTemplate>();
        }

        var templates = new List<UserTemplate>();

        foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var template = JsonSerializer.Deserialize<UserTemplate>(File.ReadAllText(file), JsonOptions);
                if (template is not null && !string.IsNullOrWhiteSpace(template.Name))
                {
                    templates.Add(template);
                }
            }
            catch
            {
                // Corrupt/partial file - skip this one template, keep the rest usable.
            }
        }

        return templates.OrderByDescending(t => t.CreatedAt).ToList();
    }

    /// <summary>Checks the same sanitized path Save/Delete use, so names like "9:16" are treated
    /// consistently and the UI can require an explicit overwrite confirmation.</summary>
    public bool Exists(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return File.Exists(PathFor(name));
    }

    /// <summary>Saves (or overwrites) a template under its name. Returns the file path written.</summary>
    public string Save(UserTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw new ArgumentException("Šablon mora imati ime.", nameof(template));
        }

        Directory.CreateDirectory(_folder);
        var path = PathFor(template.Name);

        // Write to a temp file then move: a crash mid-write leaves the previous template intact instead
        // of a truncated file that LoadAll would have to skip.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(template, JsonOptions));
        File.Move(tempPath, path, overwrite: true);

        return path;
    }

    /// <summary>True if a template with that name existed and was deleted.</summary>
    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>Renames a saved template without silently replacing another one unless overwrite is
    /// explicitly requested by the caller. The JSON name and filename are updated together.</summary>
    public UserTemplate Rename(string oldName, string newName, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Staro i novo ime šablona moraju biti uneti.");
        }

        var oldPath = PathFor(oldName);
        if (!File.Exists(oldPath))
        {
            throw new FileNotFoundException("Šablon koji želite da preimenujete više ne postoji.", oldPath);
        }

        var newPath = PathFor(newName);
        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath) && !overwrite)
        {
            throw new IOException("Šablon sa tim imenom već postoji.");
        }

        var template = JsonSerializer.Deserialize<UserTemplate>(File.ReadAllText(oldPath), JsonOptions)
            ?? throw new InvalidDataException("Šablon nije moguće pročitati.");
        template.Name = newName.Trim();

        Directory.CreateDirectory(_folder);
        var tempPath = newPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(template, JsonOptions));
        File.Move(tempPath, newPath, overwrite: true);

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        return template;
    }

    /// <summary>Builds a template from a project the user has already set up - the "save what I have as a
    /// template" direction. Empty tracks and full ones alike contribute only their KIND: a template is a
    /// starting point, never a copy of the user's actual footage.</summary>
    public static UserTemplate FromProject(Project project, string name, string description = "") => new()
    {
        Name = name,
        Description = description,
        StarterTrackKinds = project.Timeline.Tracks.Select(t => t.Kind).ToList(),
        Width = project.Format.Width,
        Height = project.Format.Height,
        FrameRate = project.Format.FrameRate,
        Fps = project.Format.Fps
    };

    private string PathFor(string name) => Path.Combine(_folder, $"{SanitizeFileName(name)}.json");

    /// <summary>Names come from a free-text box, so anything Windows forbids in a file name has to go -
    /// otherwise saving a template called "9:16" would throw instead of saving.</summary>
    public static string SanitizeFileName(string name)
    {
        var cleaned = new string(name
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
            .ToArray())
            .Trim();

        return string.IsNullOrEmpty(cleaned) ? "sablon" : cleaned;
    }
}

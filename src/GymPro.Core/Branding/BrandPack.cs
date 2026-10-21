using System.IO.Compression;

namespace GymPro.Core.Branding;

/// <summary>A brand profile plus its image files, as loaded from or saved to a brand pack.</summary>
public sealed record BrandPackContent(BrandProfile Profile, byte[]? Logo, byte[]? Icon);

/// <summary>
/// A brand pack is a folder (diffable, committable) or a <c>.brandpack</c> zip (easy to e-mail) containing
/// <c>brand.json</c>, <c>logo.png</c> and <c>app.ico</c>.
/// </summary>
public static class BrandPack
{
    public const string ProfileFile = "brand.json";
    public const string LogoFile = "logo.png";
    public const string IconFile = "app.ico";
    public const string Extension = ".brandpack";

    private const long MaxEntryBytes = 10 * 1024 * 1024; // a logo or icon; anything bigger is not a brand pack

    public static BrandPackContent Load(string path)
    {
        if (Directory.Exists(path))
        {
            return LoadFolder(path);
        }

        if (File.Exists(path) && string.Equals(Path.GetFileName(path), ProfileFile, StringComparison.OrdinalIgnoreCase))
        {
            return LoadFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }

        if (File.Exists(path))
        {
            return LoadZip(path);
        }

        throw new FileNotFoundException("Brand pack not found.", path);
    }

    /// <summary>Writes brand.json (+ logo/icon) into <paramref name="folder"/>; image paths are set to the standard file names.</summary>
    public static void SaveFolder(BrandPackContent content, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in Entries(content))
        {
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }

        // Remove images that the brand no longer has, so the folder matches the profile.
        if (content.Logo is null)
        {
            File.Delete(Path.Combine(folder, LogoFile));
        }

        if (content.Icon is null)
        {
            File.Delete(Path.Combine(folder, IconFile));
        }
    }

    public static void SaveZip(BrandPackContent content, string zipPath)
    {
        var tmp = zipPath + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (name, bytes) in Entries(content))
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(bytes);
            }
        }

        File.Move(tmp, zipPath, overwrite: true);
    }

    private static IEnumerable<(string Name, byte[] Bytes)> Entries(BrandPackContent c)
    {
        var profile = c.Profile with
        {
            LogoPath = c.Logo is null ? null : LogoFile,
            IconPath = c.Icon is null ? null : IconFile,
        };
        yield return (ProfileFile, System.Text.Encoding.UTF8.GetBytes(BrandJson.Serialize(profile)));
        if (c.Logo is not null)
        {
            yield return (LogoFile, c.Logo);
        }

        if (c.Icon is not null)
        {
            yield return (IconFile, c.Icon);
        }
    }

    private static BrandPackContent LoadFolder(string folder)
    {
        var json = Path.Combine(folder, ProfileFile);
        if (!File.Exists(json))
        {
            throw new InvalidDataException($"No {ProfileFile} in {folder}.");
        }

        var profile = BrandJson.Deserialize(File.ReadAllText(json));
        return new BrandPackContent(profile, ReadSibling(folder, profile.LogoPath), ReadSibling(folder, profile.IconPath));
    }

    private static byte[]? ReadSibling(string folder, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        // Only files inside the pack folder: a brand.json must not be able to read arbitrary files.
        var full = Path.GetFullPath(Path.Combine(folder, relative));
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{relative}' points outside the brand pack.");
        }

        return File.Exists(full) ? File.ReadAllBytes(full) : throw new InvalidDataException($"'{relative}' is missing from the brand pack.");
    }

    private static BrandPackContent LoadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        byte[]? Read(string name)
        {
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            if (entry.Length > MaxEntryBytes)
            {
                throw new InvalidDataException($"{name} in the brand pack is too large.");
            }

            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        var json = Read(ProfileFile) ?? throw new InvalidDataException($"No {ProfileFile} in {Path.GetFileName(zipPath)}.");
        var profile = BrandJson.Deserialize(System.Text.Encoding.UTF8.GetString(json));

        // Zip entries are looked up by fixed names only (no extraction to disk), so a crafted zip can't write files.
        return new BrandPackContent(profile,
            profile.LogoPath is null ? null : Read(LogoFile),
            profile.IconPath is null ? null : Read(IconFile));
    }
}

/// <summary>Writes and reads the Windows .ico container with PNG-compressed entries (supported since Windows Vista).</summary>
public static class IcoFile
{
    public static readonly int[] StandardSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public sealed record Entry(int Width, int Height, int Offset, int Length);

    public static byte[] Write(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        if (images.Count == 0)
        {
            throw new ArgumentException("An icon needs at least one image.", nameof(images));
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1); // type: icon
        w.Write((ushort)images.Count);
        var offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            if (size is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(images), $"Icon size {size} must be 1..256.");
            }

            var dim = (byte)(size >= 256 ? 0 : size); // 0 means 256 in the ICO format
            w.Write(dim);
            w.Write(dim);
            w.Write((byte)0);  // palette
            w.Write((byte)0);  // reserved
            w.Write((ushort)1);  // planes
            w.Write((ushort)32); // bits per pixel
            w.Write((uint)png.Length);
            w.Write((uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            w.Write(png);
        }

        w.Flush();
        return ms.ToArray();
    }

    public static IReadOnlyList<Entry> Read(byte[] ico)
    {
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1)
        {
            throw new InvalidDataException("Not an .ico file.");
        }

        var count = BitConverter.ToUInt16(ico, 4);
        if (ico.Length < 6 + (16 * count))
        {
            throw new InvalidDataException("The .ico file is truncated.");
        }

        var list = new List<Entry>(count);
        for (var i = 0; i < count; i++)
        {
            var o = 6 + (16 * i);
            int Dim(byte b) => b == 0 ? 256 : b;
            list.Add(new Entry(Dim(ico[o]), Dim(ico[o + 1]), (int)BitConverter.ToUInt32(ico, o + 12), (int)BitConverter.ToUInt32(ico, o + 8)));
        }

        return list;
    }
}

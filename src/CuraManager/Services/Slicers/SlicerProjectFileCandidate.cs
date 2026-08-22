using System.IO;
using System.IO.Compression;

namespace CuraManager.Services.Slicers;

/// <summary>
/// One file being offered to every provider for identification. The zip is opened
/// once and shared, so provider count does not multiply reads.
/// </summary>
public sealed class SlicerProjectFileCandidate : IDisposable
{
    private static readonly string[] NoEntries = Array.Empty<string>();

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, string> _entryTextCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, string> _entryTextPrefixCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    private SlicerProjectFileCandidate(
        string filePath,
        ZipArchive archive,
        IReadOnlyCollection<string> entryNames,
        IReadOnlyList<string> lockingProcessNames
    )
    {
        FilePath = filePath;
        Extension = Path.GetExtension(filePath);
        _archive = archive;
        ZipEntryNames = entryNames;
        LockingProcessNames = lockingProcessNames;
    }

    public string FilePath { get; }
    public string Extension { get; }

    /// <summary>Entry names, or empty when the file is not a readable zip.</summary>
    public IReadOnlyCollection<string> ZipEntryNames { get; }

    /// <summary>
    /// Populated only when the archive could not be read. Providers use it as a
    /// last-resort hint that the slicer currently has the file open.
    /// </summary>
    public IReadOnlyList<string> LockingProcessNames { get; }

    public static SlicerProjectFileCandidate Create(
        string filePath,
        IFileLockInspector lockInspector
    )
    {
        if (
            !string.Equals(Path.GetExtension(filePath), ".3mf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(filePath)
        )
        {
            return new SlicerProjectFileCandidate(filePath, null, NoEntries, NoEntries);
        }

        ZipArchive archive = null;
        try
        {
            archive = ZipFile.OpenRead(filePath);
            var entryNames = archive.Entries.Select(x => x.FullName).ToArray();
            return new SlicerProjectFileCandidate(filePath, archive, entryNames, NoEntries);
        }
        catch (Exception ex)
            when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Typically the slicer itself is holding the file open, or the archive is
            // truncated. Entries is read lazily, so this can throw after OpenRead
            // succeeded — the archive must be disposed on the way out.
            archive?.Dispose();
            return new SlicerProjectFileCandidate(
                filePath,
                null,
                NoEntries,
                lockInspector.GetLockingProcessNames(filePath)
            );
        }
    }

    public bool HasEntry(string entryName) =>
        ZipEntryNames.Any(x => string.Equals(x, entryName, StringComparison.OrdinalIgnoreCase));

    public bool HasEntryStartingWith(string prefix) =>
        ZipEntryNames.Any(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads an entry as text in full, or returns <see langword="null"/> if absent or
    /// unreadable. Memoized. A valid central directory does not guarantee a readable
    /// entry stream -- a corrupt/truncated entry throws <see cref="InvalidDataException"/>
    /// or <see cref="IOException"/> from <see cref="ZipArchiveEntry.Open"/> or the
    /// subsequent read, which is treated the same as the entry being absent so a single
    /// damaged entry cannot take down every provider probing the file.
    /// </summary>
    public string ReadEntryText(string entryName)
    {
        if (_archive == null)
            return null;
        if (_entryTextCache.TryGetValue(entryName, out var cached))
            return cached;

        string text = null;
        try
        {
            var entry = _archive.GetEntry(entryName);
            if (entry != null)
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
        }
        catch (Exception ex)
            when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            text = null;
        }

        _entryTextCache[entryName] = text;
        return text;
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> bytes of an entry as text, or returns
    /// <see langword="null"/> if absent or unreadable. Memoized separately per
    /// (entry, bound) pair from <see cref="ReadEntryText"/>, because the two serve
    /// different callers: this exists so a discriminator that only needs to see a
    /// marker near the start of an entry never has to decompress the rest of it -- for a
    /// multi-hundred-megabyte mesh entry, <see cref="ReadEntryText"/> would otherwise
    /// allocate a UTF-16 string roughly twice that size, on the UI thread, for every
    /// provider that gets asked to identify the file. Decoded with a replacement-fallback
    /// UTF-8 decoder, so a bound that lands mid-codepoint degrades gracefully instead of
    /// throwing.
    /// </summary>
    public string ReadEntryTextPrefix(string entryName, int maxBytes)
    {
        if (_archive == null)
            return null;

        var cacheKey = $"{entryName}\0{maxBytes}";
        if (_entryTextPrefixCache.TryGetValue(cacheKey, out var cached))
            return cached;

        string text = null;
        try
        {
            var entry = _archive.GetEntry(entryName);
            if (entry != null)
            {
                using var stream = entry.Open();
                var buffer = new byte[maxBytes];
                var totalRead = 0;
                int read;
                while (
                    totalRead < buffer.Length
                    && (read = stream.Read(buffer, totalRead, buffer.Length - totalRead)) > 0
                )
                {
                    totalRead += read;
                }

                text = Encoding.UTF8.GetString(buffer, 0, totalRead);
            }
        }
        catch (Exception ex)
            when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            text = null;
        }

        _entryTextPrefixCache[cacheKey] = text;
        return text;
    }

    public void Dispose() => _archive?.Dispose();
}

using System;
using System.IO;
using System.IO.Compression;

namespace CuraManager.Tests;

internal static class TestZip
{
    /// <summary>Writes a zip to <paramref name="path"/> with the given entries.</summary>
    public static string Create(string path, params (string EntryName, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    /// <summary>A directory that deletes itself at the end of a test.</summary>
    public sealed class Scope : IDisposable
    {
        public Scope()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "curamanager-tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A leaked temp directory must never fail a test.
            }
        }
    }
}

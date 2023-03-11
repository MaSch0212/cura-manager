namespace CuraManager.Models;

public record CuraVersion(
    Version Version,
    string DisplayName,
    string ProgramFilesPath,
    string AppDataPath,
    bool IsSupported);

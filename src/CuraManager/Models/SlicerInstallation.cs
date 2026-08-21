namespace CuraManager.Models;

public record SlicerInstallation(
    Version Version,
    string DisplayName,
    string ProgramFilesPath,
    string AppDataPath,
    bool IsSupported
);

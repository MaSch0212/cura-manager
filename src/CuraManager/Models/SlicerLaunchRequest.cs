namespace CuraManager.Models;

/// <summary>
/// A request to open a slicer with a set of models.
/// </summary>
/// <param name="ProjectDirectory">Directory the models are staged in.</param>
/// <param name="ModelFiles">Absolute paths of the model files to load.</param>
/// <param name="ProjectName">
/// Only set when legacy Cura project naming is active. Every other provider ignores it.
/// Remove this property — not the record — when legacy naming goes away in 2.0.
/// </param>
public record SlicerLaunchRequest(
    string ProjectDirectory,
    IReadOnlyList<string> ModelFiles,
    string ProjectName
);

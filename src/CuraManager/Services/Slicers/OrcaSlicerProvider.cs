namespace CuraManager.Services.Slicers;

/// <summary>
/// <see cref="ISlicerProvider"/> implementation for OrcaSlicer.
/// </summary>
public class OrcaSlicerProvider : OrcaFamilySlicerProvider
{
    public const string ProviderId = "orca";

    /// <summary>
    /// OrcaSlicer stamps this into 3D/3dmodel.model in addition to the inherited
    /// Bambu <c>Application</c> tag. It is the only marker in a saved project that
    /// distinguishes it from Bambu Studio, which emits the same X-BBL- producer key.
    /// </summary>
    private const string OrcaModelMetadata = "name=\"OrcaSlicer\"";

    public override string Id => ProviderId;
    public override string DisplayName => "OrcaSlicer";
    public override string IconResourceKey => "OrcaSlicerIcon";

    // Latest verified against a real 2.4.2 project file.
    public override Version LatestSupportedVersion { get; } = new Version(2, 4, 2);

    protected override string AppKey => "OrcaSlicer";

    protected override string[] ExecutableFileNames => ["orca-slicer.exe", "OrcaSlicer.exe"];

    protected override string InstallDirNameFilter => "orca";

    protected override string[] ProcessNames => ["orca-slicer", "OrcaSlicer"];

    // Not used — IsProducedByThisFlavour is overridden, because OrcaSlicer and Bambu
    // Studio both emit X-BBL- and the prefix therefore cannot separate them.
    protected override string SliceInfoHeaderPrefix => null;

    protected override bool IsProducedByThisFlavour(SlicerProjectFileCandidate candidate) =>
        ReadModelMetadata(candidate)?.Contains(OrcaModelMetadata, StringComparison.Ordinal) == true;
}

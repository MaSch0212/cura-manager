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

    public OrcaSlicerProvider(IMsixPackageLocator msixPackageLocator = null)
        : base(msixPackageLocator) { }

    public override string Id => ProviderId;
    public override string DisplayName => "OrcaSlicer";
    public override string IconResourceKey => "OrcaSlicerIcon";

    // Latest verified against the maintainer's Microsoft Store build, 2.4.3.0.
    // Four-part like the sibling providers' LatestSupportedVersion (not new Version(2, 4,
    // 3), which defaults Revision to -1): every detected version -- whether normalized
    // through VersionExtensions.SafeParse or read straight from an MSIX package identity
    // -- carries an explicit Revision of 0 or more, so a three-part latest-version would
    // make even an exact version match compare as unsupported.
    public override Version LatestSupportedVersion { get; } = new Version(2, 4, 3, 0);

    protected override string AppKey => "OrcaSlicer";

    protected override string[] ExecutableFileNames => ["orca-slicer.exe", "OrcaSlicer.exe"];

    protected override string InstallDirNameFilter => "orca";

    // The Microsoft Store package name is "OrcaSlicer.OrcaSlicer"; matching on the
    // prefix keeps this robust to the second segment being renamed.
    protected override string MsixPackageNamePrefix => "OrcaSlicer.";

    protected override string[] ProcessNames => ["orca-slicer", "OrcaSlicer"];

    // Not used — IsProducedByThisFlavour is overridden, because OrcaSlicer and Bambu
    // Studio both emit X-BBL- and the prefix therefore cannot separate them.
    protected override string SliceInfoHeaderPrefix => null;

    protected override bool IsProducedByThisFlavour(SlicerProjectFileCandidate candidate) =>
        ReadModelMetadata(candidate)?.Contains(OrcaModelMetadata, StringComparison.Ordinal) == true;
}

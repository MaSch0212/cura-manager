namespace CuraManager.Services.Slicers;

/// <summary>
/// <see cref="ISlicerProvider"/> implementation for Anycubic Slicer Next.
/// </summary>
public class AnycubicSlicerProvider : OrcaFamilySlicerProvider
{
    public const string ProviderId = "anycubic";

    public override string Id => ProviderId;
    public override string DisplayName => "Anycubic Slicer Next";
    public override string IconResourceKey => "AnycubicIcon";

    // Known-good shipped version at the time of writing.
    public override Version LatestSupportedVersion { get; } = new Version(1, 4, 1, 2);

    protected override string AppKey => "AnycubicSlicerNext";

    protected override string[] ExecutableFileNames => ["AnycubicSlicerNext.exe"];

    protected override string InstallDirNameFilter => "anycubic";

    protected override string SliceInfoHeaderPrefix => "X-ACNext-";
}

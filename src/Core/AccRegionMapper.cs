using System;
using Autodesk.Revit.DB;

namespace SAUDICO.Federate.Core;

/// <summary>
/// Maps the APS hub "region" attribute (the raw string APS returns, e.g.
/// "US"/"EMEA") to the exact Revit API cloud-region constant required by
/// ModelPathUtils.ConvertCloudGUIDsToCloudPath. Only the two officially
/// documented Revit API region constants (ModelPathUtils.CloudRegionUS /
/// CloudRegionEMEA) are ever produced — any other APS region value
/// (including newer APS platform region codes this session could not
/// verify against an equivalent Revit-API-accepted literal, e.g.
/// AUS/CAN/DEU/GBR/IND/JPN) is refused rather than guessed.
/// </summary>
public static class AccRegionMapper
{
    public static bool IsSupportedRegion(string? apsRegion) =>
        apsRegion == "US" || apsRegion == "EMEA";

    /// <summary>Only call after <see cref="IsSupportedRegion"/> has confirmed the value.</summary>
    public static string ToRevitRegionConstant(string? apsRegion) => apsRegion switch
    {
        "US" => ModelPathUtils.CloudRegionUS,
        "EMEA" => ModelPathUtils.CloudRegionEMEA,
        _ => throw new ArgumentOutOfRangeException(nameof(apsRegion), apsRegion, "Unsupported ACC region."),
    };
}

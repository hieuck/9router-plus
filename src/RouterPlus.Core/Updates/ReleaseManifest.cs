namespace RouterPlus.Core.Updates;

public sealed record ReleaseManifest(
    ReleaseVersion Version,
    string Channel,
    string AssetName,
    string Sha256,
    string Publisher,
    string Signature)
{
    public string SigningPayload => string.Join("\n",
        Version.ToString(),
        Channel,
        AssetName,
        Sha256.ToLowerInvariant(),
        Publisher);
}

namespace LocalChromeStore.Services.Crx;

public sealed record Crx3PackageResult(
    string CrxPath,
    string ExtensionId,
    string PublicKeySha256,
    string PackageSha256,
    long SizeBytes);

public sealed record Crx3VerificationResult(
    string ExtensionId,
    string PublicKeySha256,
    bool SignatureValid,
    bool ExtensionIdMatchesPublicKey,
    int ZipPayloadOffset,
    int ZipPayloadLength);

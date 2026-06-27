using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LocalChromeStore.Services.Crx;

public static class Crx3PackageService
{
    private static readonly byte[] Magic = [(byte)'C', (byte)'r', (byte)'2', (byte)'4'];
    private static readonly byte[] SignatureContext = Encoding.UTF8.GetBytes("CRX3 SignedData\0");

    public static Crx3PackageResult PackDirectory(
        string extensionRoot,
        string privateKeyPemPath,
        string outputCrxPath,
        string? expectedPublicKeySha256 = null)
    {
        if (string.IsNullOrWhiteSpace(extensionRoot))
            throw new ArgumentException("Extension root is required.", nameof(extensionRoot));
        if (string.IsNullOrWhiteSpace(privateKeyPemPath))
            throw new ArgumentException("Private key path is required.", nameof(privateKeyPemPath));
        if (string.IsNullOrWhiteSpace(outputCrxPath))
            throw new ArgumentException("Output CRX path is required.", nameof(outputCrxPath));

        var root = Path.GetFullPath(extensionRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Extension root was not found: {root}");
        if (!File.Exists(Path.Combine(root, "manifest.json")))
            throw new InvalidOperationException("CRX3 packaging requires manifest.json at the extension root.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputCrxPath))!);
        using var rsa = LoadPrivateKey(privateKeyPemPath);
        var zipBytes = CreateZipArchive(root);
        var crxBytes = CreateCrx3(zipBytes, rsa, expectedPublicKeySha256, out var extensionId, out var publicKeySha256);
        File.WriteAllBytes(outputCrxPath, crxBytes);

        return new Crx3PackageResult(
            Path.GetFullPath(outputCrxPath),
            extensionId,
            publicKeySha256,
            Convert.ToHexStringLower(SHA256.HashData(crxBytes)),
            crxBytes.LongLength);
    }

    public static byte[] CreateCrx3(
        byte[] zipArchive,
        RSA privateKey,
        string? expectedPublicKeySha256,
        out string extensionId,
        out string publicKeySha256)
    {
        ArgumentNullException.ThrowIfNull(zipArchive);
        ArgumentNullException.ThrowIfNull(privateKey);
        if (zipArchive.Length == 0)
            throw new ArgumentException("ZIP payload is empty.", nameof(zipArchive));
        if (privateKey.KeySize < 2048)
            throw new InvalidOperationException("CRX3 signing requires an RSA key of at least 2048 bits.");

        var publicKey = privateKey.ExportSubjectPublicKeyInfo();
        extensionId = DeriveExtensionId(publicKey);
        publicKeySha256 = Convert.ToHexStringLower(SHA256.HashData(publicKey));
        EnsureSameSigningKey(expectedPublicKeySha256, publicKeySha256);

        var signedHeaderData = EncodeMessage([EncodeBytesField(1, GetCrxIdBytes(publicKey))]);
        Span<byte> signedHeaderSize = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(signedHeaderSize, signedHeaderData.Length);

        var signatureInput = Concat(SignatureContext, signedHeaderSize.ToArray(), signedHeaderData, zipArchive);
        var signature = privateKey.SignData(signatureInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var proof = EncodeMessage([
            EncodeBytesField(1, publicKey),
            EncodeBytesField(2, signature)
        ]);
        var header = EncodeMessage([
            EncodeBytesField(2, proof),
            EncodeBytesField(10000, signedHeaderData)
        ]);

        Span<byte> headerSize = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(headerSize, header.Length);
        return Concat(Magic, [3, 0, 0, 0], headerSize.ToArray(), header, zipArchive);
    }

    public static Crx3VerificationResult VerifyPackage(byte[] crxBytes)
    {
        var parsed = ParseCrx3(crxBytes);
        var crxId = ParseSignedDataCrxId(parsed.SignedHeaderData);
        var expectedCrxId = GetCrxIdBytes(parsed.PublicKey);
        var idMatchesKey = crxId.SequenceEqual(expectedCrxId);
        var extensionId = ExtensionIdFromCrxIdBytes(crxId);
        var publicKeySha256 = Convert.ToHexStringLower(SHA256.HashData(parsed.PublicKey));

        Span<byte> signedHeaderSize = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(signedHeaderSize, parsed.SignedHeaderData.Length);
        var zipPayload = crxBytes.AsSpan(parsed.ZipPayloadOffset, parsed.ZipPayloadLength).ToArray();
        var signatureInput = Concat(SignatureContext, signedHeaderSize.ToArray(), parsed.SignedHeaderData, zipPayload);

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(parsed.PublicKey, out _);
        var valid = rsa.VerifyData(signatureInput, parsed.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return new Crx3VerificationResult(
            extensionId,
            publicKeySha256,
            valid,
            idMatchesKey,
            parsed.ZipPayloadOffset,
            parsed.ZipPayloadLength);
    }

    public static string DeriveExtensionId(byte[] subjectPublicKeyInfo)
        => ExtensionIdFromCrxIdBytes(GetCrxIdBytes(subjectPublicKeyInfo));

    public static bool IsValidExtensionId(string? extensionId)
    {
        if (extensionId is not { Length: 32 }) return false;
        foreach (var c in extensionId)
        {
            if (c < 'a' || c > 'p') return false;
        }
        return true;
    }

    public static void EnsureSameSigningKey(string? expectedPublicKeySha256, string actualPublicKeySha256)
    {
        if (string.IsNullOrWhiteSpace(expectedPublicKeySha256)) return;
        if (!string.Equals(expectedPublicKeySha256.Trim(), actualPublicKeySha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to package update: the CRX signing key does not match the installed extension key. " +
                "Chrome updates require every version of an extension to use the same signing key.");
        }
    }

    private static RSA LoadPrivateKey(string privateKeyPemPath)
    {
        if (!File.Exists(privateKeyPemPath))
            throw new FileNotFoundException("CRX3 private key was not found.", privateKeyPemPath);

        var pem = File.ReadAllText(privateKeyPemPath);
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            throw new InvalidOperationException("CRX3 signing key must be an RSA private key PEM.", ex);
        }

        if (rsa.KeySize < 2048)
        {
            rsa.Dispose();
            throw new InvalidOperationException("CRX3 signing requires an RSA key of at least 2048 bits.");
        }

        return rsa;
    }

    private static byte[] CreateZipArchive(string root)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    throw new InvalidOperationException($"Refusing to package path outside extension root: {file}");

                var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                using var input = File.OpenRead(file);
                using var output = entry.Open();
                input.CopyTo(output);
            }
        }
        return ms.ToArray();
    }

    private static byte[] GetCrxIdBytes(byte[] subjectPublicKeyInfo)
        => SHA256.HashData(subjectPublicKeyInfo).Take(16).ToArray();

    private static string ExtensionIdFromCrxIdBytes(byte[] crxId)
    {
        if (crxId.Length != 16)
            throw new ArgumentException("CRX ID must be exactly 16 bytes.", nameof(crxId));

        Span<char> chars = stackalloc char[32];
        for (var i = 0; i < crxId.Length; i++)
        {
            chars[i * 2] = (char)('a' + (crxId[i] >> 4));
            chars[i * 2 + 1] = (char)('a' + (crxId[i] & 0x0F));
        }
        return new string(chars);
    }

    private static ParsedCrx3 ParseCrx3(byte[] crxBytes)
    {
        ArgumentNullException.ThrowIfNull(crxBytes);
        if (crxBytes.Length < 16 ||
            crxBytes[0] != Magic[0] ||
            crxBytes[1] != Magic[1] ||
            crxBytes[2] != Magic[2] ||
            crxBytes[3] != Magic[3])
        {
            throw new InvalidOperationException("Not a valid CRX file (magic mismatch).");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(crxBytes.AsSpan(4, 4));
        if (version != 3)
            throw new InvalidOperationException($"Expected CRX3 package, found CRX version {version}.");

        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(crxBytes.AsSpan(8, 4));
        if (headerLength <= 0 || headerLength > crxBytes.Length - 12)
            throw new InvalidOperationException("CRX3 header length is invalid.");

        var header = crxBytes.AsSpan(12, headerLength).ToArray();
        var zipOffset = 12 + headerLength;
        var zipLength = crxBytes.Length - zipOffset;
        if (zipLength <= 0)
            throw new InvalidOperationException("CRX3 package has no ZIP payload.");

        byte[]? publicKey = null;
        byte[]? signature = null;
        byte[]? signedHeaderData = null;
        foreach (var field in ReadLengthDelimitedFields(header))
        {
            if (field.Number == 2)
            {
                foreach (var proofField in ReadLengthDelimitedFields(field.Value))
                {
                    if (proofField.Number == 1) publicKey = proofField.Value;
                    if (proofField.Number == 2) signature = proofField.Value;
                }
            }
            else if (field.Number == 10000)
            {
                signedHeaderData = field.Value;
            }
        }

        return new ParsedCrx3(
            publicKey ?? throw new InvalidOperationException("CRX3 header is missing RSA public key proof."),
            signature ?? throw new InvalidOperationException("CRX3 header is missing RSA signature proof."),
            signedHeaderData ?? throw new InvalidOperationException("CRX3 header is missing signed header data."),
            zipOffset,
            zipLength);
    }

    private static byte[] ParseSignedDataCrxId(byte[] signedHeaderData)
    {
        foreach (var field in ReadLengthDelimitedFields(signedHeaderData))
        {
            if (field.Number == 1)
            {
                if (field.Value.Length != 16)
                    throw new InvalidOperationException("CRX3 signed header contains an invalid extension ID length.");
                return field.Value;
            }
        }
        throw new InvalidOperationException("CRX3 signed header is missing extension ID.");
    }

    private static IEnumerable<ProtoBytesField> ReadLengthDelimitedFields(byte[] message)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var fieldNumber = checked((int)(key >> 3));
            var wireType = key & 0x07;
            if (wireType != 2)
                throw new InvalidOperationException($"Unsupported CRX3 protobuf wire type {wireType}.");

            var length = checked((int)ReadVarint(message, ref offset));
            if (length < 0 || offset + length > message.Length)
                throw new InvalidOperationException("CRX3 protobuf length exceeds message bounds.");

            var value = new byte[length];
            Buffer.BlockCopy(message, offset, value, 0, length);
            offset += length;
            yield return new ProtoBytesField(fieldNumber, value);
        }
    }

    private static byte[] EncodeMessage(IEnumerable<byte[]> fields)
    {
        using var ms = new MemoryStream();
        foreach (var field in fields)
            ms.Write(field);
        return ms.ToArray();
    }

    private static byte[] EncodeBytesField(int fieldNumber, byte[] value)
    {
        using var ms = new MemoryStream();
        WriteVarint(ms, ((ulong)fieldNumber << 3) | 2);
        WriteVarint(ms, (ulong)value.Length);
        ms.Write(value);
        return ms.ToArray();
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static ulong ReadVarint(byte[] data, ref int offset)
    {
        ulong result = 0;
        var shift = 0;
        while (offset < data.Length)
        {
            var b = data[offset++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63)
                throw new InvalidOperationException("CRX3 protobuf varint is too large.");
        }
        throw new InvalidOperationException("CRX3 protobuf varint is truncated.");
    }

    private static byte[] Concat(params byte[][] chunks)
    {
        var length = chunks.Sum(chunk => chunk.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    private sealed record ParsedCrx3(
        byte[] PublicKey,
        byte[] Signature,
        byte[] SignedHeaderData,
        int ZipPayloadOffset,
        int ZipPayloadLength);

    private sealed record ProtoBytesField(int Number, byte[] Value);
}

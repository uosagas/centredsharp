using System.Buffers.Binary;
using System.Security.Cryptography;
using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Utility;

namespace CentrED.Tests;

/// <summary>
/// Round-trip tests for the UO-Sagas .sag support patched into the ClassicUO
/// DLLs (tools/cuo-sag). Fixtures are generated in-test following the two
/// documented on-disk formats, so no real game assets are required.
/// </summary>
public class SagCryptoTests : IDisposable
{
    // Must match SagCrypto.AesCtrKey in the patched ClassicUO.IO (and the
    // UO-Sagas game client).
    private static readonly byte[] AesCtrKey =
        Convert.FromHexString("65b1ebe3aa584031f7b6b0b59a3196ccba460c04b4c3e71881a96a1902e74c94");

    private readonly string _dir;

    public SagCryptoTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "centred-sag-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // best effort
        }
    }

    private static byte[] MakePayload(int size)
    {
        var payload = new byte[size];
        new Random(1234).NextBytes(payload);
        return payload;
    }

    /// Legacy scheme: key(32) | IV(16) | plaintext size int64 LE(8) | AES-256-CBC ciphertext (zero padding).
    private static byte[] EncryptCbc(byte[] payload)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.Zeros;
        aes.GenerateKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(payload, 0, payload.Length);

        using var ms = new MemoryStream();
        ms.Write(aes.Key);
        ms.Write(aes.IV);
        Span<byte> sizeBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, payload.Length);
        ms.Write(sizeBytes);
        ms.Write(ciphertext);
        return ms.ToArray();
    }

    /// CTR scheme: ciphertext | trailer { size u64 LE(8), SHA256(first min(size,4096) ciphertext bytes + key)(32), nonce(12) }.
    private static byte[] EncryptCtr(byte[] payload)
    {
        var nonce = new byte[12];
        new Random(5678).NextBytes(nonce);

        var ciphertext = CtrTransform(payload, AesCtrKey, nonce);

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(ciphertext, 0, Math.Min(ciphertext.Length, 4096));
        digest.AppendData(AesCtrKey);

        using var ms = new MemoryStream();
        ms.Write(ciphertext);
        Span<byte> sizeBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(sizeBytes, (ulong)payload.Length);
        ms.Write(sizeBytes);
        ms.Write(digest.GetHashAndReset());
        ms.Write(nonce);
        return ms.ToArray();
    }

    /// Standard CTR construction: AES-ECB over counter blocks (nonce | 32-bit BE counter), XORed with the data.
    private static byte[] CtrTransform(byte[] data, byte[] key, byte[] nonce)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor(key, new byte[16]);

        var result = new byte[data.Length];
        var counterBlock = new byte[16];
        var keystream = new byte[16];
        nonce.CopyTo(counterBlock, 0);

        for (int block = 0; block * 16 < data.Length; block++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(counterBlock.AsSpan(12), (uint)block);
            encryptor.TransformBlock(counterBlock, 0, 16, keystream, 0);

            int offset = block * 16;
            int n = Math.Min(16, data.Length - offset);
            for (int i = 0; i < n; i++)
            {
                result[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            }
        }
        return result;
    }

    private string WriteSag(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] ReadAll(UOFile file)
    {
        var result = new byte[file.Length];
        file.Read(result);
        return result;
    }

    [Theory]
    [InlineData(100_000)] // not a multiple of the AES block size
    [InlineData(4096)]    // exactly one CTR page
    [InlineData(16)]      // single block
    public void CbcSag_RoundTrips(int size)
    {
        var payload = MakePayload(size);
        var path = WriteSag($"cbc{size}.sag", EncryptCbc(payload));

        using var file = new UOFile(path);
        Assert.Equal(payload.Length, file.Length);
        Assert.Equal(payload, ReadAll(file));
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(4096)]
    [InlineData(16)]
    public void CtrSag_RoundTrips(int size)
    {
        var payload = MakePayload(size);
        var path = WriteSag($"ctr{size}.sag", EncryptCtr(payload));

        using var file = new UOFile(path);
        Assert.Equal(payload.Length, file.Length);
        Assert.Equal(payload, ReadAll(file));
    }

    [Fact]
    public void GetPlaintextSize_ReportsStoredSize_ForBothSchemes()
    {
        var payload = MakePayload(100_000);

        Assert.Equal(100_000, SagCrypto.GetPlaintextSize(WriteSag("cbc.sag", EncryptCbc(payload))));
        Assert.Equal(100_000, SagCrypto.GetPlaintextSize(WriteSag("ctr.sag", EncryptCtr(payload))));
    }

    [Fact]
    public void GarbageSag_Throws()
    {
        var path = WriteSag("garbage.sag", MakePayload(1000));

        Assert.Throws<InvalidDataException>(() => new UOFile(path));
    }

    [Fact]
    public void GetPlaintextSize_ReturnsMinusOne_ForGarbage()
    {
        Assert.Equal(-1, SagCrypto.GetPlaintextSize(WriteSag("garbage2.sag", MakePayload(1000))));
    }

    [Fact]
    public void PlainMulFiles_AreUntouched()
    {
        var payload = MakePayload(5000);
        var path = Path.Combine(_dir, "plain.mul");
        File.WriteAllBytes(path, payload);

        using var file = new UOFile(path);
        Assert.Equal(payload.Length, file.Length);
        Assert.Equal(payload, ReadAll(file));
    }

    [Fact]
    public void GetUOFilePath_PrefersSag_FallsBackToMul()
    {
        var mulPath = Path.Combine(_dir, "tiledata.mul");
        var sagPath = Path.Combine(_dir, "tiledata.sag");
        File.WriteAllBytes(mulPath, MakePayload(100));
        File.WriteAllBytes(sagPath, MakePayload(100));

        var manager = new UOFileManager(ClientVersion.CV_7090, _dir);

        // .sag wins even when both exist — the game client only reads .sag,
        // so a plain .mul next to it may be stale.
        Assert.Equal(sagPath, manager.GetUOFilePath("tiledata.mul"));

        File.Delete(sagPath);
        Assert.Equal(mulPath, manager.GetUOFilePath("tiledata.mul"));

        // non-.mul names are never remapped
        Assert.Equal(Path.Combine(_dir, "multi.idx"), manager.GetUOFilePath("multi.idx"));
    }
}

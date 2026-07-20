// SPDX-License-Identifier: BSD-2-Clause
//
// UO-Sagas encrypted asset (.sag) support.
// Ported from the UO-Sagas ClassicUO client (src/ClassicUO.IO/UOFileSag.cs and
// AES256_CTR.cs, author of the CTR scheme: Max Kellermann) with the lazy
// page-decryption mode removed — CentrED# decrypts eagerly.
//
// Two on-disk formats are supported and auto-detected per file:
//
//   1. AES-256-CTR ("trailer" scheme): ciphertext (same length as plaintext),
//      followed by a 52-byte trailer: Size u64 LE | Digest (SHA-256 over the
//      first min(Size, 4096) ciphertext bytes + key) | 12-byte nonce.
//      The key is not stored in the file; it is compiled in below.
//
//   2. Legacy AES-256-CBC ("key in file"): 56-byte header: key (32) | IV (16) |
//      plaintext size i64 LE (8), followed by CBC ciphertext, zero padding.
//
// Detection tries the CTR trailer first (bounds + digest/key match), then
// falls back to the CBC header, mirroring UOFileSag.Decrypt in the game client.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ClassicUO.IO
{
    public static unsafe class SagCrypto
    {
        public const string Extension = ".sag";

        private const int CBC_KEY_SIZE = 32;
        private const int CBC_IV_SIZE = 16;
        private const int CBC_FILE_SIZE = 8;
        private const int CBC_HEADER_SIZE = CBC_KEY_SIZE + CBC_IV_SIZE + CBC_FILE_SIZE;

        /* Must stay in sync with the key compiled into the UO-Sagas game
           client (UOFileSag.AesCtrKey). If the client rotates keys, mirror
           the change here. */
        private static readonly byte[] AesCtrKey =
        {
            0x65, 0xb1, 0xeb, 0xe3, 0xaa, 0x58, 0x40, 0x31, 0xf7, 0xb6, 0xb0, 0xb5, 0x9a, 0x31, 0x96, 0xcc,
            0xba, 0x46, 0x0c, 0x04, 0xb4, 0xc3, 0xe7, 0x18, 0x81, 0xa9, 0x6a, 0x19, 0x02, 0xe7, 0x4c, 0x94,
        };

        public static bool IsSagPath(string path)
        {
            return string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);
        }

        /**
         * Decrypt an in-memory .sag image. Returns the plaintext in a new
         * HGlobal allocation (caller frees) and its logical length, or
         * (IntPtr.Zero, 0) if the data matches neither scheme.
         */
        public static (IntPtr Allocation, long PlaintextLength) Decrypt(byte* src, long srcLength)
        {
            var (data, fileSize) = DecryptAES256_CTR(src, srcLength);
            if (data == IntPtr.Zero)
            {
                /* fall back to the old encryption scheme */
                (data, fileSize) = DecryptOld(src, srcLength);
            }

            return (data, fileSize);
        }

        /**
         * Peek at a .sag file and report its plaintext size without
         * decrypting the payload. Returns -1 if the file matches neither
         * scheme. Used e.g. for client version detection from tiledata size.
         */
        public static long GetPlaintextSize(string path)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long length = stream.Length;

            /* AES-256-CTR trailer? */
            if (length >= sizeof(AES256_CTR_EncryptionTrailer))
            {
                long payload = length - sizeof(AES256_CTR_EncryptionTrailer);
                Span<byte> trailerBytes = stackalloc byte[sizeof(AES256_CTR_EncryptionTrailer)];
                stream.Seek(payload, SeekOrigin.Begin);
                ReadUntilFinished(stream, trailerBytes);

                ulong size = BinaryPrimitives.ReadUInt64LittleEndian(trailerBytes);
                if (size <= (ulong)payload && size + AES256_CTR.PAGE_SIZE > (ulong)payload)
                {
                    var firstPage = new byte[Math.Min(size, AES256_CTR.PAGE_SIZE)];
                    stream.Seek(0, SeekOrigin.Begin);
                    ReadUntilFinished(stream, firstPage);

                    if (SelectKey(firstPage, trailerBytes.Slice(sizeof(ulong), SHA256.HashSizeInBytes)) != null)
                    {
                        return (long)size;
                    }
                }
            }

            /* legacy CBC header? */
            if (length >= CBC_HEADER_SIZE)
            {
                Span<byte> sizeBytes = stackalloc byte[CBC_FILE_SIZE];
                stream.Seek(CBC_KEY_SIZE + CBC_IV_SIZE, SeekOrigin.Begin);
                ReadUntilFinished(stream, sizeBytes);

                long fileSize = BinaryPrimitives.ReadInt64LittleEndian(sizeBytes);
                if (fileSize >= 0 && fileSize <= length - CBC_HEADER_SIZE)
                {
                    return fileSize;
                }
            }

            return -1;
        }

        /**
         * Was the fileDigest generated using the specified key?
         */
        private static bool MatchKey(ReadOnlySpan<byte> src, ReadOnlySpan<byte> fileDigest, byte[] key)
        {
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha256.AppendData(src);
            sha256.AppendData(key);
            var myDigest = sha256.GetHashAndReset();
            return myDigest.AsSpan().SequenceEqual(fileDigest);
        }

        /**
         * Check which key was used to generate the fileDigest.
         * Returns null if no key was recognized.
         */
        private static byte[] SelectKey(ReadOnlySpan<byte> src, ReadOnlySpan<byte> fileDigest)
        {
            if (MatchKey(src, fileDigest, AesCtrKey))
            {
                return AesCtrKey;
            }

            /* here we could check additional keys if more are being
               used in the field eventually */

            return null;
        }

        private static (IntPtr, long) DecryptAES256_CTR(byte* src, long srcLength)
        {
            Debug.Assert(AesCtrKey.Length == AES256_CTR.KEY_SIZE);

            long size = srcLength - sizeof(AES256_CTR_EncryptionTrailer);
            if (size < 0)
            {
                return (IntPtr.Zero, 0);
            }

            var trailer = (AES256_CTR_EncryptionTrailer*)(src + size);
            if (trailer->Size > (ulong)size || trailer->Size + AES256_CTR.PAGE_SIZE <= (ulong)size)
            {
                return (IntPtr.Zero, 0);
            }

            var key = SelectKey(
                new ReadOnlySpan<byte>(src, (int)Math.Min(trailer->Size, AES256_CTR.PAGE_SIZE)),
                new ReadOnlySpan<byte>(trailer->Digest, SHA256.HashSizeInBytes));

            if (key == null)
            {
                return (IntPtr.Zero, 0);
            }

            IntPtr dest = Marshal.AllocHGlobal((IntPtr)trailer->Size);

            try
            {
                using var aes = new AES256_CTR(key, new ReadOnlySpan<byte>(trailer->Nonce, AES256_CTR.NONCE_SIZE));
                aes.Transform((byte*)dest, src, trailer->Size, 0);
                return (dest, (long)trailer->Size);
            }
            catch
            {
                Marshal.FreeHGlobal(dest);
                throw;
            }
        }

        private static (IntPtr, long) DecryptOld(byte* src, long srcLength)
        {
            if (srcLength < CBC_HEADER_SIZE)
            {
                return (IntPtr.Zero, 0);
            }

            using var stream = new UnmanagedMemoryStream(src, srcLength);

            var keyBytes = new byte[CBC_KEY_SIZE];
            ReadUntilFinished(stream, keyBytes);

            var ivBytes = new byte[CBC_IV_SIZE];
            ReadUntilFinished(stream, ivBytes);

            Span<byte> fileSizeBytes = stackalloc byte[CBC_FILE_SIZE];
            ReadUntilFinished(stream, fileSizeBytes);

            long fileSize = BinaryPrimitives.ReadInt64LittleEndian(fileSizeBytes);
            if (fileSize < 0 || fileSize > srcLength - CBC_HEADER_SIZE)
            {
                /* malformed header */
                return (IntPtr.Zero, 0);
            }

            using Aes aes = Aes.Create();
            aes.Padding = PaddingMode.Zeros;
            aes.Mode = CipherMode.CBC;

            using ICryptoTransform decryptor = aes.CreateDecryptor(keyBytes, ivBytes);

            int size = (int)(srcLength - CBC_HEADER_SIZE);
            IntPtr data = Marshal.AllocHGlobal(size);

            try
            {
                using var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read);
                ReadUntilFinished(cs, new Span<byte>((void*)data, size));
                cs.Flush();
            }
            catch
            {
                Marshal.FreeHGlobal(data);
                throw;
            }

            return (data, fileSize);
        }

        private static void ReadUntilFinished(Stream readStream, Span<byte> destination)
        {
            int bytesRead;
            int totalBytesRead = 0;
            while (totalBytesRead < destination.Length
                   && (bytesRead = readStream.Read(destination[totalBytesRead..])) > 0)
            {
                totalBytesRead += bytesRead;
            }
        }
    }

    /**
     * This structure follows the encrypted payload and describes how
     * to decrypt the payload.
     */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct AES256_CTR_EncryptionTrailer
    {
        /**
         * The size of the plaintext payload in bytes.
         */
        public ulong Size;

        public fixed byte Digest[SHA256.HashSizeInBytes];

        public fixed byte Nonce[AES256_CTR.NONCE_SIZE];
    }

    /// <summary>
    ///     A simplified AES256-CTR implementation.
    ///     author: Max Kellermann &lt;max.kellermann@gmail.com&gt;
    /// </summary>
    internal sealed class AES256_CTR : IDisposable
    {
        public const int KEY_SIZE = 32;
        public const int BLOCK_SIZE = 16;
        public const int COUNTER_SIZE = 4;
        public const int NONCE_SIZE = BLOCK_SIZE - COUNTER_SIZE;
        public const int PAGE_SIZE = 4096;

        private readonly Aes aes = Aes.Create();
        private readonly ICryptoTransform cryptoTransform;

        private readonly byte[] noncePage = new byte[PAGE_SIZE];
        private readonly byte[] xorPage = new byte[PAGE_SIZE];

        public AES256_CTR(byte[] key, ReadOnlySpan<byte> nonce)
        {
            Debug.Assert(key.Length == KEY_SIZE);
            Debug.Assert(nonce.Length == NONCE_SIZE);

            aes.KeySize = KEY_SIZE * 8;
            aes.BlockSize = BLOCK_SIZE * 8;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            /* we use AES-ECB with a zero nonce to encrypt the nonce plus
               the counter and then XOR it with the plain text */
            var zeroIV = new byte[BLOCK_SIZE];
            cryptoTransform = aes.CreateEncryptor(key, zeroIV);

            Span<byte> nonceSpan = noncePage;
            for (int i = 0; i < PAGE_SIZE; i += BLOCK_SIZE)
                nonce.CopyTo(nonceSpan.Slice(i, NONCE_SIZE));
        }

        public void Dispose()
        {
            cryptoTransform.Dispose();
            aes.Dispose();
        }

        /**
         * Set the counters in each nonce of the specified page.  Returns
         * the first counter value for the next page.
         */
        private static unsafe uint SetNoncePageCounters(byte* noncePage, uint counter)
        {
            for (uint end = counter + PAGE_SIZE / BLOCK_SIZE; counter != end; ++counter)
            {
                /* skip the actual nonce */
                noncePage += NONCE_SIZE;

                /* now write the 32 bit counter in big-endian */
                *noncePage++ = (byte)(counter >> 24);
                *noncePage++ = (byte)(counter >> 16);
                *noncePage++ = (byte)(counter >> 8);
                *noncePage++ = (byte)counter;
            }

            return counter;
        }

        private static unsafe void XorBuffer(byte* dest, byte* a, byte* b, int size)
        {
            for (; size > 0; --size)
            {
                *dest++ = (byte)(*a++ ^ *b++);
            }
        }

        private static unsafe void Transform(byte* dest, byte* src, byte[] noncePage, byte* noncePagePtr, byte[] xorPage, byte* xorPagePtr, ulong size, ICryptoTransform cryptoTransform, uint counter)
        {
            while (size > 0)
            {
                counter = SetNoncePageCounters(noncePagePtr, counter);
                cryptoTransform.TransformBlock(noncePage, 0, PAGE_SIZE, xorPage, 0);

                int n = (int)Math.Min(size, PAGE_SIZE);
                XorBuffer(dest, src, xorPagePtr, n);
                dest += n;
                src += n;
                size -= (ulong)n;
            }
        }

        public unsafe void Transform(byte* dest, byte* src, ulong size, uint counter)
        {
            fixed (byte* noncePagePtr = noncePage)
            {
                fixed (byte* xorPagePtr = xorPage)
                {
                    Transform(dest, src, noncePage, noncePagePtr, xorPage, xorPagePtr, size, cryptoTransform, counter);
                }
            }
        }
    }
}

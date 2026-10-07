using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MoneySpend.Services;

/// File layout:
/// "MSBK"(4) | version(1) | salt(16) | nonce(12) | tag(16) | ciphertext
/// Header (everything before the tag) is bound as AES-GCM associated data.
public static class BackupCrypto
{
    private static readonly byte[] Magic =
    {
        (byte)'M', (byte)'S', (byte)'B', (byte)'K'
    };

    private const byte FormatVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 310_000;

    private const int HeaderSize =
        4 + 1 + SaltSize + NonceSize;

    public static byte[] Encrypt(byte[] plain, string passphrase)
    {
        // Check AES-GCM support before encryption
        if (!AesGcm.IsSupported)
            throw new DriveBackupException(
                DriveErrorKind.Api,
                "Encryption is not supported on this device.");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        var header = new byte[HeaderSize];

        Magic.CopyTo(header, 0);
        header[4] = FormatVersion;
        salt.CopyTo(header, 5);
        nonce.CopyTo(header, 5 + SaltSize);

        var compressed = Gzip(plain);
        var cipher = new byte[compressed.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(
                nonce,
                compressed,
                cipher,
                tag,
                header);
        }

        var output = new byte[
            HeaderSize +
            TagSize +
            cipher.Length];

        header.CopyTo(output, 0);
        tag.CopyTo(output, HeaderSize);
        cipher.CopyTo(output, HeaderSize + TagSize);

        return output;
    }

    public static byte[] Decrypt(byte[] data, string passphrase)
    {
        // Check AES-GCM support before decryption
        if (!AesGcm.IsSupported)
            throw new DriveBackupException(
                DriveErrorKind.Api,
                "Encryption is not supported on this device.");

        if (data.Length < HeaderSize + TagSize ||
            !data.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new DriveBackupException(
                DriveErrorKind.CorruptBackup,
                "This file is not a valid MoneySpend backup.");
        }

        if (data[4] != FormatVersion)
        {
            throw new DriveBackupException(
                DriveErrorKind.CorruptBackup,
                "This backup was created by a newer app version. Please update MoneySpend.");
        }

        var header = data.AsSpan(0, HeaderSize).ToArray();

        var salt = data.AsSpan(5, SaltSize);
        var nonce = data.AsSpan(5 + SaltSize, NonceSize);
        var tag = data.AsSpan(HeaderSize, TagSize);
        var cipher = data.AsSpan(HeaderSize + TagSize);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        var compressed = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);

            aes.Decrypt(
                nonce,
                cipher,
                tag,
                compressed,
                header);
        }
        catch (CryptographicException ex)
        {
            throw new DriveBackupException(
                DriveErrorKind.WrongPassphrase,
                "Wrong passphrase, or the backup file is damaged.",
                ex);
        }

        try
        {
            return Gunzip(compressed);
        }
        catch (InvalidDataException ex)
        {
            throw new DriveBackupException(
                DriveErrorKind.CorruptBackup,
                "Backup data is corrupted.",
                ex);
        }
    }

    private static byte[] Gzip(byte[] input)
    {
        using var ms = new MemoryStream();

        using (var gz = new GZipStream(
            ms,
            CompressionLevel.Optimal,
            leaveOpen: true))
        {
            gz.Write(input, 0, input.Length);
        }

        return ms.ToArray();
    }

    private static byte[] Gunzip(byte[] input)
    {
        using var src = new MemoryStream(input);
        using var gz = new GZipStream(
            src,
            CompressionMode.Decompress);

        using var dst = new MemoryStream();

        gz.CopyTo(dst);

        return dst.ToArray();
    }
}
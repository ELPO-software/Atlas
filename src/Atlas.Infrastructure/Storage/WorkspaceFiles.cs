using System.Text;
using Atlas.Core.Exchange;

namespace Atlas.Infrastructure.Storage;

/// <param name="Text">The content of the file, or null when <paramref name="Problem"/> says why it could not be read.</param>
public sealed record FileReadResult(string? Text, DocumentProblem? Problem);

/// <summary>Reads files to import and writes exported ones.</summary>
public static class WorkspaceFiles
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads at most <see cref="WorkspaceDocument.MaxBytes"/>. A larger file is refused from its size, before any of it is read.
    /// </summary>
    public static async Task<FileReadResult> ReadForImportAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                return new FileReadResult(null, DocumentProblem.FileNotFound);

            if (file.Length > WorkspaceDocument.MaxBytes)
                return new FileReadResult(null, DocumentProblem.TooLarge);

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var buffer = new byte[WorkspaceDocument.MaxBytes + 1];
            var length = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;
                if (length > WorkspaceDocument.MaxBytes)
                    return new FileReadResult(null, DocumentProblem.TooLarge);
            }

            // Many editors start UTF-8 files with a byte order mark; it is not part of the text.
            var bytes = buffer.AsSpan(0, length);
            if (bytes.StartsWith(Encoding.UTF8.Preamble))
                bytes = bytes[Encoding.UTF8.Preamble.Length..];

            return new FileReadResult(StrictUtf8.GetString(bytes), null);
        }
        catch (DecoderFallbackException)
        {
            return new FileReadResult(null, DocumentProblem.NotText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileReadResult(null, DocumentProblem.Unreadable);
        }
    }

    /// <summary>
    /// Writes the whole file or nothing: the text goes to a temporary file next to the destination, which then
    /// replaces it. An existing file is never left half written.
    /// </summary>
    public static async Task WriteAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, text, StrictUtf8, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

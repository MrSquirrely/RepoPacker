using System.Buffers;
using System.IO;
using System.IO.Enumeration;
using System.Security;
using System.Text;
using System.Xml;

using LibGit2Sharp;

namespace RepoPacker.UI;

public sealed class RepoPackerService {
    private const string CDataStart = "<![CDATA[";
    private const string CDataEnd = "]]>";
    private const char ReplacementChar = '�';

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding LegacyFallbackEncoding = CreateLegacyFallbackEncoding();

    public async Task ProcessRepositoryAsync(
        string repoUrl,
        string token,
        string outputFilePath,
        HashSet<string> excludedDirs,
        HashSet<string> excludedExts,
        long maxFileSizeBytes,
        IProgress<string> progress,
        CancellationToken cancellationToken) {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RepoPacker_" + Guid.NewGuid().ToString("N"));

        try {
            progress.Report("Cloning repository (shallow depth = 1)...");
            await Task.Run(() => GitCloner.CloneRepository(repoUrl, tempDirectory, token, cancellationToken), cancellationToken);

            progress.Report("Analyzing repository structure with preset filters & size limits...");
            List<string> textFilePaths = new List<string>(capacity: 512);
            ExclusionRules directoryRules = new ExclusionRules(excludedDirs);
            ExclusionRules fileRules = new ExclusionRules(excludedExts);

            // Disk-heavy stages stay off the caller's thread so the UI remains responsive
            await Task.Run(() => {
                using Repository repository = new Repository(tempDirectory);
                CollectValidFiles(tempDirectory, tempDirectory, repository, directoryRules, fileRules, maxFileSizeBytes, textFilePaths, cancellationToken);
            }, cancellationToken);

            progress.Report($"Packing {textFilePaths.Count} files into LLM digest...");
            await Task.Run(() => BuildOutputDigestAsync(tempDirectory, textFilePaths, repoUrl, outputFilePath, cancellationToken), cancellationToken);

            progress.Report("Completed successfully.");
        }
        finally {
            await Task.Run(() => DeleteDirectorySafe(tempDirectory));
        }
    }

    private void CollectValidFiles(
        string rootDirectory,
        string currentDirectory,
        Repository repository,
        ExclusionRules directoryRules,
        ExclusionRules fileRules,
        long maxFileSizeBytes,
        List<string> collectedFiles,
        CancellationToken cancellationToken) {

        foreach (string fullFilePath in Directory.EnumerateFiles(currentDirectory)) {
            cancellationToken.ThrowIfCancellationRequested();

            // Fast filter without file system metadata reads or relative path allocation
            if (fileRules.MatchesFile(Path.GetFileName(fullFilePath))) {
                continue;
            }

            if (maxFileSizeBytes > 0) {
                FileInfo fileInfo = new FileInfo(fullFilePath);
                if (fileInfo.Length > maxFileSizeBytes) {
                    continue;
                }
            }

            string relativePath = Path.GetRelativePath(rootDirectory, fullFilePath);
            if (repository.Ignore.IsPathIgnored(relativePath)) {
                continue;
            }

            if (IsBinaryFile(fullFilePath)) {
                continue;
            }

            collectedFiles.Add(relativePath);
        }

        foreach (string subDirectory in Directory.EnumerateDirectories(currentDirectory)) {
            string dirName = Path.GetFileName(subDirectory);
            if (directoryRules.MatchesName(dirName)) {
                continue;
            }

            CollectValidFiles(rootDirectory, subDirectory, repository, directoryRules, fileRules, maxFileSizeBytes, collectedFiles, cancellationToken);
        }
    }

    private static bool IsBinaryFile(string filePath) {
        const int sampleSize = 1024;
        Span<byte> buffer = stackalloc byte[sampleSize];

        using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, sampleSize, FileOptions.SequentialScan);
        int bytesRead = stream.Read(buffer);
        ReadOnlySpan<byte> sample = buffer.Slice(0, bytesRead);

        // UTF-16 text is full of zero bytes, so only a whole zero code unit signals binary content
        if (sample.StartsWith<byte>([0xFF, 0xFE]) || sample.StartsWith<byte>([0xFE, 0xFF])) {
            for (int i = 2; i + 1 < sample.Length; i += 2) {
                if (sample[i] == 0x00 && sample[i + 1] == 0x00) {
                    return true;
                }
            }

            return false;
        }

        return sample.Contains((byte)0x00);
    }

    private static Encoding CreateLegacyFallbackEncoding() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    // Byte order marks still take priority when the returned encoding is used to open the file
    private static Encoding DetectFileEncoding(string filePath, char[] scratchBuffer, CancellationToken cancellationToken) {
        using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using StreamReader reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);

        try {
            while (reader.Read(scratchBuffer, 0, scratchBuffer.Length) > 0) {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Encoding.UTF8;
        }
        catch (DecoderFallbackException) {
            return LegacyFallbackEncoding;
        }
    }

    private static string EscapeXml(string value) {
        char[] chars = value.ToCharArray();
        ReplaceInvalidXmlChars(chars);
        return SecurityElement.Escape(new string(chars));
    }

    private static void ReplaceInvalidXmlChars(Span<char> chars) {
        for (int i = 0; i < chars.Length; i++) {
            if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1])) {
                i++;
            }
            else if (!XmlConvert.IsXmlChar(chars[i])) {
                chars[i] = ReplacementChar;
            }
        }
    }

    private static async Task WriteCDataContentAsync(TextReader reader, TextWriter writer, char[] buffer, CancellationToken cancellationToken) {
        int consecutiveBrackets = 0;
        int carriedChars = 0;
        int charsRead;

        await writer.WriteAsync(CDataStart);

        while ((charsRead = await reader.ReadAsync(buffer.AsMemory(carriedChars, buffer.Length - carriedChars), cancellationToken)) > 0) {
            int available = carriedChars + charsRead;

            // Hold back a trailing high surrogate so a pair split across chunks stays intact
            carriedChars = char.IsHighSurrogate(buffer[available - 1]) ? 1 : 0;
            int writable = available - carriedChars;
            ReplaceInvalidXmlChars(buffer.AsSpan(0, writable));

            int segmentStart = 0;
            for (int i = 0; i < writable; i++) {
                char current = buffer[i];
                if (current == '>' && consecutiveBrackets >= 2) {
                    // Split the section so "]]>" in the source cannot terminate it early
                    await writer.WriteAsync(buffer.AsMemory(segmentStart, i - segmentStart), cancellationToken);
                    await writer.WriteAsync(CDataEnd);
                    await writer.WriteAsync(CDataStart);
                    segmentStart = i;
                }

                consecutiveBrackets = current == ']' ? consecutiveBrackets + 1 : 0;
            }

            await writer.WriteAsync(buffer.AsMemory(segmentStart, writable - segmentStart), cancellationToken);

            if (carriedChars > 0) {
                buffer[0] = buffer[available - 1];
            }
        }

        if (carriedChars > 0) {
            await writer.WriteAsync(ReplacementChar);
        }

        await writer.WriteAsync(CDataEnd);
    }

    private static async Task BuildOutputDigestAsync(
        string rootDirectory,
        List<string> relativeFilePaths,
        string repoUrl,
        string outputFilePath,
        CancellationToken cancellationToken) {

        relativeFilePaths.Sort(StringComparer.Ordinal);

        await using FileStream fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using StreamWriter writer = new StreamWriter(fileStream, Encoding.UTF8, bufferSize: 65536);

        await writer.WriteLineAsync("<repository>");
        await writer.WriteLineAsync("  <metadata>");
        await writer.WriteLineAsync($"    <url>{EscapeXml(repoUrl)}</url>");
        await writer.WriteLineAsync($"    <generated_at>{DateTime.UtcNow:O}</generated_at>");
        await writer.WriteLineAsync($"    <file_count>{relativeFilePaths.Count}</file_count>");
        await writer.WriteLineAsync("  </metadata>");

        await writer.WriteLineAsync("  <directory_structure>");
        foreach (string path in relativeFilePaths) {
            await writer.WriteAsync("    ");
            await writer.WriteLineAsync(EscapeXml(path));
        }
        await writer.WriteLineAsync("  </directory_structure>");

        await writer.WriteLineAsync("  <files>");
        char[] chunkBuffer = ArrayPool<char>.Shared.Rent(16384);
        try {
            foreach (string relativePath in relativeFilePaths) {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath = Path.Combine(rootDirectory, relativePath);

                await writer.WriteAsync("    <file path=\"");
                await writer.WriteAsync(EscapeXml(relativePath));
                await writer.WriteLineAsync("\">");

                Encoding fileEncoding = DetectFileEncoding(fullPath, chunkBuffer, cancellationToken);

                using (FileStream inputStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (StreamReader reader = new StreamReader(inputStream, fileEncoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096)) {
                    await WriteCDataContentAsync(reader, writer, chunkBuffer, cancellationToken);
                }

                await writer.WriteLineAsync();
                await writer.WriteLineAsync("    </file>");
            }
        }
        finally {
            ArrayPool<char>.Shared.Return(chunkBuffer);
        }

        await writer.WriteLineAsync("  </files>");
        await writer.WriteLineAsync("</repository>");
    }

    private static void DeleteDirectorySafe(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)) {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directoryPath, true);
    }

    private sealed class ExclusionRules {
        private readonly HashSet<string> _exactEntries;
        private readonly List<string> _multiPartExtensions = new List<string>();
        private readonly List<string> _wildcardPatterns = new List<string>();

        public ExclusionRules(HashSet<string> entries) {
            _exactEntries = entries;

            foreach (string entry in entries) {
                if (entry.AsSpan().IndexOfAny('*', '?') >= 0) {
                    _wildcardPatterns.Add(entry);
                }
                else if (entry.StartsWith('.') && entry.IndexOf('.', 1) > 0) {
                    _multiPartExtensions.Add(entry);
                }
            }
        }

        public bool MatchesName(string name) {
            if (_exactEntries.Contains(name)) {
                return true;
            }

            foreach (string pattern in _wildcardPatterns) {
                if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)) {
                    return true;
                }
            }

            return false;
        }

        // Path.GetExtension only returns the last segment, so ".min.js" needs a suffix comparison
        public bool MatchesFile(string fileName) {
            if (_exactEntries.Contains(Path.GetExtension(fileName)) || MatchesName(fileName)) {
                return true;
            }

            foreach (string extension in _multiPartExtensions) {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }
    }
}
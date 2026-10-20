using System.Buffers;
using System.IO;
using System.Text;

using LibGit2Sharp;

namespace RepoPacker.UI;

public sealed class RepoPackerService {
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
            await Task.Run(() => GitCloner.CloneRepository(repoUrl, tempDirectory, token), cancellationToken);

            progress.Report("Analyzing repository structure with preset filters & size limits...");
            List<string> textFilePaths = new List<string>(capacity: 512);

            using (Repository repository = new Repository(tempDirectory)) {
                CollectValidFiles(tempDirectory, tempDirectory, repository, excludedDirs, excludedExts, maxFileSizeBytes, textFilePaths);
            }

            progress.Report($"Packing {textFilePaths.Count} files into LLM digest...");
            await BuildOutputDigestAsync(tempDirectory, textFilePaths, repoUrl, outputFilePath, cancellationToken);

            progress.Report("Completed successfully.");
        }
        finally {
            DeleteDirectorySafe(tempDirectory);
        }
    }

    private void CollectValidFiles(
        string rootDirectory,
        string currentDirectory,
        Repository repository,
        HashSet<string> excludedDirs,
        HashSet<string> excludedExts,
        long maxFileSizeBytes,
        List<string> collectedFiles) {

        foreach (string fullFilePath in Directory.EnumerateFiles(currentDirectory)) {
            string fileName = Path.GetFileName(fullFilePath);
            string extension = Path.GetExtension(fullFilePath);

            // Fast filter without file system metadata reads or relative path allocation
            if (excludedExts.Contains(extension) || excludedExts.Contains(fileName)) {
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
            if (excludedDirs.Contains(dirName)) {
                continue;
            }

            CollectValidFiles(rootDirectory, subDirectory, repository, excludedDirs, excludedExts, maxFileSizeBytes, collectedFiles);
        }
    }

    private static bool IsBinaryFile(string filePath) {
        const int sampleSize = 1024;
        Span<byte> buffer = stackalloc byte[sampleSize];

        using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, sampleSize, FileOptions.SequentialScan);
        int bytesRead = stream.Read(buffer);
        return buffer.Slice(0, bytesRead).Contains((byte)0x00);
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
        await writer.WriteLineAsync($"    <url>{repoUrl}</url>");
        await writer.WriteLineAsync($"    <generated_at>{DateTime.UtcNow:O}</generated_at>");
        await writer.WriteLineAsync($"    <file_count>{relativeFilePaths.Count}</file_count>");
        await writer.WriteLineAsync("  </metadata>");

        await writer.WriteLineAsync("  <directory_structure>");
        foreach (string path in relativeFilePaths) {
            await writer.WriteAsync("    ");
            await writer.WriteLineAsync(path);
        }
        await writer.WriteLineAsync("  </directory_structure>");

        await writer.WriteLineAsync("  <files>");
        char[] chunkBuffer = ArrayPool<char>.Shared.Rent(16384);
        try {
            foreach (string relativePath in relativeFilePaths) {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath = Path.Combine(rootDirectory, relativePath);

                await writer.WriteAsync("    <file path=\"");
                await writer.WriteAsync(relativePath);
                await writer.WriteLineAsync("\">");

                using (FileStream inputStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (StreamReader reader = new StreamReader(inputStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096)) {
                    int charsRead;
                    while ((charsRead = await reader.ReadAsync(chunkBuffer.AsMemory(0, chunkBuffer.Length), cancellationToken)) > 0) {
                        await writer.WriteAsync(chunkBuffer.AsMemory(0, charsRead), cancellationToken);
                    }
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
}
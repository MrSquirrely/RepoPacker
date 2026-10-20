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
            List<string> textFilePaths = [];

            using (Repository repository = new(tempDirectory)) {
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
        DirectoryInfo directoryInfo = new(currentDirectory);

        if (excludedDirs.Contains(directoryInfo.Name)) {
            return;
        }

        collectedFiles.AddRange(from fileInfo in directoryInfo.GetFiles() where (maxFileSizeBytes <= 0) || (fileInfo.Length <= maxFileSizeBytes) where !excludedExts.Contains(fileInfo.Extension) && !excludedExts.Contains(fileInfo.Name) let relativePath = Path.GetRelativePath(rootDirectory, fileInfo.FullName) where !repository.Ignore.IsPathIgnored(relativePath) where !IsBinaryFile(fileInfo.FullName) select relativePath);

        foreach (DirectoryInfo subDirectory in directoryInfo.GetDirectories()) {
            CollectValidFiles(rootDirectory, subDirectory.FullName, repository, excludedDirs, excludedExts, maxFileSizeBytes, collectedFiles);
        }
    }

    private static bool IsBinaryFile(string filePath) {
        const int sampleSize = 1024;
        byte[] buffer = new byte[sampleSize];

        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int bytesRead = stream.Read(buffer, 0, sampleSize);
        for (int i = 0; i < bytesRead; i++) {
	        if (buffer[i] == 0x00) {
		        return true;
	        }
        }

        return false;
    }

    private static async Task BuildOutputDigestAsync(
        string rootDirectory,
        List<string> relativeFilePaths,
        string repoUrl,
        string outputFilePath,
        CancellationToken cancellationToken) {
	    await using FileStream fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
	    await using StreamWriter writer = new StreamWriter(fileStream, Encoding.UTF8);
	    await writer.WriteLineAsync("<repository>");
	    await writer.WriteLineAsync("  <metadata>");
	    await writer.WriteLineAsync($"    <url>{repoUrl}</url>");
	    await writer.WriteLineAsync($"    <generated_at>{DateTime.UtcNow:O}</generated_at>");
	    await writer.WriteLineAsync($"    <file_count>{relativeFilePaths.Count}</file_count>");
	    await writer.WriteLineAsync("  </metadata>");

	    await writer.WriteLineAsync("  <directory_structure>");
	    foreach (string path in relativeFilePaths.OrderBy(p => p)) {
		    await writer.WriteLineAsync($"    {path}");
	    }
	    await writer.WriteLineAsync("  </directory_structure>");

	    await writer.WriteLineAsync("  <files>");
	    foreach (string relativePath in relativeFilePaths) {
		    cancellationToken.ThrowIfCancellationRequested();

		    string fullPath = Path.Combine(rootDirectory, relativePath);
		    string fileContent = await File.ReadAllTextAsync(fullPath, cancellationToken);

		    await writer.WriteLineAsync($"    <file path=\"{relativePath}\">");
		    await writer.WriteLineAsync(fileContent);
		    await writer.WriteLineAsync("    </file>");
	    }
	    await writer.WriteLineAsync("  </files>");
	    await writer.WriteLineAsync("</repository>");
    }

    private static void DeleteDirectorySafe(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }

        foreach (string file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)) {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directoryPath, true);
    }
}
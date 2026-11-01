using LibGit2Sharp;

namespace RepoPacker.UI;

public static class GitCloner {
    public static void CloneRepository(string repositoryUrl, string targetDirectory, string personalAccessToken, CancellationToken cancellationToken) {
        CloneOptions cloneOptions = new() {
            Checkout = true,
            RecurseSubmodules = false,
            FetchOptions = {
                Depth = 1, // Shallow clone for minimum disk and network overhead
				CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials {
                        Username = "git",
                        Password = personalAccessToken
                    },
                // Returning false from a progress callback makes libgit2 abort the fetch
                OnProgress = _ => !cancellationToken.IsCancellationRequested,
                OnTransferProgress = _ => !cancellationToken.IsCancellationRequested
            }
        };

        try {
            Repository.Clone(repositoryUrl, targetDirectory, cloneOptions);
        }
        catch (LibGit2SharpException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}

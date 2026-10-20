using LibGit2Sharp;

namespace RepoPacker.UI;

public static class GitCloner {
    public static void CloneRepository(string repositoryUrl, string targetDirectory, string personalAccessToken) {
        CloneOptions cloneOptions = new() {
            Checkout = true,
            RecurseSubmodules = false,
            FetchOptions = {
                Depth = 1, // Shallow clone for minimum disk and network overhead
				CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials {
                        Username = "git",
                        Password = personalAccessToken
                    }
            }
        };

        Repository.Clone(repositoryUrl, targetDirectory, cloneOptions);
    }
}
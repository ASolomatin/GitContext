using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GitContext;

#pragma warning disable RS1035 // Do not use APIs banned for analyzers

internal class GitReader
{
    private const string GitDirectoryName = ".git";
    private const string HeadFileName = "HEAD";
    private const string ObjectDirectoryName = "objects";
    private const string TagDirectoryName = "refs/tags";
    private const string HeadsDirectoryName = "refs/heads";

    private static readonly Regex AuthorRegex = new(@"^(?<name>.*) <(?<email>.*)> (?<date>\d+) (?<offset>(?:\+|-)?\d{4})$", RegexOptions.Compiled);

    private readonly string? _gitDirectory;
    private readonly Lazy<Task<HeadInfo?>> _headInfo;
    private readonly Lazy<Task<CommitInfo?>> _commitInfo;
    private readonly Lazy<Task<TagInfo[]>> _tags;

    private readonly bool _throwOnError;

    public GitReader(string? initialDirectory = null, bool throwOnError = false)
    {
        _gitDirectory = FindGitDirectory(initialDirectory);
        _headInfo = new(ReadHead);
        _commitInfo = new(ReadCommit);
        _tags = new(ReadTags);

        _throwOnError = throwOnError;
    }

    public async ValueTask<string?> GetCommitHash() => (await _headInfo.Value)?.CommitHash;
    public async ValueTask<string?> GetBranch() => (await _headInfo.Value)?.Branch;
    public async ValueTask<bool> GetIsDetached() => (await _headInfo.Value)?.IsDetached ?? false;
    public async ValueTask<string?> GetCommitAuthor() => (await _commitInfo.Value)?.Author;
    public async ValueTask<DateTimeOffset?> GetCommitDate() => (await _commitInfo.Value)?.Date;
    public async ValueTask<string?> GetCommitMessage() => (await _commitInfo.Value)?.Message;
    public async ValueTask<string[]> GetCommitParents() => (await _commitInfo.Value)?.Parents ?? [];
    public async ValueTask<string[]> GetTags() => (await _tags.Value)?.Select(t => t.Tag).ToArray() ?? [];

    private string? FindGitDirectory(string? initialDirectory)
    {
        var directory = initialDirectory ?? Directory.GetCurrentDirectory();

        while (directory != null)
        {
            var possibleGitDirectory = Path.Combine(directory, GitDirectoryName);
            if (ValidateGitDirectory(possibleGitDirectory))
                return possibleGitDirectory;

            directory = Path.GetDirectoryName(directory);
        }

        return _throwOnError ? throw new InvalidOperationException("Git directory not found") : null;

        static bool ValidateGitDirectory(string directory)
        {
            var isValid = true;

            Check(() => Directory.Exists(directory));
            Check(() => File.Exists(Path.Combine(directory, HeadFileName)));
            Check(() => Directory.Exists(Path.Combine(directory, ObjectDirectoryName)));

            return isValid;

            void Check(Func<bool> check) => isValid = isValid && check();
        }
    }

    private async Task<HeadInfo?> ReadHead()
    {
        if (_gitDirectory is null)
            return _throwOnError ? throw new InvalidOperationException("Git directory not found") : null;

        var headPath = Path.Combine(_gitDirectory, HeadFileName);
        if (!File.Exists(headPath))
            return _throwOnError ? throw new InvalidOperationException("HEAD file not found") : null;

        var headContent = (await ReadAllTextAsync(headPath)).Trim();

        if (headContent.StartsWith("ref: "))
        {
            var refPath = headContent.Substring(5);
            if (!refPath.StartsWith(HeadsDirectoryName))
                return _throwOnError ? throw new InvalidOperationException("Invalid HEAD format") : null;

            var branch = refPath.Substring(HeadsDirectoryName.Length + 1);

            var refFilePath = Path.Combine(_gitDirectory, refPath.ToString());
            if (File.Exists(refFilePath))
            {
                var refContent = (await ReadAllTextAsync(refFilePath)).Trim();

                return !ValidateCommitHash(refContent)
                    ? (_throwOnError ? throw new InvalidOperationException("Invalid commit hash") : null)
                    : new HeadInfo(headContent, branch, refContent, false);
            }
        }

        return ValidateCommitHash(headContent) ?
            new HeadInfo(headContent.ToString(), null, headContent.ToString(), true) : null;
    }

    private async Task<CommitInfo?> ReadCommit()
    {
        if (_gitDirectory is null)
            return _throwOnError ? throw new InvalidOperationException("Git directory not found") : null;

        var headInfo = await _headInfo.Value;
        if (headInfo is null)
            return _throwOnError ? throw new InvalidOperationException("HEAD info not found") : null;

        string? author = null;
        DateTimeOffset? date = null;
        List<string> parents = [];

        try
        {
            var objectsPath = Path.Combine(_gitDirectory, ObjectDirectoryName);
            using var objectReader = new ObjectFileEnumerator(objectsPath, headInfo.CommitHash);

            var type = await objectReader.ReadHeaderAsync();

            if (type != "commit")
                throw new InvalidOperationException("Invalid commit object type");

            while (await objectReader.ReadValueAsync() is { } entry)
            {
                var (key, value) = (entry.Key, entry.Value);

                switch (key)
                {
                    case "author":
                        ParseAuthor(value);
                        break;
                    case "parent":
                        if (!ValidateCommitHash(value))
                            throw new InvalidOperationException("Invalid parent hash");
                        parents.Add(value);
                        break;
                    default:
                        break;
                }
            }

            var message = await objectReader.ReadMessageAsync();

            return author is null || date is null || message is null
                ? throw new InvalidOperationException("Invalid commit format")
                : new CommitInfo(headInfo.CommitHash, author, date.Value, message, [.. parents]);
        }
        catch (Exception ex)
        {
            if (_throwOnError)
                throw;

            Debug.Fail(ex.Message);
            return null;
        }

        void ParseAuthor(string authorString)
        {
            var match = AuthorRegex.Match(authorString);
            if (!match.Success)
                throw new InvalidOperationException("Invalid author format");

            author = $"{match.Groups["name"].Value} <{match.Groups["email"].Value}>";
            ParseDate(match.Groups["date"].Value, match.Groups["offset"].Value);
        }

        void ParseDate(string timestamp, string offset)
        {
            var ticks = (long.Parse(timestamp) * TimeSpan.TicksPerSecond) + 621355968000000000;
            var negativeOffset = offset[0] == '-';
            if (offset[0] is '+' or '-')
                offset = offset.Substring(1);
            var offsetSpan = TimeSpan.ParseExact(offset, "hhmm", null);
            if (negativeOffset)
                offsetSpan = -offsetSpan;
            ticks += offsetSpan.Ticks;
            date = new DateTimeOffset(ticks, offsetSpan);
        }
    }

    private async Task<TagInfo?> ReadTag(string tagName, string? commitMatch = null)
    {
        if (_gitDirectory is null)
            return _throwOnError ? throw new InvalidOperationException("Git directory not found") : null;

        var tagPath = Path.Combine(_gitDirectory, TagDirectoryName, tagName);
        if (!File.Exists(tagPath))
            return _throwOnError ? throw new InvalidOperationException("Tag file not found") : null;

        var tagContent = (await ReadAllTextAsync(tagPath)).Trim();

        if (commitMatch is not null && tagContent == commitMatch)
            return new TagInfo(commitMatch, tagName, null);

        string? commit = null;

        try
        {
            if (!ValidateCommitHash(tagContent))
                throw new InvalidOperationException("Invalid tag hash");

            var objectsPath = Path.Combine(_gitDirectory, ObjectDirectoryName);
            using var objectReader = new ObjectFileEnumerator(objectsPath, tagContent);

            var type = await objectReader.ReadHeaderAsync();

            if (type != "tag")
                return null;

            while (await objectReader.ReadValueAsync() is { } entry)
            {
                var (key, value) = (entry.Key, entry.Value);

                switch (key)
                {
                    case "object":
                        if (!ValidateCommitHash(value))
                            throw new InvalidOperationException("Invalid commit hash");
                        if (commitMatch is not null && value != commitMatch)
                            return null;
                        commit = value;
                        break;
                    case "type":
                        if (value != "commit")
                            return null;
                        break;
                    default:
                        break;
                }
            }

            var message = await objectReader.ReadMessageAsync();

            return commit is null || message is null
                ? throw new InvalidOperationException("Invalid tag format")
                : new TagInfo(commit, tagName, message);
        }
        catch (Exception ex)
        {
            if (_throwOnError)
                throw;

            Debug.Fail(ex.Message);
            return null;
        }
    }

    private async Task<TagInfo[]> ReadTags()
    {
        if (_gitDirectory is null)
            return _throwOnError ? throw new InvalidOperationException("Git directory not found") : [];

        var tagsDirectory = Path.Combine(_gitDirectory, TagDirectoryName);
        if (!Directory.Exists(tagsDirectory))
            return [];

        var head = await _headInfo.Value;
        if (head is null)
            return _throwOnError ? throw new InvalidOperationException("HEAD info not found") : [];

        var commitHash = head.CommitHash;

        var tags = new List<TagInfo>();
        foreach (var tagFile in Directory.EnumerateFiles(tagsDirectory))
        {
            var tagName = Path.GetFileName(tagFile);
            var tagInfo = await ReadTag(tagName, commitHash);
            if (tagInfo is not null)
                tags.Add(tagInfo);
        }

        return [.. tags];
    }

    private async ValueTask<string> ReadAllTextAsync(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }

    private static bool ValidateCommitHash(string hash)
        => hash.Length == 40 && hash.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private class HeadInfo(string refString, string? branch, string commitHash, bool isDetached)
    {
        public string Ref { get; } = refString;
        public string? Branch { get; } = branch;
        public string CommitHash { get; } = commitHash;
        public bool IsDetached { get; } = isDetached;
    }

    private class CommitInfo(string hash, string author, DateTimeOffset date, string message, string[] parents)
    {
        public string Hash { get; } = hash;
        public string Author { get; } = author;
        public DateTimeOffset Date { get; } = date;
        public string Message { get; } = message;
        public string[] Parents { get; } = parents;
    }

    private class TagInfo(string commit, string tag, string? message)
    {
        public string Commit { get; } = commit;
        public string Tag { get; } = tag;
        public string? Message { get; } = message;
    }
}
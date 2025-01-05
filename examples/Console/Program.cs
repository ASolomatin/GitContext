using GitContext;

Console.WriteLine($"Hash: {Git.Hash}");
Console.WriteLine($"Author: {Git.Author}");
Console.WriteLine($"Date: {Git.Date}");
Console.WriteLine($"IsDetached: {Git.IsDetached}");
Console.WriteLine($"Branch: {Git.Branch}");
Console.WriteLine($"Tags: {string.Join(", ", Git.Tags)}");
Console.WriteLine($"Parents: {string.Join(", ", Git.Parents)}");
Console.WriteLine($"Message: {Git.Message}");

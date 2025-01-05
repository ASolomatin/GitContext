[test-icon]:            https://github.com/ASolomatin/GitContext/actions/workflows/tests.yml/badge.svg?branch=main
[test-url]:             https://github.com/ASolomatin/GitContext/actions/workflows/tests.yml

[packaging-icon]:       https://github.com/ASolomatin/GitContext/actions/workflows/publish.yml/badge.svg
[packaging-url]:        https://github.com/ASolomatin/GitContext/actions/workflows/publish.yml

[license-icon]:         https://img.shields.io/github/license/ASolomatin/GitContext
[license-url]:          https://github.com/ASolomatin/GitContext/blob/master/LICENSE

[nuget-icon]:           https://img.shields.io/nuget/v/GitContext.svg
[nuget-url]:            https://www.nuget.org/packages/GitContext

[nuget-downloads-icon]: https://img.shields.io/nuget/dt/GitContext.svg
[nuget-downloads-url]:  https://www.nuget.org/stats/packages/GitContext?groupby=Version

# Git Context

[![NuGet][nuget-icon]][nuget-url]
[![NuGet downloads][nuget-downloads-icon]][nuget-downloads-url]
[![Tests][test-icon]][test-url]
[![Publish][packaging-icon]][packaging-url]
[![GitHub][license-icon]][license-url]

----------------------------------------

Provides basic compile-time information about a git repository, built in through source generation.

The Nuget package can be found [here](https://www.nuget.org/packages/GitContext)

----------------------------------------

Example Code:
```csharp
using GitContext;

Console.WriteLine($"Hash: {Git.Hash}");
Console.WriteLine($"Author: {Git.Author}");
Console.WriteLine($"Date: {Git.Date}");
Console.WriteLine($"IsDetached: {Git.IsDetached}");
Console.WriteLine($"Branch: {Git.Branch}");
Console.WriteLine($"Tags: {string.Join(", ", Git.Tags)}");
Console.WriteLine($"Parents: {string.Join(", ", Git.Parents)}");
Console.WriteLine($"Message: {Git.Message}");
```

----------------------------------------

## License

**[MIT][license-url]**

Copyright (C) 2025 Aleksej Solomatin

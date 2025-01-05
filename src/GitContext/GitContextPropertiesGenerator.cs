using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace GitContext;

/// <summary>
/// A source generator that generates properties for the Git context.
/// </summary>
[Generator]
public class GitContextPropertiesGenerator : IIncrementalGenerator
{
    private readonly FrozenDictionary<string, GitProperty> _properties = ((IEnumerable<GitProperty>)[
        new("string?", "Hash", "The commit hash"),
        new("string?", "Branch", "The branch name"),
        new("bool", "IsDetached", "Whether the repository is in detached HEAD state"),
        new("string?", "Author", "The commit author"),
        new("global::System.DateTimeOffset?", "Date", "The commit date"),
        new("string?", "Message", "The commit message"),
        new("string[]", "Parents", "The commit parents"),
        new("string[]", "Tags", "The tags")
    ]).ToFrozenDictionary(property => property.Name);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var gitReaderProvider = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) => provider.GlobalOptions.TryGetValue("build_property.projectdir", out var projectDir) ? projectDir : null)
            .Select(static (projectDir, _) => new GitReader(projectDir));

        var propertyAccesses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Expression is IdentifierNameSyntax identifierName &&
                    identifierName.Identifier.Text == "Git",
                transform: static (ctx, ct) =>
                {
                    var node = (MemberAccessExpressionSyntax)ctx.Node;
                    var semanticModel = ctx.SemanticModel;

                    var symbolInfo = semanticModel.GetSymbolInfo(node, ct).Symbol;

                    return symbolInfo is IPropertySymbol propertySymbol &&
                        propertySymbol.ContainingType.ToDisplayString() == "GitContext.Git"
                        ? propertySymbol.Name
                        : null;
                }
            )
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!)
            .Collect();

        var propertiesProvider = gitReaderProvider.Combine(propertyAccesses)
            .Select((input, ct) =>
            {
                var (gitReader, properties) = input;

                var defaults = _properties.Keys
                    .Except(properties)
                    .Select(propertyName =>
                    {
                        var value = propertyName switch
                        {
                            "Hash" => EmitValue((string?)null),
                            "Branch" => EmitValue((string?)null),
                            "IsDetached" => EmitValue(false),
                            "Author" => EmitValue((string?)null),
                            "Date" => EmitValue((DateTimeOffset?)null),
                            "Message" => EmitValue((string?)null),
                            "Parents" => EmitValue([]),
                            "Tags" => EmitValue([]),
                            _ => throw new InvalidOperationException("Invalid property name"),
                        };

                        return _properties[propertyName] with { Value = value };
                    });

                var propertiesInfo = properties
                    .Distinct()
                    .Intersect(_properties.Keys)
                    .Select(propertyName =>
                    {
                        var value = propertyName switch
                        {
                            "Hash" => EmitValue(gitReader.GetCommitHash().Result),
                            "Branch" => EmitValue(gitReader.GetBranch().Result),
                            "IsDetached" => EmitValue(gitReader.GetIsDetached().Result),
                            "Author" => EmitValue(gitReader.GetCommitAuthor().Result),
                            "Date" => EmitValue(gitReader.GetCommitDate().Result),
                            "Message" => EmitValue(gitReader.GetCommitMessage().Result),
                            "Parents" => EmitValue(gitReader.GetCommitParents().Result),
                            "Tags" => EmitValue(gitReader.GetTags().Result),
                            _ => default,
                        };

                        return _properties[propertyName] with { Value = value };
                    })
                .Where(static property => property != default)
                .Concat(defaults)
                .OrderBy(static property => property.Name)
                .ToImmutableArray();

                return propertiesInfo;
            });

        context.RegisterPostInitializationOutput(spc =>
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("""
            #nullable enable
            namespace GitContext;
            
            /// <summary>
            /// Git context
            /// </summary>
            public static partial class Git
            {
            """);

            foreach (var property in _properties.Values.OrderBy(property => property.Name))
                sourceBuilder.AppendLine($$"""
                    /// <summary>
                    /// {{property.Comment}}
                    /// </summary>
                    public static {{property.Type}} {{property.Name}} {get; set;}
                """);

            sourceBuilder.AppendLine("""
            }
            """);

            var sourceText = SourceText.From(sourceBuilder.ToString(), Encoding.UTF8);

            spc.AddSource("GitContext.Git.g.cs", sourceText);
        });

        context.RegisterSourceOutput(propertiesProvider, (spc, properties) =>
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("""
            #nullable enable
            namespace GitContext;
            
            /// <summary>
            /// Git context
            /// </summary>
            public static partial class Git
            {
                static Git()
                {
            """);

            foreach (var property in properties)
                sourceBuilder.AppendLine($$"""
                        {{property.Name}} = {{property.Value}};
                """);

            sourceBuilder.AppendLine("""
                }
            }
            """);

            var sourceText = SourceText.From(sourceBuilder.ToString(), Encoding.UTF8);

            spc.AddSource("GitContext.Git.Ctor.g.cs", sourceText);
        });
    }

    private static string EmitValue(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);
    private static string EmitValue(bool value) => value ? "true" : "false";
    private static string EmitValue(DateTimeOffset value) => $"new global::System.DateTimeOffset({value.Ticks}, new global::System.TimeSpan({value.Offset.Hours}, {value.Offset.Minutes}, 0))";
    private static string EmitValue(DateTimeOffset? value) => value is null ? "null" : EmitValue(value.Value);
    private static string EmitValue(string[] value) => $"new string[] {{ {string.Join(", ", value.Select(EmitValue))} }}";

    private record struct GitProperty(string Type, string Name, string Comment)
    {
        public string Type { get; } = Type;
        public string Name { get; } = Name;
        public string Comment { get; } = Comment;
        public string? Value { get; set; }
    }
}

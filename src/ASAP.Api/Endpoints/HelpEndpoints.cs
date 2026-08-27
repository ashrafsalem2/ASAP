using ASAP.Platform.Kernel.Security;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>
/// Serves the help topics the messages point at.
/// </summary>
/// <remarks>
/// <para>
/// Nearly every refusal in ASAP carries a help topic, and until this existed every one of them
/// led nowhere. A link somebody follows at the moment they are already stuck had better arrive
/// somewhere.
/// </para>
/// <para>
/// The topics are markdown files under <c>docs/help</c>, one per language, and a conformance test
/// refuses to let a message point at one that is missing. That is the same bargain as the message
/// catalogue: documentation that is optional decays, and documentation a build will not go
/// without does not.
/// </para>
/// </remarks>
public static class HelpEndpoints
{
    /// <summary>Maps the help endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <param name="environment">Supplies the content root the topics sit under.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHelpEndpoints(
        this IEndpointRouteBuilder app,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(environment);

        var group = app.MapGroup("/api/help").RequireAuthorization().WithTags("Help");

        group.MapGet("/", ListAsync)
             .WithName("HelpTopics")
             .WithSummary("Lists every help topic, grouped by the module it belongs to.");

        group.MapGet("/{**topic}", ReadAsync)
             .WithName("HelpTopic")
             .WithSummary("Reads one topic in the caller's language.");

        return app;
    }

    private static IResult ListAsync(IWebHostEnvironment environment, IUserContext user)
    {
        var root = RootOf(environment);

        if (!Directory.Exists(root))
        {
            return Results.Ok(Array.Empty<object>());
        }

        var language = LanguageOf(user);

        var topics = Directory
            .EnumerateFiles(root, $"*.{language}.md", SearchOption.AllDirectories)
            .Select(path => TopicOf(root, path, language))
            .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
            .Select(topic => new
            {
                topic,
                area = topic.Split('/')[0],

                // The first heading, which is the title the author wrote rather than one
                // reconstructed from the file name.
                title = TitleOf(Path.Combine(root, PathOf(topic, language))),
            })
            .ToList();

        return Results.Ok(topics);
    }

    private static IResult ReadAsync(
        string topic,
        IWebHostEnvironment environment,
        IUserContext user,
        [FromQuery] string? language)
    {
        // A topic comes off a URL, so it is somebody's typing. Anything that could climb out of
        // the help directory is refused rather than resolved.
        if (topic.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(topic)
            || topic.Contains(':', StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var root = RootOf(environment);
        var wanted = language is "ar" or "en" ? language : LanguageOf(user);

        var path = Path.Combine(root, PathOf(topic, wanted));

        // English is the fallback, and the response says which language it actually got. A page
        // silently in the wrong language reads as a translation nobody bothered with.
        var served = wanted;

        if (!File.Exists(path))
        {
            path = Path.Combine(root, PathOf(topic, "en"));
            served = "en";
        }

        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        var markdown = File.ReadAllText(path);

        return Results.Ok(new
        {
            topic,
            language = served,
            requestedLanguage = wanted,
            title = FirstHeading(markdown) ?? topic,
            markdown,
        });
    }

    private static string RootOf(IWebHostEnvironment environment)
    {
        // Copied beside the application when it is published, and found by walking up to the
        // repository when it is run from source.
        var beside = Path.Combine(environment.ContentRootPath, "help");

        if (Directory.Exists(beside))
        {
            return beside;
        }

        var directory = new DirectoryInfo(environment.ContentRootPath);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ASAP.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? environment.ContentRootPath, "docs", "help");
    }

    private static string LanguageOf(IUserContext user)
        => user.Culture is not null
           && user.Culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? "ar"
            : "en";

    private static string PathOf(string topic, string language)
        => $"{topic.Replace('/', Path.DirectorySeparatorChar)}.{language}.md";

    private static string TopicOf(string root, string path, string language)
        => Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace($".{language}.md", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string? TitleOf(string path)
        => File.Exists(path) ? FirstHeading(File.ReadAllText(path)) : null;

    private static string? FirstHeading(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return null;
    }
}

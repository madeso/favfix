// See https://aka.ms/new-console-template for more information

using Spectre.Console;
using Spectre.Console.Cli;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileSystemGlobbing;
using Tomlyn;
using Tomlyn.Serialization;

namespace favfix;

public class FrontMatter
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [TomlExtensionData]
    public IDictionary<string, object?>? Extra { get; set; }
}

internal static class Glob
{
    public static IEnumerable<string> Files(IEnumerable<string> args)
    {
        var m = new Matcher();

        // does this recursive add all files?

        m.AddIncludePatterns(args);
        var r = m.GetResultsInFullPath(Environment.CurrentDirectory);

        // get absolute paths
        return r.Select(f => (new FileInfo(f)).FullName);
    }
}


public static class SpectreExtensions
{
    public static async Task RunArrayAsync<T>(this Progress progress, ImmutableArray<T> arr, Func<T, string?> title, Func<T, Task<bool>> action)
    {
        if (arr.Length == 1)
        {
            await action(arr[0]);
        }
        else if (arr.Length > 1)
        {
            var cont = await action(arr[0]);
            if (cont == false) return;
            await progress.StartAsync(async ctx =>
            {
                var index = 1;
                var task = ctx.AddTask(ToTaskMessage(title(arr[index])), maxValue: arr.Length - 1);

                while (!ctx.IsFinished)
                {
                    task.Description = ToTaskMessage(title(arr[index]));
                    cont = await action(arr[index]);
                    if (cont == false) return;
                    task.Description = ToTaskMessage(title(arr[index]));

                    // Increment
                    task.Increment(1);
                    index += 1;
                }
            });
        }

        return;

        static string ToTaskMessage(string? message)
            => string.IsNullOrWhiteSpace(message) ? "Waiting..." : message;
    }

}



internal class MarkdownCommand : AsyncCommand<MarkdownCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The files to to review")]
        public string[] FileNames { get; init; } = [];

        [CommandOption("-f|--force")]
        [Description("Overwrite existing tags without aborting")]
        public bool Force { get; init; }

        [CommandOption("-c|--continue")]
        [Description("Continue on error")]
        public bool Continue { get; init; }
    }

    private const string TomlLine = "+++";

    private static (string?, string) ReadMarkdown(string file)
    {
        var content = File.ReadAllText(file);
        return SplitFrontMatter(content, TomlLine);
    }

    private static void WriteMarkdown(string file, string fm, string md)
    {
        var content = $"{TomlLine}\n{fm.Trim()}\n{TomlLine}\n{md.Trim()}\n";
        File.WriteAllText(file, content);
    }

    private static (string?, string) SplitFrontMatter(string content, string split)
    {
        var first = content.IndexOf(split, StringComparison.Ordinal);
        if (first == -1) return (null, content);

        var second = content.IndexOf(split, first + split.Length, StringComparison.Ordinal);
        if (second == -1) return (null, content);

        var header = content.Substring(first + split.Length, second - (first + split.Length));
        var body = content.Substring(second + split.Length);
        return (header, body);
    }

    internal static Progress Progress()
    {
        return AnsiConsole.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns(
                new SpinnerColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new TaskDescriptionColumn()
            );
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellation)
    {
        var llm = await Tags.ConnectToModel();

        var allFiles = Glob.Files(settings.FileNames).ToImmutableArray();

        if (allFiles.Length == 0)
        {
            AnsiConsole.WriteLine("No files detected");
            return -1;
        }

        bool ok = true;

        int tot = 0;
        int suc = 0;
        int err = 0;

        await Progress().RunArrayAsync(allFiles, file=>$"Updating {file}...", async file =>
        {
            var ret = await UpdateTagsForFile(file, settings, llm, allFiles.Length == 1);

            tot += 1;
            if (ret) suc += 1;
            else err += 1;

            if (ret == false)
            {
                ok = false;

                if (settings.Continue == false)
                {
                    AnsiConsole.WriteLine("Error detected, aborting...");
                    return false;
                }
            }

            return true;
        });

        AnsiConsole.WriteLine($"Updated {suc} of {tot} file(s), failed {err}");

        return ok ? 0 : -1;
    }

    private static async Task<bool> UpdateTagsForFile(string file, Settings settings,
        Func<string, Task<CompletionResponse>> llm, bool showProgress)
    {
        var (fmSource, md) = ReadMarkdown(file);
        if (fmSource == null)
        {
            AnsiConsole.WriteLine($"{file}: Failed to read markdown");
            return false;
        }

        var fm = TomlSerializer.Deserialize<FrontMatter>(fmSource);
        if (fm == null)
        {
            AnsiConsole.WriteLine($"{file}: Failed to read frontmatter");
            return false;
        }

        fm.Tags = fm.Tags.Where(x => string.IsNullOrWhiteSpace(x) == false).Select(x => x.Trim()).ToList();
        if(settings.Force == false)
        {
            if (fm.Tags.Count != 0)
            {
                var strTags = string.Join(", ", fm.Tags.Select(x => $"[{x}]"));
                AnsiConsole.WriteLine($"{file}: Post contains existing tags, ignoring: {strTags}");
                return true;
            }
        }

        var responses = await Tags.AiTags(showProgress, $"# {fm.Title}\n\n {md}", "markdown", llm,
            "Format tags with spaces, normal capitalization and diacritics as you would in a normal sentence. If it's a name, format it like wikipedia would, for example: 'New York (city)' and 'France (country)'. Don't use names inside parenthesis only nouns, for example not 'Stockholm (sweden)' but 'Stockholm (city)'.");

        var newTags = responses.SelectMany(x => x.Tags).Select(x => x.Trim()).ToImmutableHashSet();
        var oldTags = fm.Tags.Select(x => x.ToLowerInvariant()).ToImmutableHashSet();

        foreach (var r in newTags)
        {
            if (oldTags.Contains(r.ToLowerInvariant()))
            {
                continue;
            }
            fm.Tags.Add(r);
        }

        WriteMarkdown(file, TomlSerializer.Serialize(fm), md);

        return true;
    }
}

internal class FavoriteCommand : AsyncCommand<FavoriteCommand.Settings>
{
    public class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var llm = await Tags.ConnectToModel();

        string html = "<html><body>This page will discuss the importance of petting dogs in video games and how it might improve your life. <a href=\"\">Click here</a> to start your dog petting journey.</body></html>";
        var responses = await Tags.AiTags(true, html, "html", llm);
        foreach (var r in responses)
        {
            var tags = string.Join(",", r.Tags.Select(x => JsonSerializer.Serialize(x)));
            Console.WriteLine($"{r.Reasoning}");
            Console.WriteLine($"{tags}");
            Console.WriteLine();
        }
        return 0;
    }
}

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<MarkdownCommand>("md");
            config.AddCommand<FavoriteCommand>("fav");
        });
        return await app.RunAsync(args);
    }
}

internal static class Tags
{
    public static async Task<List<TagsResponse>> AiTags(bool showProgress, string input, string type, Func<string, Task<CompletionResponse>> llm, string extra = "")
    {
        var json = JsonSerializer.Serialize(new TagsResponse()
        {
            Tags = ["list", "of", "tags"],
            Reasoning = "the tags were selected as a example"
        }
        );
        var question = $"Given the following {type} code, extract the tags that would be entered on the social bookmarking site del.icio.us. " +
                       $"Feel free to improve the tags. Output a syntactically similar to the attached json. " +
                       $"Include the reasoning with at least 2 sentences for choosing the specific tags in the reason property. " +
                       $"Don't output example code and don't format it as markdown. {extra} " +
                       $"Only the actual json:\n\n{json}\n\n" +
                       $"{type} code starts now:\n\n{input}";

        var start = DateTime.Now;
        var res = showProgress ? await Cli.Status("Thinking...", async () => await llm(question)) : await llm(question);
        var end = DateTime.Now;
        {
            var diff = end.Subtract(start);
            AnsiConsole.MarkupLineInterpolated($"Took {Math.Floor(diff.TotalSeconds)}s {diff.Milliseconds}ms ({diff})");
        }
        var responses = new List<TagsResponse>();
        foreach (var m in res.Choices)
        {
            var r = JsonSerializer.Deserialize<TagsResponse>(m.Message.Content);
            try
            {
                if (r == null)
                {
                    throw new Exception("Parsed a null");
                }
                responses.Add(r);
            }
            catch (Exception x)
            {
                throw new Exception("Not a json...", x);
            }
        }

        return responses;
    }

    public static async Task<Func<string, Task<CompletionResponse>>> ConnectToModel()
    {
        var settingsFile = Path.Join(Environment.CurrentDirectory, "settings.json");

        var settings = LmSettings.LoadFile(settingsFile);

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            settings.Host = AnsiConsole.Prompt(new TextPrompt<string>("Host>").DefaultValue("localhost"));
            settings.Port = AnsiConsole.Prompt(new TextPrompt<int>("Port?").DefaultValue(1234));
            settings.Save(settingsFile);
        }

        var lm = new LmStudio(settings.Host, settings.Port);

        if (string.IsNullOrWhiteSpace(settings.SelectedModel))
        {
            var models = await Cli.Status("Fetching available models...", async () => await lm.GetModels());
            settings.SelectedModel = Cli.SelectString("Select model", "models", models);
            settings.Save(settingsFile);
        }

        Console.WriteLine($"Using: {settings.SelectedModel}");
        return async (question) => await lm.GetCompletions(settings.SelectedModel, question);
    }
}

internal class TagsResponse
{
    [JsonPropertyName("tags")] public ImmutableArray<string> Tags { get; set; } = [];
    [JsonPropertyName("reasoning")] public string Reasoning { get; set; } = "";
}
// See https://aka.ms/new-console-template for more information

using Spectre.Console;
using Spectre.Console.Cli;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
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

internal class MarkdownCommand : AsyncCommand<MarkdownCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The file to to review")]
        public string FileName { get; init; } = string.Empty;
    }

    private (string?, string) ReadMarkdown(string file)
    {
        var content = File.ReadAllText(file);
        return SplitFrontMatter(content, "+++");
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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var llm = await Tags.ConnectToModel();

        var (fm, md) = ReadMarkdown(settings.FileName);
        if (fm == null)
        {
            AnsiConsole.WriteLine("Failed to read markdown");
            return -1;
        }

        var parsed = TomlSerializer.Deserialize<FrontMatter>(fm);
        if (parsed == null)
        {
            AnsiConsole.WriteLine("Failed to read frontmatter");
            return -1;
        }

        var responses = await Tags.AiTags($"# {parsed.Title}\n\n {md}", "markdown", llm, "Format tags with spaces, normal capitalization and diacritics as you would in a normal sentence.");

        var tags = responses.SelectMany(x => x.Tags).ToImmutableHashSet();

        foreach (var r in tags)
        {
            Console.WriteLine(r);
        }

        var ret = TomlSerializer.Serialize(parsed);

        return 0;
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
        var responses = await Tags.AiTags(html, "html", llm);
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
    public static async Task<List<TagsResponse>> AiTags(string input, string type, Func<string, Task<CompletionResponse>> llm, string extra = "")
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
        var res = await Cli.Status("Thinking...", async () => await llm(question));
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
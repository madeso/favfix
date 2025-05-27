// See https://aka.ms/new-console-template for more information

using System.Collections.Immutable;
using favfix;
using Spectre.Console;
using System.Text.Json.Serialization;
using System.Text.Json;

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

string html = "<html><body>This page will discuss the importance of petting dogs in video games and how it might improve your life. <a href=\"\">Click here</a> to start your dog petting journey.</body></html>";
var json = JsonSerializer.Serialize(new TagsResponse()
    {
        Tags = ["list", "of", "tags"]
    }
);
var question = $"Given the following html code, extract the tags that would be entered on the social bookmarking site del.icio.us. " +
               $"Feel free to improve the tags. Output a syntactically similar to the following json. " +
               $"Include the reasoning for choosing he specific tags in the reason property. " +
               $"Don't output example code and don't format it as markdown. " +
               $"Only the actual json:\n\n{json}\n\n" +
               $"Html starts now:\n\n{html}";

var res = await Cli.Status("Thinking...", async () => await lm.GetCompletions(settings.SelectedModel, question));
foreach(var m in res.Choices)
{
    Console.WriteLine($"> {m.Message.Content}");
}


internal class TagsResponse
{
    [JsonPropertyName("tags")] public ImmutableArray<string> Tags { get; set; } = [];
    [JsonPropertyName("reasoning")] public string Reasoning { get; set; } = "";
}
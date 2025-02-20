// See https://aka.ms/new-console-template for more information

using favfix;
using Spectre.Console;

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

var res = await Cli.Status("Thinking...", async () => await lm.GetCompletions(settings.SelectedModel, "Say this is a test!"));
foreach(var m in res.Choices)
{
    Console.WriteLine($"> {m.Message.Content}");
}

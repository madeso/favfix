using Spectre.Console;
using System.Collections.Immutable;

namespace favfix;

internal static class Cli
{
    public static void Print(FormattableString s)
    {
        AnsiConsole.MarkupLineInterpolated(s);
    }

    public static async Task UpdateProgressAsync<T>(string title, ImmutableArray<T> array, Func<T, Task> action)
    {
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                // Define tasks
                var task = ctx.AddTask(title);
                task.MaxValue = array.Length;

                for (var i = 0; i < array.Length; i += 1)
                {
                    task.Value = i;
                    await action(array[i]);
                    task.Value = i + 1;
                }
            });
    }

    public static async Task UpdateProgress<T>(string title, ImmutableArray<T> array, Action<T> action)
    {
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                // Define tasks
                var task = ctx.AddTask(title);
                task.MaxValue = array.Length;

                for (var i = 0; i < array.Length; i += 1)
                {
                    task.Value = i;
                    await Task.Run(() =>
                    {
                        action(array[i]);
                    });
                    task.Value = i + 1;
                }
            });
    }

    public static async Task<List<Y>> GetProgressAsync<T, Y>(string title, ImmutableArray<T> array, Func<T, Task<Y>> action)
    {
        var r = new List<Y>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                // Define tasks
                var task = ctx.AddTask(title);
                task.MaxValue = array.Length;

                for (var i = 0; i < array.Length; i += 1)
                {
                    task.Value = i;
                    var y = await action(array[i]);
                    r.Add(y);
                    task.Value = i + 1;
                }


            });
        return r;
    }

    public static string SelectString(string title, string plural, IEnumerable<string> strings)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .PageSize(10)
                .MoreChoicesText($"[grey](Move up and down to reveal more {plural})[/]")
                .AddChoices(strings));
    }

    public static async Task<T> Status<T>(string label, Func<Task<T>> generator)
    {
        T? result = default(T);
        await AnsiConsole.Status()
            .StartAsync(label, async ctx =>
            {
                result = await generator();
            });
        if (result == null) throw new Exception("Internal async error");

        return result;
    }
}

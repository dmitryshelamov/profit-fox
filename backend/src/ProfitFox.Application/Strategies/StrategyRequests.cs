using System.Text.Json;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies;

/// <summary>Checks shared by the strategy and composite node commands.</summary>
public static class StrategyRequests
{
    public const string Changed = "The library has changed since it was read. Repeat the request.";

    /// <summary>Reads the definition and checks the name, reporting problems of both at once.</summary>
    /// <exception cref="ValidationFailedException">The name is empty or the definition is malformed.</exception>
    public static T Read<T>(string name, JsonElement definition, Func<JsonElement, T> parse)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = "Name must not be empty.";
        }

        T? value = default;

        try
        {
            value = parse(definition);
        }
        catch (ValidationFailedException exception)
        {
            foreach ((string key, string message) in exception.Errors)
            {
                errors[key] = message;
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return value!;
    }

    /// <summary>Runs a library operation and reports graph errors under keys such as <c>graph.ladder1/mul2.b</c>.</summary>
    /// <exception cref="ValidationFailedException">The graph is invalid.</exception>
    public static T Check<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (GraphValidationException exception)
        {
            throw new ValidationFailedException(Errors(exception.Errors));
        }
    }

    public static Dictionary<string, string> Errors(IReadOnlyList<GraphError> errors)
    {
        return errors
            .GroupBy(Key)
            .ToDictionary(group => group.Key, group => string.Join(" ", group.Select(error => error.Reason)));
    }

    /// <exception cref="StateConflictException">The save found a newer revision.</exception>
    public static void EnsureSaved(bool saved)
    {
        if (!saved)
        {
            throw new StateConflictException(Changed, []);
        }
    }

    private static string Key(GraphError error)
    {
        string path = string.Join(".", new[] { "graph", error.Path, error.Item }.Where(part => !string.IsNullOrEmpty(part)));

        return path;
    }
}
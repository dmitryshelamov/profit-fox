using System.Text.Json;

using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Converters;

/// <summary>
///     The JSON of a signal: the file of the repository that the import reads (<c>name</c>, <c>description</c>, <c>graph</c>
///     and <c>layout</c>) and the graph and layout the database keeps. The graph is written by the engine, so moving a node
///     never changes a byte of it.
/// </summary>
internal static class SignalJson
{
    private static readonly string[] Fields = ["name", "description", "graph", "layout"];

    /// <summary>
    ///     The graph of a signal as the engine writes it: this is what the version of a signal keeps.
    /// </summary>
    public static string WriteGraph(SignalDocument signal)
    {
        return StrategyGraph.Write(signal.Graph);
    }

    public static string WriteLayout(SignalDocument signal)
    {
        return LayoutJson.Write(signal.Layout);
    }

    /// <summary>
    ///     The signal the store kept, or the errors that stop it: its graph no longer reads as a signal, or its layout is not
    ///     one.
    /// </summary>
    public static SignalRead ReadStored(Guid id, string name, string? description, string graph, string layout, int version)
    {
        using var graphDocument = JsonDocument.Parse(graph);
        ReadResult<Signal> read = StrategyGraph.ReadSignal(graphDocument.RootElement);
        List<string> errors = [.. read.Errors.Select(error => $"graph: {error}")];
        Dictionary<string, NodePosition> positions = [];
        using var layoutDocument = JsonDocument.Parse(layout);
        errors.AddRange(LayoutJson.Read(layoutDocument.RootElement, positions));
        return errors.Count == 0 && read.Value is not null
            ? new SignalRead(SignalDocument.Restore(id, name, description, read.Value.Graph, positions, version), [])
            : new SignalRead(null, errors);
    }

    /// <summary>
    ///     The signal of a file, or the errors that stop it: of its form, of the graph as a signal, or a name that is not
    ///     the name of the file.
    /// </summary>
    public static (SignalDocument? Signal, IReadOnlyList<string> Errors) Read(string text, string fileName)
    {
        List<string> errors = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return (null, [$"the file is not JSON: {exception.Message}"]);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, ["the file must be a JSON object"]);
            }

            errors.AddRange(root.EnumerateObject().Where(field => !Fields.Contains(field.Name)).Select(field => $"unknown field {field.Name}"));

            string? name = root.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
            if (name is null)
            {
                errors.Add("name is required");
            }
            else if (name != fileName)
            {
                errors.Add($"name {name} is not the name of the file {fileName}");
            }

            string? description = null;
            if (root.TryGetProperty("description", out JsonElement descriptionElement))
            {
                if (descriptionElement.ValueKind == JsonValueKind.String)
                {
                    description = descriptionElement.GetString();
                }
                else
                {
                    errors.Add("description must be a string");
                }
            }

            GraphDefinition? graph = null;
            if (root.TryGetProperty("graph", out JsonElement graphElement))
            {
                ReadResult<Signal> read = StrategyGraph.ReadSignal(graphElement);
                graph = read.Value?.Graph;
                errors.AddRange(read.Errors.Select(error => $"graph: {error}"));
            }
            else
            {
                errors.Add("graph is required");
            }

            Dictionary<string, NodePosition> layout = [];
            if (root.TryGetProperty("layout", out JsonElement layoutElement))
            {
                errors.AddRange(LayoutJson.Read(layoutElement, layout));
            }
            else
            {
                errors.Add("layout is required");
            }

            return errors.Count == 0 && name is not null && graph is not null
                ? (SignalDocument.Create(name, description, graph, layout), [])
                : (null, errors);
        }
    }
}

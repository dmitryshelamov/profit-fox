using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.BuiltIns;

public sealed record BuiltInComposite(BuiltInNode Node, string Title, CompositeDefinition Definition);
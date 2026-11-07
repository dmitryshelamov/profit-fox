using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;

internal static class IdeaAutoDbConverter
{
    // The detail of a refusal is kept as it is in the model: the enums by name, the names of the properties as they are written.
    private static readonly JsonSerializerOptions DetailOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

    internal static IdeaAutoDb FromDomain(IdeaAuto auto)
    {
        IdeaAutoConditions conditions = auto.Conditions;

        return new IdeaAutoDb
        {
            Id = auto.Id,
            Name = auto.Name,
            AccountId = auto.AccountId,
            Amount = auto.Amount,
            Mode = auto.Mode.ToCode(),
            Setups = [.. conditions.Setups.Select(setup => setup.ToCode())],
            Types = [.. conditions.Types.Select(type => type.ToCode())],
            Intervals = [.. conditions.Intervals.Select(interval => interval.ToCode())],
            Side = conditions.Side?.ToCode(),
            Category = conditions.Category?.ToCode(),
            Badge = conditions.Badge?.ToCode(),
            MinRiskReward = conditions.MinRiskReward,
            MinCases = conditions.MinCases,
            DayDrop = conditions.DayDrop.ToCode(),
            BtcDrop = conditions.BtcDrop.ToCode(),
            HighFunding = conditions.HighFunding.ToCode(),
            Position = auto.Position,
            DeletedAt = auto.DeletedAt,
            Budget = auto.Budget
        };
    }

    internal static IdeaAuto ToDomain(IdeaAutoDb row)
    {
        var conditions = new IdeaAutoConditions(
            [.. row.Setups.Select(code => IdeaCodes.TryParseSetup(code, out IdeaSetup setup) ? setup : throw Unknown("setup", code))],
            [.. row.Types.Select(code => IdeaCodes.TryParseType(code, out IdeaType type) ? type : throw Unknown("type", code))],
            [.. row.Intervals.Select(code => MarketDataCodes.TryParseInterval(code, out Interval interval) ? interval : throw Unknown("interval", code))],
            row.Side is { } side ? (IdeaCodes.TryParseSide(side, out IdeaSide parsed) ? parsed : throw Unknown("side", side)) : null,
            row.Category is { } category ? (MarketDataCodes.TryParseCategory(category, out MarketCategory market) ? market : throw Unknown("category", category)) : null,
            row.Badge is { } badge ? IdeaCodes.ParseBadge(badge) : null,
            row.MinRiskReward,
            row.MinCases,
            ParseFeatureCondition(row.DayDrop),
            ParseFeatureCondition(row.BtcDrop),
            ParseFeatureCondition(row.HighFunding));

        return new IdeaAuto(
            row.Id,
            row.Name,
            row.AccountId,
            row.Amount,
            IdeaAutoCodes.TryParseMode(row.Mode, out IdeaAutoMode mode) ? mode : throw Unknown("mode", row.Mode),
            conditions,
            row.Position,
            row.DeletedAt,
            row.Budget);
    }

    internal static IdeaAutoEntryDb FromDomain(IdeaAutoEntry entry)
    {
        return new IdeaAutoEntryDb
        {
            Id = entry.Id,
            IdeaId = entry.IdeaId,
            AutoId = entry.AutoId,
            AccountId = entry.AccountId,
            Amount = entry.Amount,
            MinRiskReward = entry.MinRiskReward,
            Rank = entry.Rank,
            Fired = entry.Fired,
            Minute = entry.Minute,
            CreatedAt = entry.CreatedAt
        };
    }

    internal static IdeaAutoEntry ToDomain(IdeaAutoEntryDb row)
    {
        return new IdeaAutoEntry(row.Id, row.IdeaId, row.AutoId, row.AccountId, row.Amount, row.MinRiskReward, row.Rank, row.Fired, row.Minute, row.CreatedAt);
    }

    internal static IdeaAutoRefusalDb FromDomain(IdeaAutoRefusal refusal)
    {
        return new IdeaAutoRefusalDb
        {
            Id = refusal.Id,
            IdeaId = refusal.IdeaId,
            AutoId = refusal.AutoId,
            Mode = refusal.Mode.ToCode(),
            CreatedAt = refusal.At,
            Category = refusal.Instrument.Category.ToCode(),
            Symbol = refusal.Instrument.Symbol,
            Setup = refusal.Setup.ToCode(),
            Interval = refusal.Interval.ToCode(),
            Type = refusal.Type.ToCode(),
            Side = refusal.Side.ToCode(),
            Reasons = [.. refusal.Reasons.Select(reason => reason.ToCode())],
            Detail = refusal.Detail is null ? null : JsonSerializer.Serialize(refusal.Detail, DetailOptions)
        };
    }

    internal static IdeaAutoRefusal ToDomain(IdeaAutoRefusalDb row)
    {
        return new IdeaAutoRefusal(
            row.Id,
            row.IdeaId,
            row.AutoId,
            IdeaAutoCodes.TryParseMode(row.Mode, out IdeaAutoMode mode) ? mode : throw Unknown("mode", row.Mode),
            row.CreatedAt,
            new Instrument(MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category) ? category : throw Unknown("category", row.Category), row.Symbol),
            IdeaCodes.TryParseSetup(row.Setup, out IdeaSetup setup) ? setup : throw Unknown("setup", row.Setup),
            MarketDataCodes.TryParseInterval(row.Interval, out Interval interval) ? interval : throw Unknown("interval", row.Interval),
            IdeaCodes.TryParseType(row.Type, out IdeaType type) ? type : throw Unknown("type", row.Type),
            IdeaCodes.TryParseSide(row.Side, out IdeaSide side) ? side : throw Unknown("side", row.Side),
            [.. row.Reasons.Select(code => IdeaTradeCodes.TryParseRefusal(code, out IdeaTradeRefusal reason) ? reason : throw Unknown("reason", code))],
            row.Detail is null ? null : JsonSerializer.Deserialize<RefusalDetail>(row.Detail, DetailOptions));
    }

    private static FeatureCondition ParseFeatureCondition(string code)
    {
        return IdeaAutoCodes.TryParseFeatureCondition(code, out FeatureCondition condition) ? condition : throw Unknown("feature condition", code);
    }

    private static InvalidOperationException Unknown(string what, string code)
    {
        return new InvalidOperationException($"Unknown {what} code '{code}' in storage.");
    }
}
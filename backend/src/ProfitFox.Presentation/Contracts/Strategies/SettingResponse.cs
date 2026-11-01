using ProfitFox.Application.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Name">The property under <c>settings</c> of a node, such as <c>side</c>.</param>
/// <param name="Values">The codes a definition may give, in the order the editor offers them.</param>
/// <param name="Default">The code the editor gives a new node.</param>
/// <param name="Inputs">The inputs whose type the setting chooses.</param>
public sealed record SettingResponse(string Name, IReadOnlyList<string> Values, string Default, IReadOnlyList<string> Inputs)
{
    public static SettingResponse Of(NodeSettingSpec spec)
    {
        return new SettingResponse(spec.Name, [.. spec.Values.Select(StrategyCodes.ToCode)], StrategyCodes.ToCode(spec.Default), spec.TypedInputs);
    }
}
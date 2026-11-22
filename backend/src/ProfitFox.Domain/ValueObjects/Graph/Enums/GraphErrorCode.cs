namespace ProfitFox.Domain.ValueObjects.Graph.Enums;

/// <summary>
///     What is wrong with a graph. The form of the JSON comes first; the meaning of nodes and wires is checked only on a
///     graph of the right form.
/// </summary>
public enum GraphErrorCode
{
    NotJson,
    UnsupportedFormatVersion,
    UnknownField,
    DuplicateField,
    MissingField,
    InvalidField,

    DuplicateParameter,
    UnknownParameter,
    ParameterTypeMismatch,
    InvalidParameterBounds,
    ParameterOutOfBounds,
    DuplicateOutput,

    InvalidNodeId,
    DuplicateNodeId,
    UnknownNodeKind,
    UnknownSetting,
    MissingSetting,
    InvalidSetting,
    UnknownInput,
    ValueNotAllowed,
    ValueTypeMismatch,
    MissingInput,

    UnknownNode,
    UnknownOutputPin,
    WireTypeMismatch,
    SecondWire,
    WireAndValue,
    Cycle,
    OutputsNodeCount,

    SignalOutputCount,
    SignalOutputNotEvent,
    SignalPositionNode,

    StrategyHasOutputs,
    StrategyEntryMissing,
    StrategyExitMissing,
    StrategyExitRepeated,
    StrategyPositionMissing,
    StrategyPositionRepeated,
    StrategyOrderUnwired,

    StrategyKindNodeNotAllowed,
    StrategyKindNeedsTime,
    StrategyKindReadsPosition,

    UnknownSignal,
    UnreadableSignal,
    SignalInSignal,
    ExpandedIdTaken,

    /// <summary>
    ///     A node <c>pair-candle</c> names a pair that is not among the listed pairs (change add-ideas-and-market).
    /// </summary>
    UnknownPair
}
namespace ProfitFox.Engine.Strategy.Graph.Reading;

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
    StrategyCloseAfterMissing,
    StrategyExitRepeated
}
namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     Why a definition of a composite node was rejected.
/// </summary>
public enum GraphErrorCode
{
    /// <summary>
    ///     The text is not JSON.
    /// </summary>
    NotJson,

    /// <summary>
    ///     <c>formatVersion</c> is missing or is not 1.
    /// </summary>
    UnsupportedFormatVersion,

    /// <summary>
    ///     A field the format does not have.
    /// </summary>
    UnknownField,

    /// <summary>
    ///     A field that appears twice in one object.
    /// </summary>
    DuplicateField,

    /// <summary>
    ///     A required field is missing.
    /// </summary>
    MissingField,

    /// <summary>
    ///     A field whose value has the wrong JSON type or form.
    /// </summary>
    InvalidField,

    /// <summary>
    ///     A node id or a name of a declared output that does not start with a Latin letter or has characters other than Latin letters,
    ///     digits and <c>-</c>.
    /// </summary>
    InvalidName,

    /// <summary>
    ///     Two nodes with one id.
    /// </summary>
    DuplicateNodeId,

    /// <summary>
    ///     Two declared outputs with one name.
    /// </summary>
    DuplicateOutputName,

    /// <summary>
    ///     A node of a kind the library does not have.
    /// </summary>
    UnknownKind,

    /// <summary>
    ///     A setting the kind of the node does not declare.
    /// </summary>
    UnknownSetting,

    /// <summary>
    ///     A value of a setting outside the values the kind allows.
    /// </summary>
    InvalidSettingValue,

    /// <summary>
    ///     A required setting is missing.
    /// </summary>
    MissingSetting,

    /// <summary>
    ///     A wire to a node the graph does not have.
    /// </summary>
    UnknownNode,

    /// <summary>
    ///     A wire to a pin, or a value of an input, the node does not have.
    /// </summary>
    UnknownPin,

    /// <summary>
    ///     A wire between pins of different types, or a value of an input of another type.
    /// </summary>
    TypeMismatch,

    /// <summary>
    ///     A second wire to one input.
    /// </summary>
    SecondWire,

    /// <summary>
    ///     An input with neither a wire nor a value.
    /// </summary>
    MissingInputValue,

    /// <summary>
    ///     An input with both a wire and a value.
    /// </summary>
    ValueOnWiredInput,

    /// <summary>
    ///     Wires that make a node depend on itself.
    /// </summary>
    Cycle,

    /// <summary>
    ///     The graph has no node of kind <c>outputs</c>, or more than one.
    /// </summary>
    OutputsNodeCount,

    /// <summary>
    ///     A declared output with no wire into the node of outputs.
    /// </summary>
    UnwiredOutput,

    /// <summary>
    ///     A composite node read as a signal does not have exactly one logical output.
    /// </summary>
    NotASignal
}
namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>
///     The cases a report of calibration checks (requirement "Проверка после изменения правил"): all of them, or only those fired after the exit
///     rule of their type × interval was applied and after the version of the rules started.
/// </summary>
public enum CalibrationScope
{
    All,
    Fresh
}
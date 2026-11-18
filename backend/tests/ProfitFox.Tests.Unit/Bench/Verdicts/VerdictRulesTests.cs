using System.Globalization;
using System.Text.Json;
using ProfitFox.Bench.Verdicts;

namespace ProfitFox.Tests.Unit.Bench.Verdicts;

/// <summary>
///     Spec hypothesis-bench: the required t, blind power and the verdict, against <c>verdict.py</c> of the Python bench
///     (task 7.3). The fixture (<c>Fixtures/Verdict</c>) is synthetic, exported from the Python bench
///     <c>profit-fox-bench</c> at commit <c>13bd168</c>; the export script was removed by task 9.1 and is in commit
///     <c>a8bbfc1</c>.
/// </summary>
public sealed class VerdictRulesTests
{
    private const double Tolerance = 1e-9;
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Bench", "Fixtures", "Verdict");

    [Test]
    public void Ten_attempts_need_t_of_3_64()
    {
        Assert.That(VerdictRules.RequiredT(1), Is.EqualTo(3.0));
        Assert.That(Math.Round(VerdictRules.RequiredT(10), 2), Is.EqualTo(3.64));
    }

    [Test]
    public void The_required_t_equals_the_Python_one()
    {
        foreach (string[] row in Rows("required_t.csv"))
        {
            int attempts = int.Parse(row[0], CultureInfo.InvariantCulture);
            Assert.That(VerdictRules.RequiredT(attempts), Is.EqualTo(Number(row[1])).Within(Tolerance), $"{attempts} attempts");
        }
    }

    [Test]
    public void The_power_equals_the_Python_one()
    {
        foreach (string[] row in Rows("power.csv"))
        {
            double power = VerdictRules.Power(Number(row[0]), Number(row[1]), int.Parse(row[2], CultureInfo.InvariantCulture), Number(row[3]));
            Assert.That(power, Is.EqualTo(Number(row[4])).Within(Tolerance), string.Join(",", row));
        }
    }

    [Test]
    public void Twelve_blocks_are_too_few_and_the_outcomes_are_not_read()
    {
        double[] pseudo = [.. Enumerable.Range(0, 12).Select(index => index % 2 == 0 ? 0.001 : -0.001)];

        Verdict verdict = VerdictRules.Decide(blockMeans: null, pseudo, minEffect: 0.003, attempts: 1, events: 40);

        Assert.That(verdict.Kind, Is.EqualTo(VerdictKind.InsufficientData));
        Assert.That(verdict.T, Is.Null);
    }

    [Test]
    public void A_sign_that_differs_between_the_halves_rejects()
    {
        double[] pseudo = [.. Enumerable.Range(0, 40).Select(index => index % 2 == 0 ? 0.0005 : -0.0005)];
        double[] means = [.. Enumerable.Range(0, 40).Select(index => index < 20 ? 0.02 + index * 0.0001 : -0.001 + index * 0.00001)];

        Verdict verdict = VerdictRules.Decide(means, pseudo, minEffect: 0.003, attempts: 1, events: 120);

        Assert.That(verdict.T, Is.GreaterThan(3.0));
        Assert.That(verdict.Kind, Is.EqualTo(VerdictKind.Rejected));
        Assert.That(verdict.Reason, Does.StartWith("sign differs between halves"));
    }

    [Test]
    public void Every_decision_equals_the_Python_one()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "decisions.json")));
        int count = 0;
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            double[] pseudo = [.. item.GetProperty("pseudo").EnumerateArray().Select(value => value.GetDouble())];
            double[] means = [.. item.GetProperty("means").EnumerateArray().Select(value => value.GetDouble())];
            int attempts = item.GetProperty("attempts").GetInt32();
            double minEffect = item.GetProperty("min_effect").GetDouble();
            JsonElement expected = item.GetProperty("verdict");
            bool enough = VerdictRules.EnoughPower(pseudo, minEffect, attempts);

            Verdict verdict = VerdictRules.Decide(enough ? means : null, pseudo, minEffect, attempts, events: pseudo.Length * 3);

            string where = $"case {count}";
            Assert.That(Name(verdict.Kind), Is.EqualTo(expected.GetProperty("verdict").GetString()), where);
            Assert.That(verdict.Reason, Is.EqualTo(expected.GetProperty("reason").GetString()), where);
            Assert.That(verdict.Blocks, Is.EqualTo(expected.GetProperty("blocks").GetInt32()), where);
            Assert.That(verdict.Power, Is.EqualTo(expected.GetProperty("power").GetDouble()).Within(Tolerance), where);
            Assert.That(verdict.RequiredT, Is.EqualTo(expected.GetProperty("required_t").GetDouble()).Within(Tolerance), where);
            Assert.That(verdict.PseudoSd, Is.EqualTo(expected.GetProperty("pseudo_sd").GetDouble()).Within(Tolerance), where);
            Optional(where, "t", verdict.T, expected);
            Optional(where, "mean", verdict.Mean, expected);
            Optional(where, "first_half_mean", verdict.FirstHalfMean, expected);
            Optional(where, "second_half_mean", verdict.SecondHalfMean, expected);
            count++;
        }

        Assert.That(count, Is.EqualTo(40));
    }

    private static void Optional(string where, string name, double? actual, JsonElement expected)
    {
        JsonElement value = expected.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            Assert.That(actual, Is.Null, $"{where} {name}");
        }
        else
        {
            Assert.That(actual, Is.EqualTo(value.GetDouble()).Within(Tolerance), $"{where} {name}");
        }
    }

    private static string Name(VerdictKind kind)
    {
        return kind switch
        {
            VerdictKind.Passed => "passed",
            VerdictKind.Rejected => "rejected",
            _ => "insufficient_data"
        };
    }

    private static IEnumerable<string[]> Rows(string file)
    {
        return File.ReadLines(Path.Combine(Folder, file)).Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));
    }

    private static double Number(string text)
    {
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

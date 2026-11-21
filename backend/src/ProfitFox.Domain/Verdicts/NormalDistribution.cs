namespace ProfitFox.Domain.Verdicts;

/// <summary>
///     The standard normal distribution, as precise as Python's <c>statistics.NormalDist</c> the Python bench used.
/// </summary>
public static class NormalDistribution
{
    private const double LogSqrtTwoPi = 0.91893853320467274178;

    /// <summary>
    ///     P(Z ≤ x), by Marsaglia's series (Journal of Statistical Software 11, 2004): absolute error about 1e-15.
    /// </summary>
    public static double Cdf(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x < -38)
        {
            return 0;
        }

        if (x > 38)
        {
            return 1;
        }

        double sum = x;
        double term = x;
        double previous = 0;
        double square = x * x;
        for (double i = 1; sum != previous; )
        {
            previous = sum;
            i += 2;
            term *= square / i;
            sum = previous + term;
        }

        return Math.Clamp(0.5 + sum * Math.Exp(-0.5 * square - LogSqrtTwoPi), 0, 1);
    }

    /// <summary>
    ///     The x with P(Z ≤ x) = p, by Wichura's AS241, the algorithm of Python's <c>NormalDist.inv_cdf</c>.
    /// </summary>
    public static double InverseCdf(double p)
    {
        if (!(p > 0 && p < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(p), p, "p must be strictly between 0 and 1.");
        }

        double q = p - 0.5;
        double r;
        double numerator;
        double denominator;
        if (Math.Abs(q) <= 0.425)
        {
            r = 0.180625 - q * q;
            numerator = (((((((2.5090809287301226727e+3 * r + 3.3430575583588128105e+4) * r + 6.7265770927008700853e+4) * r
                              + 4.5921953931549871457e+4) * r + 1.3731693765509461125e+4) * r + 1.9715909503065514427e+3) * r
                          + 1.3314166789178437745e+2) * r + 3.3871328727963666080e+0) * q;
            denominator = ((((((5.2264952788528545610e+3 * r + 2.8729085735721942674e+4) * r + 3.9307895800092710610e+4) * r
                              + 2.1213794301586595867e+4) * r + 5.3941960214247511077e+3) * r + 6.8718700749205790830e+2) * r
                          + 4.2313330701600911252e+1) * r + 1.0;
            return numerator / denominator;
        }

        r = q <= 0 ? p : 1 - p;
        r = Math.Sqrt(-Math.Log(r));
        if (r <= 5)
        {
            r -= 1.6;
            numerator = ((((((7.7454501427834140764e-4 * r + 2.2723844989269184583e-2) * r + 2.4178072517745061177e-1) * r
                             + 1.2704582524523683826e+0) * r + 3.6478483247632045960e+0) * r + 5.7694972214606914055e+0) * r
                         + 4.6303378461565452959e+0) * r + 1.4234371107496835773e+0;
            denominator = ((((((1.0507500716444168432e-9 * r + 5.4759380849953449460e-4) * r + 1.5198666563616457197e-2) * r
                               + 1.4810397642748007459e-1) * r + 6.8976733498510000455e-1) * r + 1.6763848301838038494e+0) * r
                           + 2.0531916266377588219e+0) * r + 1.0;
        }
        else
        {
            r -= 5;
            numerator = ((((((2.0103343992922881327e-7 * r + 2.7115555687434875782e-5) * r + 1.2426609473880784386e-3) * r
                             + 2.6532189526576123093e-2) * r + 2.9656057182850489123e-1) * r + 1.7848265399172913358e+0) * r
                         + 5.4637849111641143699e+0) * r + 6.6579046435011037772e+0;
            denominator = ((((((2.0442631033899397856e-15 * r + 1.4215117583164458887e-7) * r + 1.8463183175100546818e-5) * r
                               + 7.8686913114561325910e-4) * r + 1.4875361290850614853e-2) * r + 1.3692988092273580531e-1) * r
                           + 5.9983220655588793769e-1) * r + 1.0;
        }

        double x = numerator / denominator;
        return q < 0 ? -x : x;
    }
}

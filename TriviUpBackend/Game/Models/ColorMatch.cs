namespace TriviUpBackend.Game.Models;

/// <summary>Color en HSB (tono 0-359, saturación y brillo 0-100), tal como lo eligen los sliders.</summary>
public sealed record ColorHsb(int Hue, int Saturation, int Brightness)
{
    public bool IsValid() =>
        Hue is >= 0 and <= 359 && Saturation is >= 0 and <= 100 && Brightness is >= 0 and <= 100;
}

/// <summary>
/// Reglas de la pregunta especial de Colores: los jugadores imitan un color objetivo y el que más se acerca
/// se lleva la pregunta. El parecido se mide en CIELAB (distancia perceptual), no en HSB.
/// </summary>
public static class ColorMatch
{
    /// <summary>Tiempo para imitar el color, en cualquier modo (también en presencial).</summary>
    public const int ChallengeSeconds = 60;

    /// <summary>
    /// Color objetivo al azar. Se evitan los colores casi grises o casi negros, que son poco reconocibles
    /// y hacen que el tono apenas importe.
    /// </summary>
    public static ColorHsb RandomTarget(Random random) =>
        new(random.Next(0, 360), random.Next(35, 101), random.Next(35, 101));

    /// <summary>
    /// Parecido entre dos colores en porcentaje (100 = idénticos), con un decimal:
    /// 100 menos la distancia ΔE (CIE76) en CIELAB, sin bajar de 0.
    /// </summary>
    public static double Similarity(ColorHsb target, ColorHsb guess)
    {
        var (l1, a1, b1) = ToLab(target);
        var (l2, a2, b2) = ToLab(guess);
        var deltaE = Math.Sqrt(Math.Pow(l1 - l2, 2) + Math.Pow(a1 - a2, 2) + Math.Pow(b1 - b2, 2));
        return Math.Round(Math.Clamp(100 - deltaE, 0, 100), 1);
    }

    public static (double R, double G, double B) ToRgb(ColorHsb color)
    {
        var s = color.Saturation / 100.0;
        var v = color.Brightness / 100.0;
        var c = v * s;
        var hPrime = color.Hue / 60.0;
        var x = c * (1 - Math.Abs(hPrime % 2 - 1));
        var (r, g, b) = hPrime switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        var m = v - c;
        return (r + m, g + m, b + m);
    }

    private static (double L, double A, double B) ToLab(ColorHsb color)
    {
        var (r, g, b) = ToRgb(color);
        r = Linearize(r);
        g = Linearize(g);
        b = Linearize(b);

        // sRGB → XYZ (D65), normalizado por el blanco de referencia
        var x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        var y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        var z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;

        var fx = LabF(x);
        var fy = LabF(y);
        var fz = LabF(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double LabF(double t) =>
        t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
}

namespace ScreenStudio.Services;

// Chroma key por CPU sobre BGRA recto (tal como lo entrega ffmpeg con -pix_fmt bgra).
// Se usa en la rama de efectos preparados, antes de pintar el fotograma.
public static class ChromaKey
{
    public static void Apply(byte[] bgra, int width, int height, uint keyRgb, double similarity, double blend)
    {
        if (similarity <= 0 || bgra.Length < width * height * 4) return;
        var kr = (keyRgb >> 16 & 255) / 255d; var kg = (keyRgb >> 8 & 255) / 255d; var kb = (keyRgb & 255) / 255d;
        var inner = similarity * (1 - blend); var outer = similarity;
        var span = outer - inner;
        for (var i = 0; i + 4 <= bgra.Length; i += 4)
        {
            var dr = bgra[i+2] / 255d - kr; var dg = bgra[i+1] / 255d - kg; var db = bgra[i] / 255d - kb;
            var dist = Math.Sqrt((dr*dr + dg*dg + db*db) / 3);
            double alpha = dist <= inner ? 0 : span <= 0 ? 255 : Math.Min(1,(dist - inner) / span) * 255;
            if (alpha < bgra[i+3]) bgra[i+3] = (byte)alpha;
        }
    }
    public static uint Parse(string color)
    {
        if (color is { Length: 7 } && color[0] == '#' && uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)) return value;
        throw new InvalidOperationException("Color de croma inválido.");
    }
}

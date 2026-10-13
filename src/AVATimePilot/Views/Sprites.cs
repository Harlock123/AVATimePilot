namespace AVATimePilot.Views;

/// <summary>
/// The planes, seen from above and pointing up the screen (the drawing code turns them): 16×16, one
/// letter per pixel. 'a' is the craft's colour, 'b' a darker and 'c' a lighter shade of it; the other
/// letters are fixed colours; '.' is clear. All original drawings.
/// </summary>
public static class Sprites
{
    public static readonly Dictionary<char, uint> Fixed = new()
    {
        ['k'] = 0xFF141218, ['w'] = 0xFFF0EEE6, ['r'] = 0xFFD83030, ['y'] = 0xFFF0D040, ['g'] = 0xFF8A8A90,
        ['n'] = 0xFF6A4828, ['u'] = 0xFF60C0F0, ['o'] = 0xFFFF9020,
    };

    public static readonly Dictionary<string, string[]> All = new()
    {
        ["player"] =
        [
            ".......aa.......",
            ".......aa.......",
            "......aaaa......",
            "......auua......",
            "......auua......",
            ".....aaaaaa.....",
            "....aaaaaaaa....",
            "..aaaaaaaaaaaa..",
            "aaaaaaaaaaaaaaaa",
            "raa...aaaa...aar",
            "......aaaa......",
            "......aaaa......",
            ".....aaaaaa.....",
            "....aab..baa....",
            "......o..o......",
            "................",
        ],
        ["biplane"] =
        [
            ".......gg.......",
            "......g..g......",
            ".......nn.......",
            ".......aa.......",
            "aaaaaaaaaaaaaaaa",
            "abbbbbbbbbbbbbba",
            "aaaaaaaaaaaaaaaa",
            "...k...kk...k...",
            ".......aa.......",
            ".......aa.......",
            ".......aa.......",
            ".......aa.......",
            ".....aaaaaa.....",
            ".....bbbbbb.....",
            ".......aa.......",
            "................",
        ],
        ["monoplane"] =
        [
            ".......gg.......",
            "......g..g......",
            ".......aa.......",
            "......aaaa......",
            "......akka......",
            "...aaaaaaaaaa...",
            ".aaaaaaaaaaaaaa.",
            "bbbbbbaaaabbbbbb",
            "......aaaa......",
            ".......aa.......",
            ".......aa.......",
            ".......aa.......",
            ".....aaaaaa.....",
            ".....bbbbbb.....",
            "................",
            "................",
        ],
        ["helicopter"] =
        [
            "................",
            "......aaaa......",
            ".....auuuua.....",
            ".....aaaaaa.....",
            ".....aaaaaa.....",
            "....aaaaaaaa....",
            "....aaaaaaaa....",
            ".....aaaaaa.....",
            "......aaaa......",
            ".......aa.......",
            ".......aa.......",
            ".......aa.......",
            ".......aa.......",
            ".....bbaabb.....",
            ".......aa.......",
            "................",
        ],
        ["jet"] =
        [
            ".......aa.......",
            ".......aa.......",
            "......aaaa......",
            "......auua......",
            ".....aaaaaa.....",
            ".....aaaaaa.....",
            "....aaaaaaaa....",
            "...aaaaaaaaaa...",
            "..aaaaaaaaaaaa..",
            ".aaaaaaaaaaaaaa.",
            "aaaaabaaaabaaaaa",
            "......aaaa......",
            ".....aaaaaa.....",
            "....aa.oo.aa....",
            ".......oo.......",
            "................",
        ],
        ["saucer"] =
        [
            "................",
            ".....aaaaaa.....",
            "...aaaaaaaaaa...",
            "..aaaaccccaaaa..",
            ".aaaacuuuucaaaa.",
            ".aaacuuuuuucaaa.",
            "aaaacuuuuuucaaaa",
            "aaaacuuuuuucaaaa",
            "aaaacuuuuuucaaaa",
            "aaaacuuuuuucaaaa",
            ".aaacuuuuuucaaa.",
            ".aaaacuuuucaaaa.",
            "..aaaaccccaaaa..",
            "...aaaaaaaaaa...",
            ".....aaaaaa.....",
            "................",
        ],
    };

    public static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    public static uint Colour(char ch, uint tint) => ch switch
    {
        '.' => 0,
        'a' => tint,
        'b' => Shade(tint, 0.62),
        'c' => Shade(tint, 1.45),
        _ => Fixed.GetValueOrDefault(ch, 0xFFFF00FF),
    };
}

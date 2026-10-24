using System.Buffers.Binary;
using System.Text;

namespace Basin.SoftKeyboard;

/// <summary>
/// An immutable list of words with a prior for each, for the trace decoder.
/// </summary>
public sealed class WordList
{
    /// <summary>
    /// The path of fcitx5's English word list, which has frequencies.
    /// </summary>
    public const string FcitxEnglish = "/usr/share/fcitx5/spell/en_dict.fscd";
    /// <summary>
    /// The directory of the hunspell dictionaries.
    /// </summary>
    public const string HunspellDirectory = "/usr/share/hunspell";

    private static readonly byte[] FscdMagic = "FSCD0000"u8.ToArray();

    private readonly string[] _words;
    private readonly float[] _priors;

    private WordList(string[] words, float[] priors, bool weighted, string source)
    {
        _words = words;
        _priors = priors;
        Weighted = weighted;
        Source = source;
    }

    /// <summary>
    /// Gets the number of words.
    /// </summary>
    public int Count => _words.Length;

    /// <summary>
    /// Gets a value indicating whether the priors come from frequencies. A hunspell list has a flat prior of 0.5.
    /// </summary>
    public bool Weighted { get; }

    /// <summary>
    /// Gets the path or the name of the source that the list came from.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the words, without duplicates from a hunspell expansion.
    /// </summary>
    public IReadOnlyList<string> Words => _words;

    /// <summary>
    /// Gets one word.
    /// </summary>
    /// <param name="index">The word index.</param>
    /// <returns>The word.</returns>
    public string this[int index] => _words[index];

    /// <summary>
    /// Gives the prior of one word, from 0 to 1.
    /// </summary>
    /// <param name="index">The word index.</param>
    /// <returns>The prior. For a frequency list it is the weight over 65535.</returns>
    public float Prior(int index) => _priors[index];

    /// <summary>
    /// Loads the word list for a language: fcitx5's English list when it exists, otherwise the hunspell dictionary for the language.
    /// </summary>
    /// <param name="language">The language code, for example <c>en</c> or <c>fr</c>.</param>
    /// <param name="reason">The reason that no list loaded, or <see langword="null"/> when a list loaded.</param>
    /// <returns>The list, or <see langword="null"/> when no list is known, none is installed, or a file cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="language"/> is <see langword="null"/>.</exception>
    public static WordList? ForLanguage(string language, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(language);
        try
        {
            if (language == "en" && File.Exists(FcitxEnglish))
            {
                reason = null;
                return FromFscd(File.ReadAllBytes(FcitxEnglish), FcitxEnglish);
            }

            var dictionary = language switch
            {
                "en" => "en_US",
                "fr" => "fr_FR",
                "de" => "de_DE",
                "sv" => "sv_SE",
                "es" => "es_ES",
                "it" => "it_IT",
                _ => null,
            };
            if (dictionary is null)
            {
                reason = $"no word list is known for the language '{language}'";
                return null;
            }

            var aff = Path.Combine(HunspellDirectory, dictionary + ".aff");
            var dic = Path.Combine(HunspellDirectory, dictionary + ".dic");
            if (!File.Exists(aff) || !File.Exists(dic))
            {
                reason = $"no word list for '{language}': neither {FcitxEnglish} nor {dic} exists";
                return null;
            }

            reason = null;
            return FromHunspell(File.ReadAllText(aff), File.ReadAllText(dic), dic);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            reason = $"the word list for '{language}' cannot be read: {error.Message}";
            return null;
        }
    }

    /// <summary>
    /// Reads an fcitx5 FSCD word list: the magic, a little-endian entry count, and per entry a little-endian u16 weight and a NUL-terminated UTF-8 word.
    /// </summary>
    /// <param name="data">The file contents.</param>
    /// <param name="source">The name for <see cref="Source"/> and for messages.</param>
    /// <returns>A weighted list.</returns>
    /// <exception cref="FormatException">The data has no FSCD magic, or it ends inside a word.</exception>
    public static WordList FromFscd(ReadOnlySpan<byte> data, string source = "fscd")
    {
        if (data.Length < 12 || !data[..8].SequenceEqual(FscdMagic))
        {
            throw new FormatException($"{source} is not an FSCD word list.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        var words = new List<string>((int)Math.Min(count, 1_000_000));
        var priors = new List<float>(words.Capacity);
        var position = 12;
        for (var i = 0u; i < count && position + 2 < data.Length; i++)
        {
            var weight = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(position, 2));
            position += 2;
            var end = data[position..].IndexOf((byte)0);
            if (end < 0)
            {
                throw new FormatException($"{source} ends inside a word. The file is truncated.");
            }

            words.Add(Encoding.UTF8.GetString(data.Slice(position, end)));
            priors.Add(weight / 65535f);
            position += end + 1;
        }

        return new WordList([.. words], [.. priors], weighted: true, source);
    }

    /// <summary>
    /// Expands a hunspell dictionary into a word list through one level of the prefix and suffix rules, with no compounding.
    /// </summary>
    /// <param name="aff">The text of the <c>.aff</c> file.</param>
    /// <param name="dic">The text of the <c>.dic</c> file.</param>
    /// <param name="source">The name for <see cref="Source"/>.</param>
    /// <returns>A list with a flat prior.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aff"/> or <paramref name="dic"/> is <see langword="null"/>.</exception>
    public static WordList FromHunspell(string aff, string dic, string source = "hunspell")
    {
        ArgumentNullException.ThrowIfNull(aff);
        ArgumentNullException.ThrowIfNull(dic);
        var affix = HunspellAffixes.Parse(aff);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var words = new List<string>();
        using var reader = new StringReader(dic);
        var first = true;
        while (reader.ReadLine() is { } line)
        {
            if (first)
            {
                first = false;
                if (int.TryParse(line.Trim(), out _))
                {
                    continue;
                }
            }

            affix.Expand(line, word =>
            {
                if (seen.Add(word))
                {
                    words.Add(word);
                }
            });
        }

        var priors = new float[words.Count];
        Array.Fill(priors, 0.5f);
        return new WordList([.. words], priors, weighted: false, source);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Basin.Desktop;

/// <summary>
/// Derives a stable identifier for an output from its make, model, serial and name.
/// </summary>
public static class OutputUuid
{
    /// <summary>
    /// Hashes the output identity with SHA-256. The same monitor on the same connector gets the same identifier on each run.
    /// </summary>
    /// <param name="output">The output to identify.</param>
    /// <returns>32 lowercase hex digits, or <c>basin-</c> followed by the name when the output has no identity at all.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static string For(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Make.Length > 0 || output.Model.Length > 0 || output.Serial.Length > 0)
        {
            return Hash($"{output.Make}\n{output.Model}\n{output.Serial}\n{output.Name}");
        }

        if (output.Name.Length > 0)
        {
            return Hash(output.Name);
        }

        return $"basin-{output.Name}";
    }

    private static string Hash(string identity) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
}

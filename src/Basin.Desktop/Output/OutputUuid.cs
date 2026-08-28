using System.Security.Cryptography;
using System.Text;

namespace Basin.Desktop;

public static class OutputUuid
{
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

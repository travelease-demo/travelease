using System.Security.Cryptography;

namespace TravelEase.Services;

/// <summary>Generates human-friendly booking references such as TE-241118-K7Q2M.</summary>
public static class BookingReferenceGenerator
{
    // No 0/O or 1/I to avoid confusion when references are read over the phone.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int SuffixLength = 5;

    public static string Create(DateTime date)
    {
        Span<char> suffix = stackalloc char[SuffixLength];
        for (var i = 0; i < SuffixLength; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return $"TE-{date:yyMMdd}-{new string(suffix)}";
    }

    /// <summary>Deterministic variant used by the sample data seeder.</summary>
    public static string Create(DateTime date, Random random)
    {
        var suffix = new char[SuffixLength];
        for (var i = 0; i < SuffixLength; i++)
        {
            suffix[i] = Alphabet[random.Next(Alphabet.Length)];
        }
        return $"TE-{date:yyMMdd}-{new string(suffix)}";
    }
}

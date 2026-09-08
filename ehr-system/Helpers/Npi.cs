using System.Linq;

namespace EHR.Helpers;

/// <summary>
/// The National Provider Identifier, and whether one can be real.
///
/// WHY THIS IS ITS OWN THING
/// Two places take an NPI and both must judge it the same way: the supplier's
/// own, on the Settings screen, which appears in CMS-1500 box 33a on every
/// claim they ever file; and the ordering physician's, in box 17b, which is
/// mandatory on DMEPOS. A rule written twice is a rule that disagrees with
/// itself eventually, and the disagreement would show up as some claims
/// rejecting and others not.
/// </summary>
public static class Npi
{
    /// <summary>
    /// Digits only. Punctuation and spaces are how people write an NPI down;
    /// ten digits is how it is stored. Returns null for nothing at all, so a
    /// blank field stays blank rather than becoming an empty string.
    /// </summary>
    public static string? Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>
    /// Is this a well formed NPI?
    ///
    /// An NPI carries a check digit, so most typos are detectable at the moment
    /// somebody types them rather than weeks later when the payer rejects the
    /// claim. The rule is Luhn over the constant prefix 80840 followed by the
    /// first nine digits: 80840 is the health industry's ISO issuer identifier,
    /// and CMS chose it precisely so an NPI validates under an algorithm every
    /// card system already implements.
    ///
    /// This proves the number is WELL FORMED. It does not prove the doctor
    /// exists, and nothing in this product can: that needs the NPPES registry,
    /// which is a call to an external service. So a caller's message must say
    /// "cannot be right" rather than "does not exist". The software must not
    /// claim to know more than it does, which is the same rule that took the
    /// eligibility chip out.
    /// </summary>
    public static bool IsPossible(string? npi)
    {
        if (npi is not { Length: 10 } || !npi.All(char.IsDigit)) return false;

        var payload = "80840" + npi[..9];
        var sum = 0;

        // Walk from the right, doubling every second digit; a doubled value over
        // nine has its digits added, which is the same as subtracting nine.
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var d = payload[i] - '0';
            if ((payload.Length - 1 - i) % 2 == 0)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }

        return (10 - (sum % 10)) % 10 == npi[9] - '0';
    }
}

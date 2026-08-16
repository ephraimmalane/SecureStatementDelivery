using System.Globalization;

namespace Domain.Users;

// Validation for a South African ID number (format: YYMMDD SSSS C A Z).
// Beyond "13 digits", a real ID number must carry a valid date of birth in the first six digits
// and a correct Luhn check digit in the last position. These two checks are what actually reject
// typos and fabricated numbers. (The citizenship/gender digits are not constrained here to avoid
// rejecting otherwise-legitimate numbers.)
public static class SouthAfricanIdValidator
{
    public static bool IsValid(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            return false;
        }

        string id = idNumber.Trim();

        if (id.Length != 13 || !id.All(char.IsAsciiDigit))
        {
            return false;
        }

        return HasValidDateOfBirth(id) && PassesLuhnCheck(id);
    }

    private static bool HasValidDateOfBirth(string id)
    {
        int year = int.Parse(id.AsSpan(0, 2), CultureInfo.InvariantCulture);
        int month = int.Parse(id.AsSpan(2, 2), CultureInfo.InvariantCulture);
        int day = int.Parse(id.AsSpan(4, 2), CultureInfo.InvariantCulture);

        if (month is < 1 or > 12)
        {
            return false;
        }

        // The two-digit year is century-ambiguous, so accept the day if it is valid in either the
        // 1900s or the 2000s (covers leap-year 29 Feb correctly via DaysInMonth).
        return IsValidDay(1900 + year, month, day) || IsValidDay(2000 + year, month, day);
    }

    private static bool IsValidDay(int year, int month, int day) =>
        day >= 1 && day <= DateTime.DaysInMonth(year, month);

    // Standard Luhn checksum over all 13 digits (the rightmost is the check digit).
    private static bool PassesLuhnCheck(string id)
    {
        int sum = 0;
        bool doubleDigit = false;

        for (int i = id.Length - 1; i >= 0; i--)
        {
            int digit = id[i] - '0';

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}

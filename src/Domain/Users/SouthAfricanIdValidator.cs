using System.Globalization;

namespace Domain.Users;

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

        return HasValidDateOfBirth(id)
            && HasValidCitizenshipClassification(id)
            && PassesLuhnCheck(id);
    }

    // Position 11 (index 10) in YYMMDDSSSSCAZ is the citizenship classification:
    // 0 = SA citizen, 1 = permanent resident, 2 = refugee. Any other value is a data-entry error.
    // The gender digits (SSSS) and the obsolete race digit (position 12) are informational only
    // and are intentionally not validated, matching SARS eFiling behaviour.
    private static bool HasValidCitizenshipClassification(string id) =>
        id[10] is '0' or '1' or '2';

    private static bool HasValidDateOfBirth(string id)
    {
        int year = int.Parse(id.AsSpan(0, 2), CultureInfo.InvariantCulture);
        int month = int.Parse(id.AsSpan(2, 2), CultureInfo.InvariantCulture);
        int day = int.Parse(id.AsSpan(4, 2), CultureInfo.InvariantCulture);

        if (month is < 1 or > 12)
        {
            return false;
        }

        return IsValidDay(1900 + year, month, day) || IsValidDay(2000 + year, month, day);
    }

    private static bool IsValidDay(int year, int month, int day) =>
        day >= 1 && day <= DateTime.DaysInMonth(year, month);

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

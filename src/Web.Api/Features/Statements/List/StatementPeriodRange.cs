namespace Web.Api.Features.Statements.List;

// Preset period filters for the statement list. Each preset resolves server-side to an inclusive
// YYYY-MM window of the last N completed months, ending with the previous month (the current,
// incomplete month is excluded). Custom uses the caller-supplied PeriodFrom/PeriodTo instead — a
// single exact month is just Custom with equal bounds.
public enum StatementPeriodRange
{
    LastMonth = 1,
    Last3Months = 2,
    Last6Months = 3,
    Last12Months = 4,
    Custom = 5
}

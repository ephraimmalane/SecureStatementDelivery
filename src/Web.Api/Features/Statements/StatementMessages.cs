namespace Web.Api.Features.Statements;

internal static class StatementMessages
{
    // Customer-facing hint telling them how to open the protected PDF. Single source of truth so
    // the statement list, statement detail, and the "new statement available" notification all say
    // the same thing. The ID number itself is never included — only that it is the password.
    public const string PasswordHint =
        "This statement is password-protected. Use your 13-digit South African ID number as the " +
        "password to open the PDF.";
}

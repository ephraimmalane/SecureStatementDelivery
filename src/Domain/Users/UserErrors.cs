using SharedKernel;

namespace Domain.Users;

public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        "Users.NotFound",
        $"User '{userId}' was not found.");

    public static readonly Error NotFoundByEmail = Error.NotFound(
        "Users.NotFoundByEmail",
        "No account was found with the provided credentials.");

    public static readonly Error InvalidCredentials = Error.Failure(
        "Users.InvalidCredentials",
        "The email or password is incorrect.");

    public static readonly Error EmailNotUnique = Error.Conflict(
        "Users.EmailNotUnique",
        "An account with this email address already exists.");

    public static readonly Error AccountInactive = Error.Failure(
        "Users.AccountInactive",
        "This account has been deactivated. Please contact support.");

    public static readonly Error InvalidRefreshToken = Error.Failure(
        "Users.InvalidRefreshToken",
        "The refresh token is invalid or has expired.");

    public static readonly Error InvalidIdNumber = Error.Problem(
        "Users.InvalidIdNumber",
        "The South African ID number is not valid.");
}

using Domain.Users;
using Domain.Users.Identity;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.Users.Identity;

public class IdentityDocumentTests
{
    private sealed class FakeValidator(IdentityDocumentType type, bool isValid) : IIdentityDocumentValidator
    {
        public IdentityDocumentType Type => type;

        public bool IsValid(string? documentNumber) => isValid;
    }

    private static IdentityDocumentValidatorResolver ResolverWith(params IIdentityDocumentValidator[] validators) =>
        new(validators);

    [Fact]
    public void Resolve_Should_Return_Validator_For_Registered_Type()
    {
        FakeValidator saValidator = new(IdentityDocumentType.SouthAfricanId, isValid: true);
        IdentityDocumentValidatorResolver resolver = ResolverWith(saValidator);

        resolver.Resolve(IdentityDocumentType.SouthAfricanId).ShouldBe(saValidator);
    }

    [Fact]
    public void Resolve_Should_Throw_For_Unregistered_Type()
    {
        IdentityDocumentValidatorResolver resolver = ResolverWith();

        Should.Throw<NotSupportedException>(() => resolver.Resolve(IdentityDocumentType.SouthAfricanId));
    }

    [Fact]
    public void Create_Should_Return_Success_And_Trim_When_Valid()
    {
        IdentityDocumentValidatorResolver resolver =
            ResolverWith(new FakeValidator(IdentityDocumentType.SouthAfricanId, isValid: true));

        Result<IdentityDocument> result = IdentityDocument.Create(
            IdentityDocumentType.SouthAfricanId,
            "  8001015009087  ",
            resolver);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Type.ShouldBe(IdentityDocumentType.SouthAfricanId);
        result.Value.Value.ShouldBe("8001015009087");
    }

    [Fact]
    public void Create_Should_Return_Failure_When_Invalid()
    {
        IdentityDocumentValidatorResolver resolver =
            ResolverWith(new FakeValidator(IdentityDocumentType.SouthAfricanId, isValid: false));

        Result<IdentityDocument> result = IdentityDocument.Create(
            IdentityDocumentType.SouthAfricanId,
            "not-an-id",
            resolver);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidIdentityDocument);
    }
}

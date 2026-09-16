using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel;
using Shouldly;
using Web.Api.Infrastructure;

namespace Web.Api.UnitTests.Infrastructure;

public sealed class CustomResultsTests
{
    [Fact]
    public void Problem_Should_Map_ForbiddenError_To403()
    {
        var result = Result.Failure(Error.Forbidden("Some.AccessDenied", "You do not have access."));

        IResult problem = CustomResults.Problem(result);

        ProblemHttpResult httpResult = problem.ShouldBeOfType<ProblemHttpResult>();
        httpResult.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        httpResult.ProblemDetails.Title.ShouldBe("Some.AccessDenied");
        httpResult.ProblemDetails.Detail.ShouldBe("You do not have access.");
    }
}

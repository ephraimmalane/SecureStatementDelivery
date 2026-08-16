namespace Web.Api.Features;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

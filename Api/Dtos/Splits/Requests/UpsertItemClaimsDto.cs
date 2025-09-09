using Api.Dtos.Splits.Common;

namespace Api.Dtos.Splits.Requests
{
    public sealed record UpsertItemClaimsDto(IReadOnlyList<ItemClaimDto> Claims);
}

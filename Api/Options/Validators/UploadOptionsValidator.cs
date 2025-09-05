using Microsoft.Extensions.Options;
using System.IO;

namespace Api.Options.Validators;

public sealed class UploadOptionsValidator : IValidateOptions<UploadOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadOptions options)
    {
        if (Path.IsPathRooted(options.RootFolder))
            return ValidateOptionsResult.Fail("RootFolder must be a relative path (not absolute).");

        if (options.PublicRequestPath.EndsWith("/") && options.PublicRequestPath != "/")
            return ValidateOptionsResult.Fail("PublicRequestPath should not end with a trailing slash.");

        return ValidateOptionsResult.Success;
    }
}

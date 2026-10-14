using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Qizhu.Infrastructure.Identity;

public sealed class FrameworkLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name?.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
    public string? NormalizeEmail(string? email) => email?.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
}

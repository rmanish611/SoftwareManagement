using AwesomeAssertions;
using SoftwareManagement.Application.Common;

namespace SoftwareManagement.Application.Tests.Common;

/// <summary>Boundary tests for BR-SITE-02: lowercase, 3 to 120 characters, no double hyphen.</summary>
public sealed class SlugTests
{
    [Theory]
    [InlineData("Hospital Management System", "hospital-management-system")]
    [InlineData("  ERP  for   SMBs  ", "erp-for-smbs")]
    [InlineData("Billing & Invoicing 2.0", "billing-invoicing-2-0")]
    public void From_ProducesAUrlSafeSlug(string input, string expected)
    {
        Slug.From(input).Should().Be(expected);
    }

    [Fact]
    public void From_FoldsAccentsInsteadOfDroppingTheWord()
    {
        Slug.From("Café Manager").Should().Be("cafe-manager");
    }

    [Fact]
    public void From_ReturnsEmpty_WhenNothingUsableRemains()
    {
        Slug.From("!!! ???").Should().BeEmpty("the caller must reject this rather than persist an empty slug");
    }

    [Fact]
    public void From_TruncatesAtTheMaximumLengthWithoutLeavingATrailingHyphen()
    {
        var input = string.Join(' ', Enumerable.Repeat("management", 30));

        var slug = Slug.From(input);

        slug.Length.Should().BeLessThanOrEqualTo(Slug.MaxLength);
        slug.Should().NotEndWith("-");
    }

    [Theory]
    [InlineData("erp")]
    [InlineData("hospital-management-system")]
    [InlineData("v2-billing")]
    public void IsValid_AcceptsAWellFormedSlug(string slug)
    {
        Slug.IsValid(slug).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("Upper-Case")]
    [InlineData("has space")]
    [InlineData("under_score")]
    public void IsValid_RejectsAMalformedSlug(string? slug)
    {
        Slug.IsValid(slug).Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsASlugLongerThanTheColumn()
    {
        var tooLong = new string('a', Slug.MaxLength + 1);

        Slug.IsValid(tooLong).Should().BeFalse("the column is nvarchar(120) and a longer value would be truncated silently");
    }
}

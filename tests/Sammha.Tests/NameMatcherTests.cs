using Sammha.Domain;

namespace Sammha.Tests;

public class NameMatcherTests
{
    [Theory]
    [InlineData("الشَّامخ", "شامخ")]
    [InlineData("مدعث", "مدعث")]
    [InlineData("أصايل", "اصايل")]
    [InlineData("غزلانة", "غزلانه")]
    [InlineData("ريم البيداء", "ريمالبيداء")]
    public void Normalizes_like_the_front_end(string input, string expected) => Assert.Equal(expected, NameMatcher.Normalize(input));

    [Fact]
    public void Contained_names_are_similar()
    {
        Assert.True(NameMatcher.Similarity(NameMatcher.Normalize("شاهين"), NameMatcher.Normalize("شاهينه")) >= 0.8);
        Assert.Equal(1, NameMatcher.Similarity("مدعث", "مدعث"));
        Assert.True(NameMatcher.Similarity("مدعث", "سلطان") < 0.7);
    }

    [Theory]
    [InlineData("", "اكتب الاسم")]
    [InlineData("ا", "الاسم قصير جدًا")]
    [InlineData("Ahmed", "استخدم الحروف العربية فقط")]
    public void Validates(string input, string message) => Assert.Equal(message, NameMatcher.Validate(input));

    [Theory]
    [InlineData("0551234567", "0551234567")]
    [InlineData("+966551234567", "0551234567")]
    [InlineData("٠٥٥١٢٣٤٥٦٧", "0551234567")]
    [InlineData("551234567", "0551234567")]
    public void Normalizes_phones(string input, string expected) => Assert.Equal(expected, NameMatcher.NormalizePhone(input));

    [Fact]
    public void Masks_names() => Assert.Equal("فهد الد…", NameMatcher.MaskName("فهد عبدالله الدوسري"));
}

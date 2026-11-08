using Atlas.App.Text;
using Atlas.Core.Validation;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.App.Tests;

public sealed class TextTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0 ressource")]
    [InlineData(1, "1 ressource")]
    [InlineData(2, "2 ressources")]
    public void ResourceCount_UsesFrenchPlural(int count, string expected) =>
        Assert.Equal(expected, UiText.ResourceCount(count));

    [Fact]
    public void Modified_SaysTodayWithTheTime() =>
        Assert.Equal("Modifié aujourd'hui à 09:05", DateText.Modified(new DateTimeOffset(2026, 11, 6, 9, 5, 0, TimeSpan.Zero), Clock()));

    [Fact]
    public void Modified_SaysYesterdayWithTheTime() =>
        Assert.Equal("Modifié hier à 23:59", DateText.Modified(new DateTimeOffset(2026, 11, 5, 23, 59, 0, TimeSpan.Zero), Clock()));

    [Fact]
    public void Modified_GivesTheFullDateBeforeThat() =>
        Assert.Equal("Modifié le 4 novembre 2026", DateText.Modified(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero), Clock()));

    [Fact]
    public void Modified_WritesTheFirstOfTheMonthTheFrenchWay() =>
        Assert.Equal("Modifié le 1er mars 2026", DateText.Modified(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero), Clock()));

    [Fact]
    public void Dates_AreFrenchWhateverTheSystemLanguage()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("Créé le 4 août 2026", DateText.Created(new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero), Clock()));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Dates_UseTheLocalDayNotTheUtcDay()
    {
        var clock = Clock();
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2"));

        // 23:30 UTC on the 5th is already 01:30 on the 6th for this user, the same day as "now".
        Assert.Equal("Modifié aujourd'hui à 01:30", DateText.Modified(new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero), clock));
    }

    [Theory]
    [InlineData(ValidationField.WorkspaceName, ValidationCode.Required, "Donnez un nom")]
    [InlineData(ValidationField.WorkspaceName, ValidationCode.TooLong, "120")]
    [InlineData(ValidationField.WorkspaceName, ValidationCode.ControlCharacters, "saut de ligne")]
    [InlineData(ValidationField.WorkspaceDescription, ValidationCode.TooLong, "2000")]
    [InlineData(ValidationField.WorkspaceNotes, ValidationCode.TooLong, "1000000")]
    public void Validation_ExplainsEachWorkspaceRule(ValidationField field, ValidationCode code, string expected) =>
        Assert.Contains(expected, ValidationText.Describe(new ValidationError(field, code)), StringComparison.Ordinal);

    [Fact]
    public void Validation_StillSaysSomethingForACombinationWithoutItsOwnMessage() =>
        Assert.Equal(
            "Cette valeur n'est pas acceptée.",
            ValidationText.Describe(new ValidationError(ValidationField.WorkspaceName, ValidationCode.InvalidPath)));

    private static FakeTimeProvider Clock() => new(Noon);
}

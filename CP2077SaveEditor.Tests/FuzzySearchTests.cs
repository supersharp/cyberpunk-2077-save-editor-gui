using CP2077SaveEditor.Utils;
using Xunit;

namespace CP2077SaveEditor.Tests
{
    public class FuzzySearchTests
    {
        [Theory]
        [InlineData("Mantis Blades", "mantis", true)]
        [InlineData("Mantis Blades", "blades", true)]
        [InlineData("Items.Preset_Overture_Default", "overture", true)]
        [InlineData("Items.Preset_Overture_Default", "default", true)]
        public void Matches_ExactSubstring_ReturnsTrue(string text, string query, bool expected)
        {
            Assert.Equal(expected, FuzzySearch.Matches(text, query));
        }

        [Theory]
        [InlineData("Mantis Blades Legendary", "mantis blades", true)]
        [InlineData("Items.Preset_Overture_Default", "overture preset", true)]
        [InlineData("Items.Preset_Overture_Default", "overture nonexisting", false)]
        public void Matches_MultiTerm_MatchesAllTerms(string text, string query, bool expected)
        {
            Assert.Equal(expected, FuzzySearch.Matches(text, query));
        }

        [Theory]
        [InlineData("Katana", "katna", true)]          // transposition / omission
        [InlineData("Sniper Rifle", "snipr", true)]     // 1 deletion
        [InlineData("Johnny Silverhand", "Silvrhand", true)] // typo in name
        [InlineData("Mantis Blades", "Mantis Bldaes", true)] // transposition in blades
        [InlineData("Revolver", "revovler", true)]      // transposition
        public void Matches_TypoTolerance_ReturnsTrue(string text, string query, bool expected)
        {
            Assert.Equal(expected, FuzzySearch.Matches(text, query));
        }

        [Theory]
        [InlineData("Mantis Blades", "mb", true)]
        [InlineData("Items.Preset_Overture", "po", true)]
        [InlineData("Point of No Return", "ponr", true)]
        public void Matches_Acronym_ReturnsTrue(string text, string query, bool expected)
        {
            Assert.Equal(expected, FuzzySearch.Matches(text, query));
        }

        [Theory]
        [InlineData("Preset_Overture", "ovrt", true)]
        [InlineData("MantisBlades", "mnts", true)]
        public void Matches_Subsequence_ReturnsTrue(string text, string query, bool expected)
        {
            Assert.Equal(expected, FuzzySearch.Matches(text, query));
        }

        [Theory]
        [InlineData("", "", true)]
        [InlineData("Any string", "", true)]
        [InlineData("Any string", "   ", true)]
        [InlineData(null, "search", false)]
        public void Matches_NullOrEmpty_HandledGracefully(string? text, string? query, bool expected)
        {
            Assert.Equal(expected, FuzzySearch.Matches(text, query));
        }

        [Fact]
        public void MatchesAny_MultipleFields_ReturnsTrueIfMatchedInAny()
        {
            var match = FuzzySearch.MatchesAny("panam", "ManualSave-1", "sq030_rider", "With A Little Help From My Friends", "Nomad");
            // "panam" matches or typos
            Assert.False(match); // not in this save

            var match2 = FuzzySearch.MatchesAny("nomad friends", "ManualSave-1", "sq030_rider", "With A Little Help From My Friends", "Nomad");
            Assert.True(match2); // "nomad" in lifepath, "friends" in quest title!
        }

        [Fact]
        public void Score_RanksExactAndPrefixHigherThanFuzzy()
        {
            var exactScore = FuzzySearch.Score("q101_done", "q101");
            var fuzzyScore = FuzzySearch.Score("sq101_done", "q101");
            var typoScore = FuzzySearch.Score("q102_done", "q101");

            Assert.True(exactScore > fuzzyScore);
            Assert.True(fuzzyScore > typoScore);
        }
    }
}

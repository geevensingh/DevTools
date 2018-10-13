using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using System;

namespace IdParser.Tests
{
    [TestClass()]
    public class PublicIdParserTests
    {
        [DataTestMethod]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d", "d6d64cae9b4074b5c02f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  ", "   d6d64cae9b4074b5c02f574d12de535f   ")]
        [DataRow("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f", "")]
        [DataRow("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f/    ", "")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b4074b5c02f574d12de535f", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b4074b5c02f574d12de535f   ", "")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b4074b5c02f574d12de535f", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b4074b5c02f574d12de535f   ", "")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b4074b5c02f574d12de535f", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d     d6d64cae9b4074b5c02f574d12de535f   ", "")]
        public void TryParseTest_Passing_WellFormed(string accountId, string scheduleId)
        {
            PublicIdParser.TryParse(ref accountId, ref scheduleId, out PublicIdParser publicIdParser).Should().BeTrue();
            publicIdParser.ViewBag.ErrorMessage.Should().BeEmpty();
            publicIdParser.ViewBag.WarningMessage.Should().BeEmpty();
            Guid.ParseExact(accountId, "D").ToString("D").Should().Be(accountId);
            Guid.ParseExact(scheduleId, "N").ToString("N").Should().Be(scheduleId);
        }

        [DataTestMethod]
        [DataRow("aaaa", "bbbb")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd72jhg2a0d", "d6d64cae9b4074b5c02f574d12de535f")]
        [DataRow("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f/    ", "d6d64cae9b40702f574d12de535f")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f")]
        public void TryParseTest_Warning_AccountId_Invalid(string accountId, string scheduleId)
        {
            PublicIdParser.TryParse(ref accountId, ref scheduleId, out PublicIdParser publicIdParser).Should().BeTrue();
            publicIdParser.ViewBag.ErrorMessage.Should().BeEmpty();
            publicIdParser.ViewBag.WarningMessage.Should().Be($"AccountId does not seem to be in the right format: {accountId}");
        }

        [DataTestMethod]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  ", "   d6d64cae9b40702f574d12de535f   ")]
        [DataRow("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b40702f574d12de535f", "")]
        [DataRow("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b40702f574d12de535f/    ", "")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b40702f574d12de535f", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b40702f574d12de535f   ", "")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b40702f574d12de535f", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b40702f574d12de535f   ", "")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b40702f574d12de535f", "")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b40702f574d12de535f   ", "")]
        public void TryParseTest_Warning_ScheduleId_Invalid(string accountId, string scheduleId)
        {
            PublicIdParser.TryParse(ref accountId, ref scheduleId, out PublicIdParser publicIdParser).Should().BeTrue();
            publicIdParser.ViewBag.ErrorMessage.Should().BeEmpty();
            publicIdParser.ViewBag.WarningMessage.Should().Be($"ScheduleId does not seem to be in the right format: {scheduleId}");
        }

        [DataTestMethod]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd72jhg2a0d", "d6d64cae9b4074b5c02f574d12de535f")]
        [DataRow("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f/    ", "d6d64cae9b40702f574d12de535f")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f")]
        [DataRow("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f")]
        [DataRow("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f")]
        public void TryParseTest_Warning_Too_Much_Data(string accountId, string scheduleId)
        {
            PublicIdParser.TryParse(ref accountId, ref scheduleId, out PublicIdParser publicIdParser).Should().BeTrue();
            publicIdParser.ViewBag.ErrorMessage.Should().BeEmpty();
            publicIdParser.ViewBag.WarningMessage.Should().Be($"AccountId does not seem to be in the right format: {accountId}");
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("    ")]
        [DataRow("\t")]
        [DataRow("\t\t\t")]
        [DataRow("\r")]
        [DataRow("\r\r\r")]
        [DataRow("\n")]
        [DataRow("\n\n\n")]
        [DataRow("\r\n")]
        [DataRow("\r\n\r\n\r\n")]
        [DataRow(",")]
        [DataRow(",,,")]
        [DataRow("/")]
        [DataRow("///")]
        public void TryParseTest_Failing_NothingFound(string input)
        {
            string accountId = input;
            string scheduleId = string.Empty;
            PublicIdParser.TryParse(ref accountId, ref scheduleId, out PublicIdParser publicIdParser).Should().BeFalse();
            publicIdParser.ViewBag.ErrorMessage.Should().Be("AccountId and ScheduleId are both empty.  No data found.");
            publicIdParser.ViewBag.WarningMessage.Should().BeEmpty();

            accountId = string.Empty;
            scheduleId = input;
            PublicIdParser.TryParse(ref accountId, ref scheduleId, out publicIdParser).Should().BeFalse();
            publicIdParser.ViewBag.ErrorMessage.Should().Be("AccountId and ScheduleId are both empty.  No data found.");
            publicIdParser.ViewBag.WarningMessage.Should().BeEmpty();
        }
    }
}
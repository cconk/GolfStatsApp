using System.Text;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Tests;

public class DraftOrderTests
{
    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 3, 2)]
    [InlineData(5, 3, 0)]
    [InlineData(6, 3, 0)]
    [InlineData(8, 3, 2)]
    public void SnakeOrder_ReversesEveryOtherRound(int pick, int members, int expected) =>
        Assert.Equal(expected, DraftOrder.MemberIndexForPick(pick, members));

    [Fact]
    public void NoMembers_ReturnsMinusOne() => Assert.Equal(-1, DraftOrder.MemberIndexForPick(0, 0));
}

public class ExportServiceTests
{
    private static readonly string[] Headers = { "Name", "Score" };

    [Fact]
    public void Csv_EscapesCommasAndQuotes()
    {
        var bytes = ExportService.ToCsv(Headers, new[] { (IReadOnlyList<object?>)new object?[] { "Smith, \"Bob\"", 1.5 } });
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        Assert.Equal("Name,Score\r\n\"Smith, \"\"Bob\"\"\",1.5\r\n".Replace("\r\n", Environment.NewLine), text);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("-5", "-5")]
    [InlineData("plain", "plain")]
    public void Csv_NeutralisesFormulaInjection(string input, string expected) =>
        Assert.Equal(expected, ExportService.Escape(input));

    [Fact]
    public void Xlsx_ProducesZipFile()
    {
        var bytes = ExportService.ToXlsx("Sheet", Headers, new[] { (IReadOnlyList<object?>)new object?[] { "A", 2 } });

        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }
}

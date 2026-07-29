using System.IO;
using SAUDICO.Federate.Shared;
using Xunit;

namespace SAUDICO.Federate.Tests;

public sealed class PathHelperTests
{
    [Fact]
    public void Output_ReplacesRvtExtensionWithNwc()
    {
        string inputPath = @"C:\Models\Tower A.rvt";
        string outputDirectory = @"D:\Export";

        string actual = PathHelper.Output(inputPath, outputDirectory);
        string expected = Path.Combine(outputDirectory, "Tower A.nwc");

        Assert.Equal(expected, actual);
    }
}
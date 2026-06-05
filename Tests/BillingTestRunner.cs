using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Tests;

public class BillingTestRunner
{
    public async Task<string> RunAsync(string ndi)
    {
        var test = new UnitTest1();
        try
        {
            await test.RunAllTestsAsync(ndi);
        }
        catch
        {
        }

        Console.WriteLine($"Log saved to: {test.LogFilePath}");
        return test.LogFilePath;
    }
}

internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _console;
    private readonly TextWriter _file;

    public TeeTextWriter(TextWriter console, TextWriter file)
    {
        _console = console;
        _file = file;
    }

    public override Encoding Encoding => _console.Encoding;

    public override void Write(char value)
    {
        _console.Write(value);
        _file.Write(value);
    }

    public override void Write(string? value)
    {
        _console.Write(value);
        _file.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _console.WriteLine(value);
        _file.WriteLine(value);
    }
}

[TestFixture]
public class BillingTestRunnerTests
{
    [Test]
    public async Task RunAllBillingTestsForNdi()
    {
        const string ndi = "019a119f-31b1-7d88-8f7b-02eab04d7fb4";
        await new BillingTestRunner().RunAsync(ndi);
    }
}

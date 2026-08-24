using HotKeyViewer.Services;
using Xunit;

namespace HotKeyViewer.Tests;

public class EditorLauncherTests
{
    [Theory]
    [InlineData("code", new[] { "-g", "/etc/hypr.conf:42" })]
    [InlineData("/usr/bin/codium", new[] { "-g", "/etc/hypr.conf:42" })]
    [InlineData("zed", new[] { "/etc/hypr.conf:42" })]
    [InlineData("hx", new[] { "/etc/hypr.conf:42" })]
    [InlineData("nvim", new[] { "+42", "/etc/hypr.conf" })]
    [InlineData("nano", new[] { "+42", "/etc/hypr.conf" })]
    [InlineData("rider", new[] { "--line", "42", "/etc/hypr.conf" })]
    public void BuildsTheGoToLineArgumentsEachEditorUnderstands(string editor, string[] expected)
    {
        Assert.Equal(expected, EditorLauncher.ArgumentsFor(editor, "/etc/hypr.conf", 42));
    }

    [Fact]
    public void AnUnknownEditorStillGetsTheRightFile()
    {
        // Opening the correct file is most of the value; a wrong flag would
        // just make the editor refuse to start.
        Assert.Equal(["/etc/hypr.conf"], EditorLauncher.ArgumentsFor("acme", "/etc/hypr.conf", 42));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoKnownLineMeansJustOpenTheFile(int line)
    {
        Assert.Equal(["/etc/hypr.conf"], EditorLauncher.ArgumentsFor("nvim", "/etc/hypr.conf", line));
    }

    [Fact]
    public void TheOmarchyLauncherIsNeverTreatedAsTheEditorItself()
    {
        // $EDITOR is "omarchy-launch-editor --inline" on Omarchy. Taking that at
        // face value would ask the launcher to open files with itself.
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        try
        {
            Environment.SetEnvironmentVariable("EDITOR", "omarchy-launch-editor --inline");

            Assert.DoesNotContain("omarchy-launch-editor", EditorLauncher.ResolveEditor());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EDITOR", editor);
        }
    }
}

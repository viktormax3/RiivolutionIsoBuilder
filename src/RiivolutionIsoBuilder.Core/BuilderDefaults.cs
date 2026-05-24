namespace RiivolutionIsoBuilder;

public static class BuilderDefaults
{
    public static IReadOnlyList<string> OutputExtensions { get; } = ["wbfs", "iso", "ciso", "wdf", "wia"];

    public static IReadOnlyList<string> InputImageExtensions { get; } = ["iso", "wbfs", "ciso", "wdf", "wia"];
}
